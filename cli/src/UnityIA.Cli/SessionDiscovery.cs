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
        out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(project))
        {
            string expected = Path.GetFullPath(project);
            LiveSessionDescriptor[] matches = sessions
                .Where(session => string.Equals(
                    Path.GetFullPath(session.ProjectPath),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }

            error = matches.Length == 0
                ? "No live UnityIA session matches the requested project."
                : "Multiple live sessions match the requested project.";
            return null;
        }

        if (sessions.Count == 1)
        {
            return sessions[0];
        }

        error = sessions.Count == 0
            ? "No live UnityIA session was found."
            : "Multiple sessions are live; specify --project.";
        return null;
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

