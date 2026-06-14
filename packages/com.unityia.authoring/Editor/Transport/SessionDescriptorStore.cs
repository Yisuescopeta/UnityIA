using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Transport
{
    internal static class SessionDescriptorStore
    {
        public static string Write(int port, string token)
        {
            string directory = GetSessionsDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, ProjectKey() + ".json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            LiveSessionDescriptor descriptor = new LiveSessionDescriptor
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                SessionId = EditorSession.SessionId,
                ProjectPath = ProjectPaths.ProjectRoot,
                ProcessId = Process.GetCurrentProcess().Id,
                Port = port,
                Token = token,
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            File.WriteAllText(
                temporary,
                JsonConvert.SerializeObject(descriptor, Formatting.Indented),
                new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
            return path;
        }

        public static void Delete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Stale descriptors are ignored by clients after checking the PID.
            }
        }

        private static string GetSessionsDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UnityIA",
                "sessions");
        }

        private static string ProjectKey()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                ProjectPaths.ProjectRoot.ToUpperInvariant());
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(32);
                for (int index = 0; index < 16; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}

