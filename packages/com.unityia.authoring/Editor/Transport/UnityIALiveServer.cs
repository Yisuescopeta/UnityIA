using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Transport
{
    [InitializeOnLoad]
    internal static class UnityIALiveServer
    {
        private const int MaximumPayloadBytes = 1024 * 1024;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static HttpListener listener;
        private static Thread listenerThread;
        private static string bearerToken;
        private static string descriptorPath;
        private static volatile bool stopping;

        static UnityIALiveServer()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (listener != null)
            {
                return;
            }

            try
            {
                int port = ReserveEphemeralPort();
                bearerToken = CreateToken();
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();
                descriptorPath = SessionDescriptorStore.Write(port, bearerToken);
                stopping = false;
                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "UnityIA Live HTTP"
                };
                listenerThread.Start();
            }
            catch (Exception exception)
            {
                Stop();
                Debug.LogError("UnityIA live server failed to start: " + exception.Message);
            }
        }

        private static void Stop()
        {
            stopping = true;
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch
            {
                // Shutdown is best-effort during reload and Editor exit.
            }

            listener = null;
            SessionDescriptorStore.Delete(descriptorPath);
            descriptorPath = null;
            bearerToken = null;
        }

        private static void ListenLoop()
        {
            while (!stopping && listener != null && listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(context));
                }
                catch (HttpListenerException)
                {
                    if (!stopping)
                    {
                        Debug.LogWarning("UnityIA live listener stopped unexpectedly.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UnityIA live listener error: " + exception.Message);
                }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            try
            {
                if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
                {
                    Write(
                        context,
                        403,
                        Results.Error(
                            ResultCodes.PermissionDenied,
                            "Only loopback connections are allowed."));
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    Write(
                        context,
                        401,
                        Results.Error(
                            ResultCodes.PermissionDenied,
                            "A valid bearer token is required."));
                    return;
                }

                string path = context.Request.Url.AbsolutePath.TrimEnd('/');
                if (path.Length == 0)
                {
                    path = "/";
                }

                if (context.Request.HttpMethod == "GET" && path == "/status")
                {
                    ExecuteQueued(context, CreateReadEnvelope("system.status"));
                    return;
                }

                if (context.Request.HttpMethod == "GET" && path == "/commands")
                {
                    ExecuteQueued(context, CreateReadEnvelope("system.commands.list"));
                    return;
                }

                if (context.Request.HttpMethod == "POST" && path == "/execute")
                {
                    string json;
                    string readError;
                    if (!TryReadBody(context.Request, out json, out readError))
                    {
                        Write(
                            context,
                            413,
                            Results.Error(ResultCodes.InvalidJson, readError));
                        return;
                    }

                    ExecuteQueued(
                        context,
                        () => CoreServices.Dispatcher.ExecuteJson(json));
                    return;
                }

                Write(
                    context,
                    404,
                    Results.Error(ResultCodes.InvalidCommand, "Unknown endpoint."));
            }
            catch (Exception exception)
            {
                Write(
                    context,
                    500,
                    Results.Error(ResultCodes.InternalError, exception.Message));
            }
        }

        private static void ExecuteQueued(
            HttpListenerContext context,
            CommandEnvelope envelope)
        {
            ExecuteQueued(context, () => CoreServices.Dispatcher.Execute(envelope));
        }

        private static void ExecuteQueued(
            HttpListenerContext context,
            Func<ActionResult<JObject>> operation)
        {
            Task<ActionResult<JObject>> task = MainThreadCommandQueue.Enqueue(operation);
            if (!task.Wait(RequestTimeout))
            {
                Write(
                    context,
                    504,
                    Results.Error(
                        ResultCodes.RequestTimeout,
                        "The client wait timed out. Retry with the same commandId."));
                return;
            }

            Write(context, task.Result.Success ? 200 : 400, task.Result);
        }

        private static bool TryReadBody(
            HttpListenerRequest request,
            out string json,
            out string error)
        {
            json = null;
            error = null;
            if (request.ContentLength64 > MaximumPayloadBytes)
            {
                error = "The request exceeds the 1 MiB payload limit.";
                return false;
            }

            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int total = 0;
                int read;
                while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    if (total > MaximumPayloadBytes)
                    {
                        error = "The request exceeds the 1 MiB payload limit.";
                        return false;
                    }

                    buffer.Write(chunk, 0, read);
                }

                json = Encoding.UTF8.GetString(buffer.ToArray());
                return true;
            }
        }

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            string header = request.Headers["Authorization"];
            return !string.IsNullOrWhiteSpace(bearerToken) &&
                   string.Equals(
                       header,
                       "Bearer " + bearerToken,
                       StringComparison.Ordinal);
        }

        private static void Write(
            HttpListenerContext context,
            int status,
            ActionResult<JObject> result)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(
                    CommandJson.Serialize(result, Formatting.None));
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentEncoding = Encoding.UTF8;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                // The client may have disconnected after its timeout.
            }
        }

        private static CommandEnvelope CreateReadEnvelope(string command)
        {
            return new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = command,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = new JObject(),
                Options = new CommandOptions()
            };
        }

        private static int ReserveEphemeralPort()
        {
            TcpListener reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            return port;
        }

        private static string CreateToken()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }
    }
}

