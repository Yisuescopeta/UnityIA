using System.Text.Json;
using UnityIA.Protocol;

namespace UnityIA.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Console.Out.WriteLine(
                ResultWriter.Error("INTERNAL_ERROR", "CLI failure: " + exception.Message));
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length >= 2 && args[0] == "session" && args[1] == "list")
        {
            return ListSessions();
        }

        if (args.Length == 0 || args[0] is not ("status" or "commands" or "execute"))
        {
            Console.Error.WriteLine(
                "Usage: unityia session list | status [--project PATH] | commands [--project PATH] | execute --file FILE [--project PATH]");
            Console.Out.WriteLine(
                ResultWriter.Error("INVALID_COMMAND", "Invalid CLI command."));
            return 2;
        }

        string? project = ReadOption(args, "--project");
        IReadOnlyList<LiveSessionDescriptor> sessions = SessionDiscovery.FindLiveSessions();
        LiveSessionDescriptor? session = SessionDiscovery.Select(sessions, project, out string? error);
        if (session is null)
        {
            Console.Out.WriteLine(ResultWriter.Error("TARGET_NOT_FOUND", error!));
            return 1;
        }

        using LiveClient client = new(session);
        HttpResponseMessage response;
        switch (args[0])
        {
            case "status":
                response = await client.GetStatusAsync().ConfigureAwait(false);
                break;
            case "commands":
                response = await client.GetCommandsAsync().ConfigureAwait(false);
                break;
            case "execute":
                string? file = ReadOption(args, "--file");
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    Console.Out.WriteLine(
                        ResultWriter.Error("INVALID_COMMAND", "--file must reference an existing command JSON file."));
                    return 2;
                }

                string json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                JsonSchemaCommandValidator validator = new(
                    Path.Combine(AppContext.BaseDirectory, "schemas", "v0.1"));
                SchemaValidationResult validation =
                    await validator.ValidateAsync(json).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    Console.Out.WriteLine(
                        ResultWriter.Error(
                            "VALIDATION_FAILED",
                            "Command JSON does not satisfy the v0.1 schema.",
                            new
                            {
                                command = validation.Command,
                                errors = validation.Errors,
                                warnings = Array.Empty<string>()
                            }));
                    return 1;
                }

                response = await client.ExecuteAsync(json).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("Unreachable CLI command.");
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.Out.WriteLine(body);
        return IsSuccessfulActionResult(body) ? 0 : 1;
    }

    private static int ListSessions()
    {
        object[] sessions = SessionDiscovery.FindLiveSessions()
            .Select(session => new
            {
                protocolVersion = session.ProtocolVersion,
                sessionId = session.SessionId,
                projectPath = session.ProjectPath,
                processId = session.ProcessId,
                port = session.Port,
                startedAtUtc = session.StartedAtUtc
            })
            .Cast<object>()
            .ToArray();
        Console.Out.WriteLine(
            ResultWriter.Success(
                "Live UnityIA sessions.",
                new { sessions, warnings = Array.Empty<string>() }));
        return 0;
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == option && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool IsSuccessfulActionResult(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("success", out JsonElement success) &&
                   success.ValueKind == JsonValueKind.True;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine("Unity returned invalid ActionResult JSON: " + exception.Message);
            return false;
        }
    }
}

