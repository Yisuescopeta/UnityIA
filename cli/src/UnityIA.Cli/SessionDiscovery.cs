using System.Diagnostics;
using System.Text.Json;
using UnityIA.Protocol;

namespace UnityIA.Cli;

internal static class SessionDiscovery
{
    public static IReadOnlyList<LiveSessionDescriptor> FindLiveSessions()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnityIA",
            "sessions");
        if (!Directory.Exists(directory))
        {
            return Array.Empty<LiveSessionDescriptor>();
        }

        List<LiveSessionDescriptor> sessions = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                LiveSessionDescriptor? descriptor =
                    JsonSerializer.Deserialize<LiveSessionDescriptor>(File.ReadAllText(file));
                if (descriptor is not null &&
                    descriptor.ProtocolVersion == "0.1" &&
                    descriptor.Port is > 0 and <= 65535 &&
                    IsRunning(descriptor.ProcessId))
                {
                    sessions.Add(descriptor);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Ignoring invalid session descriptor '{file}': {exception.Message}");
            }
        }

        return sessions
            .OrderBy(session => session.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static LiveSessionDescriptor? Select(
        IReadOnlyList<LiveSessionDescriptor> sessions,
        string? project,
        string? sessionId,
        out string? error)
    {
        error = null;
        IEnumerable<LiveSessionDescriptor> candidates = sessions;
        bool hasProject = !string.IsNullOrWhiteSpace(project);
        bool hasSessionId = !string.IsNullOrWhiteSpace(sessionId);

        if (hasSessionId)
        {
            candidates = candidates.Where(session => string.Equals(
                session.SessionId,
                sessionId,
                StringComparison.Ordinal));
        }

        if (hasProject)
        {
            if (!TryGetFullPath(project!, out string expected, out error))
            {
                return null;
            }

            candidates = candidates.Where(session =>
                TryGetFullPath(session.ProjectPath, out string actual, out _) &&
                string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase));
        }

        LiveSessionDescriptor[] matches = candidates.ToArray();
        if (hasProject || hasSessionId)
        {
            if (matches.Length == 1)
            {
                return matches[0];
            }

            error = matches.Length == 0
                ? "No live UnityIA session matches the requested selector."
                : "Multiple live UnityIA sessions match the requested selector.";
            return null;
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        error = matches.Length == 0
            ? "No live UnityIA session was found."
            : "Multiple sessions are live; specify --project or --session.";
        return null;
    }

    private static bool TryGetFullPath(string path, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Invalid project path: " + exception.Message;
            return false;
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
