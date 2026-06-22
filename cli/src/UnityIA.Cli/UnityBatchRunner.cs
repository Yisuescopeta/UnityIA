using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using UnityIA.Protocol;

namespace UnityIA.Cli;

internal interface IUnityBatchRunner
{
    Task<string> ExecuteCommandAsync(BatchCommandRequest request);

    Task<string> RunTestsAsync(BatchTestRequest request);
}

internal sealed record BatchCommandRequest(
    string UnityPath,
    string ProjectPath,
    string CommandFile,
    TimeSpan Timeout);

internal sealed record BatchTestRequest(
    string UnityPath,
    string ProjectPath,
    string Mode,
    string Suite,
    TimeSpan Timeout);

internal sealed class UnityBatchRunner : IUnityBatchRunner
{
    private const string BatchEntrypoint = "UnityIA.UnityIABatchEntrypoint.ExecuteCommand";

    public async Task<string> ExecuteCommandAsync(BatchCommandRequest request)
    {
        string runId = Guid.NewGuid().ToString("N");
        string directory = CreateBatchDirectory(runId);
        string resultPath = Path.Combine(directory, "result.json");
        string logPath = Path.Combine(directory, "unity.log");

        BatchProcessResult process = await RunUnityAsync(
            request.UnityPath,
            [
                "-batchmode",
                "-nographics",
                "-quit",
                "-projectPath",
                request.ProjectPath,
                "-executeMethod",
                BatchEntrypoint,
                "-unityiaCommandFile",
                request.CommandFile,
                "-unityiaResultFile",
                resultPath,
                "-logFile",
                logPath
            ],
            request.Timeout).ConfigureAwait(false);

        if (process.TimedOut)
        {
            return ResultWriter.Error(
                "REQUEST_TIMEOUT",
                "Unity batch command timed out.",
                new
                {
                    runId,
                    logPath,
                    resultPath,
                    timeoutSeconds = request.Timeout.TotalSeconds,
                    warnings = Array.Empty<string>()
                });
        }

        if (!File.Exists(resultPath))
        {
            return ResultWriter.Error(
                "UNITY_OPERATION_FAILED",
                "Unity batch command did not produce an ActionResult.",
                new
                {
                    runId,
                    exitCode = process.ExitCode,
                    logPath,
                    resultPath,
                    stderr = process.Stderr,
                    warnings = Array.Empty<string>()
                });
        }

        return await File.ReadAllTextAsync(resultPath).ConfigureAwait(false);
    }

    public async Task<string> RunTestsAsync(BatchTestRequest request)
    {
        string runId = Guid.NewGuid().ToString("N");
        string directory = CreateBatchDirectory(runId);
        string resultPath = Path.Combine(directory, "test-results.xml");
        string logPath = Path.Combine(directory, "unity.log");

        BatchProcessResult process = await RunUnityAsync(
            request.UnityPath,
            [
                "-batchmode",
                "-nographics",
                "-projectPath",
                request.ProjectPath,
                "-runTests",
                "-testPlatform",
                request.Mode,
                "-testResults",
                resultPath,
                "-logFile",
                logPath
            ],
            request.Timeout).ConfigureAwait(false);

        if (process.TimedOut)
        {
            return ResultWriter.Error(
                "REQUEST_TIMEOUT",
                "Unity test run timed out.",
                new
                {
                    runId,
                    suite = request.Suite,
                    mode = request.Mode,
                    logPath,
                    resultPath,
                    timeoutSeconds = request.Timeout.TotalSeconds,
                    warnings = Array.Empty<string>()
                });
        }

        if (!File.Exists(resultPath))
        {
            return ResultWriter.Error(
                "UNITY_OPERATION_FAILED",
                "Unity test run did not produce a test result file.",
                new
                {
                    runId,
                    suite = request.Suite,
                    mode = request.Mode,
                    exitCode = process.ExitCode,
                    logPath,
                    resultPath,
                    stderr = process.Stderr,
                    warnings = Array.Empty<string>()
                });
        }

        return UnityTestResultParser.ToActionResult(
            await File.ReadAllTextAsync(resultPath).ConfigureAwait(false),
            runId,
            request.Suite,
            request.Mode,
            resultPath,
            logPath,
            process.ExitCode);
    }

    private static async Task<BatchProcessResult> RunUnityAsync(
        string unityPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = unityPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        using CancellationTokenSource cts = new(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new BatchProcessResult(-1, true, string.Empty, string.Empty);
        }

        return new BatchProcessResult(
            process.ExitCode,
            false,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static string CreateBatchDirectory(string runId)
    {
        string directory = Path.Combine(Path.GetTempPath(), "UnityIA", "batch", runId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after timeout.
        }
    }

    private sealed record BatchProcessResult(
        int ExitCode,
        bool TimedOut,
        string Stdout,
        string Stderr);
}

internal static class UnityTestResultParser
{
    public static string ToActionResult(
        string xml,
        string runId,
        string suite,
        string mode,
        string resultPath,
        string logPath,
        int processExitCode)
    {
        try
        {
            XDocument document = XDocument.Parse(xml);
            XElement root = document.Root ?? throw new InvalidDataException("Missing XML root.");
            int total = ReadInt(root, "total", CountCases(root));
            int passed = ReadInt(root, "passed", CountCases(root, "Passed"));
            int failed = ReadInt(root, "failed", CountCases(root, "Failed"));
            int skipped = ReadInt(root, "skipped", CountSkipped(root));
            double duration = ReadDouble(root, "duration", 0d);
            object[] failures = ReadFailures(root);
            bool success = failed == 0 && processExitCode == 0;

            object data = new
            {
                runId,
                suite,
                mode,
                total,
                passed,
                failed,
                skipped,
                durationSeconds = duration,
                resultPath,
                logPath,
                processExitCode,
                failures,
                warnings = Array.Empty<string>()
            };

            return success
                ? ResultWriter.Success("Unity tests passed.", data)
                : ResultWriter.Error("UNITY_OPERATION_FAILED", "Unity tests failed.", data);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or System.Xml.XmlException)
        {
            return ResultWriter.Error(
                "INVALID_RESPONSE",
                "Unity test result XML could not be parsed.",
                new
                {
                    runId,
                    suite,
                    mode,
                    resultPath,
                    logPath,
                    parseError = exception.Message,
                    warnings = Array.Empty<string>()
                });
        }
    }

    private static object[] ReadFailures(XElement root)
    {
        return root
            .Descendants()
            .Where(element => element.Name.LocalName == "test-case")
            .Where(element => IsFailed((string?)element.Attribute("result")))
            .Take(20)
            .Select(element => new
            {
                name = (string?)element.Attribute("fullname") ??
                       (string?)element.Attribute("name") ??
                       string.Empty,
                message = element
                    .Elements()
                    .FirstOrDefault(child => child.Name.LocalName == "failure")
                    ?.Elements()
                    .FirstOrDefault(child => child.Name.LocalName == "message")
                    ?.Value ?? string.Empty,
                stackTrace = element
                    .Elements()
                    .FirstOrDefault(child => child.Name.LocalName == "failure")
                    ?.Elements()
                    .FirstOrDefault(child => child.Name.LocalName == "stack-trace")
                    ?.Value ?? string.Empty
            })
            .Cast<object>()
            .ToArray();
    }

    private static int CountCases(XElement root, string? result = null)
    {
        IEnumerable<XElement> cases = root
            .Descendants()
            .Where(element => element.Name.LocalName == "test-case");
        if (result is null)
        {
            return cases.Count();
        }

        return cases.Count(element =>
            string.Equals((string?)element.Attribute("result"), result, StringComparison.Ordinal));
    }

    private static int CountSkipped(XElement root)
    {
        return root
            .Descendants()
            .Where(element => element.Name.LocalName == "test-case")
            .Count(element =>
            {
                string? result = (string?)element.Attribute("result");
                return string.Equals(result, "Skipped", StringComparison.Ordinal) ||
                       string.Equals(result, "Ignored", StringComparison.Ordinal) ||
                       string.Equals(result, "Inconclusive", StringComparison.Ordinal);
            });
    }

    private static bool IsFailed(string? result)
    {
        return string.Equals(result, "Failed", StringComparison.Ordinal) ||
               string.Equals(result, "Error", StringComparison.Ordinal);
    }

    private static int ReadInt(XElement element, string attribute, int fallback)
    {
        return int.TryParse((string?)element.Attribute(attribute), out int value)
            ? value
            : fallback;
    }

    private static double ReadDouble(XElement element, string attribute, double fallback)
    {
        string? raw = (string?)element.Attribute(attribute);
        return double.TryParse(
            raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : double.TryParse(
                raw?.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
                ? value
                : fallback;
    }
}
