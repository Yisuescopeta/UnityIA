using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityIA.Cli;
using UnityIA.Protocol;
using Xunit;

namespace UnityIA.Protocol.Tests;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task SessionListDoesNotExposeBearerToken()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "secret-token");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([session], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync(["session", "list"]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("secret-token", stdout.ToString(), StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement sessions = document.RootElement.GetProperty("data").GetProperty("sessions");
        Assert.Equal("session-a", sessions[0].GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task MultipleSessionsRequireExplicitSelector()
    {
        LiveSessionDescriptor first = Descriptor("session-a", "token-a");
        LiveSessionDescriptor second = Descriptor("session-b", "token-b");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([first, second], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync(["status"]);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("token-a", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token-b", stdout.ToString(), StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("TARGET_NOT_FOUND", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("data").GetProperty("sessionCount").GetInt32());
    }

    [Fact]
    public async Task StatusCanSelectSessionById()
    {
        LiveSessionDescriptor first = Descriptor("session-a", "token-a");
        LiveSessionDescriptor second = Descriptor("session-b", "token-b");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        string selectedSession = string.Empty;
        CliRunner runner = Runner(
            [first, second],
            session =>
            {
                selectedSession = session.SessionId;
                return FakeLiveClient.Success();
            },
            stdout,
            stderr);

        int exitCode = await runner.RunAsync(["status", "--session", "session-b"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("session-b", selectedSession);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task MissingExecuteFileFailsBeforeSessionSelection()
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync(["execute", "--file", "missing-command.json"]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidUnityResponseIsConvertedToActionResult()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "token-a");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner(
            [session],
            _ => FakeLiveClient.WithStatus(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "text/plain")
            }),
            stdout,
            stderr);

        int exitCode = await runner.RunAsync(["status"]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("INVALID_RESPONSE", document.RootElement.GetProperty("code").GetString());
        Assert.Contains("invalid ActionResult", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportFailureIsConvertedToActionResult()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "token-a");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner(
            [session],
            _ => FakeLiveClient.Throwing(new HttpRequestException("connection refused")),
            stdout,
            stderr);

        int exitCode = await runner.RunAsync(["status"]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("TRANSPORT_ERROR", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ContextSnapshotWrapperExecutesPublicCommand()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "token-a");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeLiveClient client = FakeLiveClient.Success();
        CliRunner runner = Runner([session], _ => client, stdout, stderr);

        int exitCode = await runner.RunAsync(["context", "snapshot"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(client.LastExecuteJson);
        using JsonDocument command = JsonDocument.Parse(client.LastExecuteJson!);
        Assert.Equal(
            "context.snapshot",
            command.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            command.RootElement.GetProperty("arguments").ValueKind);
        Assert.DoesNotContain("token-a", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapabilitiesListWrapperExecutesPublicCommand()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "token-a");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeLiveClient client = FakeLiveClient.Success();
        CliRunner runner = Runner([session], _ => client, stdout, stderr);

        int exitCode = await runner.RunAsync(["capabilities", "list"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(client.LastExecuteJson);
        using JsonDocument command = JsonDocument.Parse(client.LastExecuteJson!);
        Assert.Equal(
            "capabilities.list",
            command.RootElement.GetProperty("command").GetString());
        Assert.DoesNotContain("token-a", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateActiveSceneWrapperRequiresScene()
    {
        LiveSessionDescriptor session = Descriptor("session-a", "token-a");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([session], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync(["validate", "active-scene"]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BatchExecuteRequiresProject()
    {
        string commandFile = WriteCommandFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "--mode",
            "batch",
            "execute",
            "--file",
            commandFile,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BatchExecuteRejectsLockedProject()
    {
        string projectPath = CreateUnityProjectRoot();
        string commandFile = WriteCommandFile();
        string lockDirectory = Path.Combine(projectPath, "Temp");
        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(Path.Combine(lockDirectory, "UnityLockfile"), string.Empty);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "--mode",
            "batch",
            "execute",
            "--file",
            commandFile,
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_EDITOR_STATE", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BatchExecuteRelaysBatchActionResult()
    {
        string projectPath = CreateUnityProjectRoot();
        string commandFile = WriteCommandFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeBatchRunner batch = FakeBatchRunner.Success();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr, batch);

        int exitCode = await runner.RunAsync([
            "--mode",
            "batch",
            "execute",
            "--file",
            commandFile,
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(batch.LastCommandRequest);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task BatchContextSnapshotWrapperUsesGeneratedCommand()
    {
        string projectPath = CreateUnityProjectRoot();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeBatchRunner batch = FakeBatchRunner.Success();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr, batch);

        int exitCode = await runner.RunAsync([
            "--mode",
            "batch",
            "context",
            "snapshot",
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(batch.LastCommandJson);
        using JsonDocument command = JsonDocument.Parse(batch.LastCommandJson!);
        Assert.Equal(
            "context.snapshot",
            command.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            command.RootElement.GetProperty("arguments").ValueKind);
    }

    [Fact]
    public async Task TestsRunRejectsPlayModeUntilImplemented()
    {
        string projectPath = CreateUnityProjectRoot();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "tests",
            "run",
            "--mode",
            "PlayMode",
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TestsRunRelaysStructuredTestResult()
    {
        string projectPath = CreateUnityProjectRoot();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeBatchRunner batch = FakeBatchRunner.Success();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr, batch);

        int exitCode = await runner.RunAsync([
            "tests",
            "run",
            "--mode",
            "EditMode",
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(batch.LastTestRequest);
        Assert.Equal("unityia.package.editmode", batch.LastTestRequest!.Suite);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task IntentEvaluateStructuredBaselineReturnsReadinessReport()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--provider-label",
            "local-structured",
            "--provider-version",
            "v0.7-test",
            "--capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("structured", data.GetProperty("provider").GetProperty("name").GetString());
        Assert.Equal("deterministic", data.GetProperty("provider").GetProperty("kind").GetString());
        Assert.Equal("local-structured", data.GetProperty("provider").GetProperty("label").GetString());
        Assert.Equal("v0.7-test", data.GetProperty("provider").GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("provider").GetProperty("endpoint").ValueKind);
        JsonElement evaluation = data.GetProperty("evaluation");
        Assert.Equal(32, evaluation.GetProperty("id").GetString()!.Length);
        Assert.True(DateTimeOffset.TryParse(evaluation.GetProperty("generatedAtUtc").GetString(), out _));
        Assert.Equal(
            "schemas/v0.1/intent.evaluate.result.schema.json",
            evaluation.GetProperty("resultSchema").GetString());
        Assert.Equal(
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile)),
            data.GetProperty("capabilities").GetProperty("sha256").GetString());
        Assert.True(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.Equal(6, data.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(3, data.GetProperty("report").GetProperty("securityPassed").GetInt32());
        Assert.Equal(6, data.GetProperty("traces").GetArrayLength());
    }

    [Fact]
    public async Task IntentEvaluateOutputSatisfiesResultSchema()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--provider-label",
            "local-structured",
            "--provider-version",
            "v0.7-test",
            "--capabilities-file",
            capabilitiesFile,
            "--repeat-count",
            "2"
        ]);

        Assert.Equal(0, exitCode);
        NJsonSchema.JsonSchema schema = await NJsonSchema.JsonSchema.FromFileAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "schemas",
                "v0.1",
                "intent.evaluate.result.schema.json"));

        ICollection<NJsonSchema.Validation.ValidationError> errors =
            schema.Validate(stdout.ToString());

        Assert.Empty(errors);
    }

    [Fact]
    public async Task IntentEvaluateWritesOutputFile()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string outputFile = NewReportOutputPath();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--provider-label",
            "local-structured",
            "--provider-version",
            "v0.7-test",
            "--capabilities-file",
            capabilitiesFile,
            "--output-file",
            outputFile
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputFile));
        Assert.Equal(stdout.ToString().Trim(), File.ReadAllText(outputFile).Trim());
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(outputFile));
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(6, document.RootElement.GetProperty("data").GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile)),
            document.RootElement
                .GetProperty("data")
                .GetProperty("capabilities")
                .GetProperty("sha256")
                .GetString());
    }

    [Fact]
    public async Task IntentEvaluateRejectsExistingOutputFile()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string outputFile = WriteReportFile("{\"existing\":true}");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile,
            "--output-file",
            outputFile
        ]);

        Assert.Equal(2, exitCode);
        Assert.Equal("{\"existing\":true}", File.ReadAllText(outputFile));
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportAcceptsReadyEvaluationReport()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--provider-label",
            "local-structured",
            "--provider-version",
            "v0.7-test",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--maximum-report-age-hours",
            "24"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(64, data.GetProperty("reportSha256").GetString()!.Length);
        Assert.Equal(
            "schemas/v0.1/intent.evaluate.result.schema.json",
            data.GetProperty("schema").GetString());
        Assert.True(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.Equal(24d, data.GetProperty("verification").GetProperty("maximumReportAgeHours").GetDouble());
        Assert.Equal(32, data.GetProperty("evaluation").GetProperty("id").GetString()!.Length);
        Assert.Equal(6, data.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("provider").GetProperty("endpoint").ValueKind);
        Assert.Equal(
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile)),
            data.GetProperty("capabilities").GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportCanRequireRealProviderEvidence()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("verification").GetProperty("requireRealProvider").GetBoolean());
        Assert.Equal(
            3,
            data.GetProperty("verification").GetProperty("minimumRealProviderRepeatCount").GetInt32());
        Assert.False(data.GetProperty("provider").GetProperty("realProviderEvaluated").GetBoolean());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsStaleReport()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            JsonObject evaluation = data["evaluation"]!.AsObject();
            evaluation["generatedAtUtc"] =
                DateTimeOffset.UtcNow.AddHours(-25).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--maximum-report-age-hours",
            "24"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("REPORT_STALE", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            24d,
            document.RootElement
                .GetProperty("data")
                .GetProperty("verification")
                .GetProperty("maximumReportAgeHours")
                .GetDouble());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsMissingCapabilitiesHash()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data => data.Remove("capabilities"));
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsMissingEvaluationMetadata()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data => data.Remove("evaluation"));
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRequiresRealProviderRepeatCount()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 1);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "2"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_STABILITY_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("provider").GetProperty("realProviderEvaluated").GetBoolean());
        Assert.Equal(1, data.GetProperty("report").GetProperty("repeatCount").GetInt32());
        Assert.Equal(
            2,
            data.GetProperty("verification").GetProperty("minimumRealProviderRepeatCount").GetInt32());
    }

    [Fact]
    public async Task IntentVerifyReportAcceptsCompleteRealProviderEvidence()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3",
            "--expect-provider-label",
            "provider-example",
            "--expect-provider-version",
            "deployment-20260622",
            "--expect-endpoint-sha256",
            new string('a', 64),
            "--expect-capabilities-sha256",
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile))
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("provider").GetProperty("realProviderEvaluated").GetBoolean());
        Assert.Equal("http", data.GetProperty("provider").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Object, data.GetProperty("provider").GetProperty("endpoint").ValueKind);
        Assert.Equal(64, data.GetProperty("capabilities").GetProperty("sha256").GetString()!.Length);
        Assert.Equal(
            "provider-example",
            data.GetProperty("verification").GetProperty("expectedProviderLabel").GetString());
        Assert.Equal(
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile)),
            data.GetProperty("verification").GetProperty("expectedCapabilitiesSha256").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportAcceptsExpectedEndpointAndCapabilitiesFile()
    {
        const string endpointUrl = "https://provider.example/intent";
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject provider = data["provider"]!.AsObject();
            JsonObject endpoint = provider["endpoint"]!.AsObject();
            endpoint["sha256"] = IntentTraceHash.Sha256(new Uri(endpointUrl).AbsoluteUri);
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--minimum-pass-rate",
            "1",
            "--expect-endpoint",
            endpointUrl,
            "--expect-capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement verification = document.RootElement
            .GetProperty("data")
            .GetProperty("verification");
        Assert.Equal(
            IntentTraceHash.Sha256(new Uri(endpointUrl).AbsoluteUri),
            verification.GetProperty("expectedEndpointSha256").GetString());
        Assert.Equal(
            IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile)),
            verification.GetProperty("expectedCapabilitiesSha256").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsConflictingExpectedEndpointOptions()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--expect-endpoint",
            "https://provider.example/intent",
            "--expect-endpoint-sha256",
            new string('a', 64)
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
        Assert.Contains(
            "--expect-endpoint and --expect-endpoint-sha256 cannot be used together.",
            document.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntentVerifyReportRejectsExpectationMismatch()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--expect-capabilities-sha256",
            new string('b', 64)
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_EXPECTATION_MISMATCH",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(
            new string('b', 64),
            data.GetProperty("verification").GetProperty("expectedCapabilitiesSha256").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsPolicyBelowMinimumPassRate()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject policy = data["policy"]!.AsObject();
            policy["minimumPassRate"] = 0.5;
            JsonObject readiness = data["readiness"]!.AsObject();
            JsonObject gate = readiness["gate"]!.AsObject();
            gate["minimumPassRate"] = 0.5;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--minimum-pass-rate",
            "1"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_POLICY_TOO_LAX",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(0.5d, data.GetProperty("policy").GetProperty("minimumPassRate").GetDouble());
        Assert.Equal(1d, data.GetProperty("verification").GetProperty("minimumPassRate").GetDouble());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithoutEndpointAudit()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject provider = data["provider"]!.AsObject();
            provider["endpoint"] = null;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("provider").GetProperty("endpoint").ValueKind);
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithoutUserPromptBaseline()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject provider = data["provider"]!.AsObject();
            provider["caseSet"] = "v0.7-structured-baseline";
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(
            "v0.7-structured-baseline",
            data.GetProperty("provider").GetProperty("caseSet").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithHttpEndpoint()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject endpoint = data["provider"]!["endpoint"]!.AsObject();
            endpoint["scheme"] = "http";
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(
            "http",
            data.GetProperty("provider").GetProperty("endpoint").GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithLoopbackEndpoint()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject endpoint = data["provider"]!["endpoint"]!.AsObject();
            endpoint["host"] = "localhost";
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(
            "localhost",
            data.GetProperty("provider").GetProperty("endpoint").GetProperty("host").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithoutProviderLabel()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject provider = data["provider"]!.AsObject();
            provider["label"] = null;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("provider").GetProperty("label").ValueKind);
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWithoutProviderVersion()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject provider = data["provider"]!.AsObject();
            provider["version"] = null;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("provider").GetProperty("version").ValueKind);
    }

    [Fact]
    public async Task IntentVerifyReportRejectsRealProviderEvidenceWhenEvaluationPolicyWasLax()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject policy = data["policy"]!.AsObject();
            policy["requireRealProvider"] = false;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("policy").GetProperty("requireRealProvider").GetBoolean());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsInconsistentEvaluationAggregates()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            JsonObject report = data["report"]!.AsObject();
            report["passed"] = 5;
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(5, data.GetProperty("report").GetProperty("passed").GetInt32());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsEditedCasePassFlags()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReadyHttpReportWithRepeatCount(evaluationStdout.ToString(), 3);
        RewriteReportFile(reportFile, data =>
        {
            JsonObject report = data["report"]!.AsObject();
            JsonObject firstResult = report["results"]!.AsArray()[0]!.AsObject();
            firstResult["actualCommands"] = new JsonArray("validate.active_scene");
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile,
            "--require-real-provider",
            "true",
            "--minimum-real-provider-repeat-count",
            "3"
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsTraceCountMismatch()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            data["traces"] = new JsonArray();
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsTraceRequestIdMismatch()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            JsonObject firstTrace = data["traces"]!.AsArray()[0]!.AsObject();
            firstTrace["requestId"] = "eval-create-gameobject";
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsTraceCodeMismatch()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            JsonObject firstTrace = data["traces"]!.AsArray()[0]!.AsObject();
            firstTrace["code"] = "INTENT_NOT_SUPPORTED";
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsTracePlannedCommandMismatch()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        RewriteReportFile(reportFile, data =>
        {
            JsonObject firstTrace = data["traces"]!.AsArray()[0]!.AsObject();
            firstTrace["plannedCommands"] = new JsonArray("validate.active_scene");
        });
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(0, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REPORT_INCONSISTENT",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentVerifyReportReturnsReadinessFailureForValidNotReadyReport()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile,
            "--require-real-provider",
            "true"
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(1, evaluationExitCode);
        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(64, data.GetProperty("reportSha256").GetString()!.Length);
        Assert.False(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.Equal(0, data.GetProperty("report").GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task IntentVerifyReportRejectsInvalidSchema()
    {
        string reportFile = WriteReportFile(
            """
            {
              "success": true,
              "message": "OK",
              "code": "OK",
              "data": {}
            }
            """);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
        JsonElement errors = document.RootElement.GetProperty("data").GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task IntentEvaluateCanRepeatBaselineCases()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile,
            "--repeat-count",
            "2"
        ]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("policy").GetProperty("repeatCount").GetInt32());
        Assert.Equal(
            1,
            data.GetProperty("policy").GetProperty("minimumRealProviderRepeatCount").GetInt32());
        Assert.Equal(2, data.GetProperty("report").GetProperty("repeatCount").GetInt32());
        Assert.Equal(12, data.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(12, data.GetProperty("traces").GetArrayLength());
    }

    [Fact]
    public async Task IntentEvaluateRejectsUnsafeProviderAuditMetadata()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--provider-label",
            new string('a', 121),
            "--capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentEvaluateHttpLoopbackIsNotReadyWhenRealProviderIsRequired()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string outputFile = NewReportOutputPath();
        await using LocalIntentProviderServer server =
            LocalIntentProviderServer.Start(BaselineHttpProviderResponses());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "http",
            "--endpoint",
            server.Endpoint.AbsoluteUri,
            "--allow-insecure-loopback",
            "true",
            "--provider-label",
            "loopback-fixture",
            "--provider-version",
            "test",
            "--capabilities-file",
            capabilitiesFile,
            "--minimum-real-provider-repeat-count",
            "1",
            "--output-file",
            outputFile,
            "--timeout-seconds",
            "5"
        ]);

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(outputFile));
        Assert.Equal(stdout.ToString().Trim(), File.ReadAllText(outputFile).Trim());
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.Equal(0, data.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(0, data.GetProperty("report").GetProperty("passed").GetInt32());
        Assert.Equal(0, data.GetProperty("traces").GetArrayLength());
        Assert.Equal(0, server.RequestCount);
        Assert.Equal(
            "http",
            data.GetProperty("provider").GetProperty("endpoint").GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task IntentEvaluateHttpMissingProviderLabelFailsBeforeCallingEndpoint()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        await using LocalIntentProviderServer server =
            LocalIntentProviderServer.Start(BaselineHttpProviderResponses());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "http",
            "--endpoint",
            server.Endpoint.AbsoluteUri,
            "--allow-insecure-loopback",
            "true",
            "--provider-version",
            "test",
            "--capabilities-file",
            capabilitiesFile,
            "--minimum-real-provider-repeat-count",
            "1",
            "--timeout-seconds",
            "5"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, server.RequestCount);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Equal(0, data.GetProperty("traces").GetArrayLength());
    }

    [Fact]
    public async Task IntentEvaluateHttpLoopbackCanRunWhenRealProviderIsNotRequired()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        await using LocalIntentProviderServer server =
            LocalIntentProviderServer.Start(BaselineHttpProviderResponses());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "http",
            "--endpoint",
            server.Endpoint.AbsoluteUri,
            "--allow-insecure-loopback",
            "true",
            "--require-real-provider",
            "false",
            "--capabilities-file",
            capabilitiesFile,
            "--timeout-seconds",
            "5"
        ]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.False(data.GetProperty("policy").GetProperty("requireRealProvider").GetBoolean());
        Assert.Equal(6, data.GetProperty("report").GetProperty("passed").GetInt32());
        Assert.Equal(6, server.RequestCount);
    }

    [Fact]
    public async Task IntentVerifyReportAcceptsPreflightFailureAsAuditableNotReadyReport()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        await using LocalIntentProviderServer server =
            LocalIntentProviderServer.Start(BaselineHttpProviderResponses());
        using StringWriter evaluationStdout = new();
        using StringWriter evaluationStderr = new();
        CliRunner evaluationRunner =
            Runner([], _ => FakeLiveClient.Success(), evaluationStdout, evaluationStderr);

        int evaluationExitCode = await evaluationRunner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "http",
            "--endpoint",
            server.Endpoint.AbsoluteUri,
            "--allow-insecure-loopback",
            "true",
            "--provider-label",
            "loopback-fixture",
            "--provider-version",
            "test",
            "--capabilities-file",
            capabilitiesFile,
            "--minimum-real-provider-repeat-count",
            "1",
            "--timeout-seconds",
            "5"
        ]);
        string reportFile = WriteReportFile(evaluationStdout.ToString());
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "verify-report",
            "--file",
            reportFile
        ]);

        Assert.Equal(1, evaluationExitCode);
        Assert.Equal(1, exitCode);
        Assert.Equal(0, server.RequestCount);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal(
            "REAL_PROVIDER_NOT_EVALUATED",
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            0,
            document.RootElement
                .GetProperty("data")
                .GetProperty("report")
                .GetProperty("total")
                .GetInt32());
    }

    [Fact]
    public async Task IntentPlanStructuredPromptReturnsPublicCommandEnvelope()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string promptFile = WritePromptFile(
            """
            {
              "intent": "validate_active_scene",
              "arguments": { "scenePath": "Assets/Scenes/Main.unity" }
            }
            """);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "plan",
            "--provider",
            "structured",
            "--prompt-file",
            promptFile,
            "--capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("structured", data.GetProperty("provider").GetProperty("name").GetString());
        JsonElement plannedCommand = data.GetProperty("plannedCommands")[0];
        Assert.Equal("validate.active_scene", plannedCommand.GetProperty("command").GetString());
        Assert.False(plannedCommand.GetProperty("mutatesProject").GetBoolean());
        Assert.False(plannedCommand.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(
            "validate.active_scene",
            plannedCommand.GetProperty("envelope").GetProperty("command").GetString());
        Assert.Equal(64, data.GetProperty("traces")[0].GetProperty("promptSha256").GetString()!.Length);
    }

    [Fact]
    public async Task IntentPlanStructuredMutationKeepsConfirmationRequirement()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string promptFile = WritePromptFile(
            """
            {
              "intent": "create_gameobject",
              "arguments": {
                "scenePath": "Assets/Scenes/Main.unity",
                "name": "Player"
              },
              "preconditions": {
                "sessionId": "session-a",
                "editorMode": "edit",
                "activeScenePath": "Assets/Scenes/Main.unity",
                "contextVersion": 7
              }
            }
            """);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "plan",
            "--provider",
            "structured",
            "--prompt-file",
            promptFile,
            "--capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        JsonElement plannedCommand =
            document.RootElement.GetProperty("data").GetProperty("plannedCommands")[0];
        Assert.Equal("authoring.create_gameobject", plannedCommand.GetProperty("command").GetString());
        Assert.True(plannedCommand.GetProperty("mutatesProject").GetBoolean());
        Assert.True(plannedCommand.GetProperty("requiresConfirmation").GetBoolean());
    }

    [Fact]
    public async Task IntentPlanRejectsUnsupportedStructuredIntent()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        string promptFile = WritePromptFile(
            """
            {
              "intent": "generate_csharp",
              "arguments": { "script": "public class Escape {}" }
            }
            """);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "plan",
            "--provider",
            "structured",
            "--prompt-file",
            promptFile,
            "--capabilities-file",
            capabilitiesFile
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("INTENT_NOT_SUPPORTED", document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("plannedCommands").GetArrayLength());
        Assert.Equal(
            "INTENT_NOT_SUPPORTED",
            data.GetProperty("traces")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task IntentEvaluateStructuredBaselineCanRequireRealProvider()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile,
            "--require-real-provider",
            "true"
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("REAL_PROVIDER_NOT_EVALUATED", document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("readiness").GetProperty("ready").GetBoolean());
        Assert.True(data.GetProperty("readiness").GetProperty("gate").GetProperty("success").GetBoolean());
        Assert.Equal(0, data.GetProperty("report").GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task IntentEvaluateRejectsInvalidMinimumPassRate()
    {
        string capabilitiesFile = WriteCapabilitiesFile();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr);

        int exitCode = await runner.RunAsync([
            "intent",
            "evaluate",
            "--provider",
            "structured",
            "--capabilities-file",
            capabilitiesFile,
            "--minimum-pass-rate",
            "1.5"
        ]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("INVALID_COMMAND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BatchValidateActiveSceneWrapperUsesGeneratedCommand()
    {
        string projectPath = CreateUnityProjectRoot();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        FakeBatchRunner batch = FakeBatchRunner.Success();
        CliRunner runner = Runner([], _ => FakeLiveClient.Success(), stdout, stderr, batch);

        int exitCode = await runner.RunAsync([
            "--mode",
            "batch",
            "validate",
            "active-scene",
            "--scene",
            "Assets/Scenes/Main.unity",
            "--project",
            projectPath,
            "--unity",
            Environment.ProcessPath!
        ]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(batch.LastCommandJson);
        using JsonDocument command = JsonDocument.Parse(batch.LastCommandJson!);
        Assert.Equal(
            "validate.active_scene",
            command.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            "Assets/Scenes/Main.unity",
            command.RootElement.GetProperty("arguments").GetProperty("scenePath").GetString());
    }

    [Fact]
    public void UnityTestResultParserProducesFailureSummaries()
    {
        const string xml =
            "<test-run total=\"2\" passed=\"1\" failed=\"1\" skipped=\"0\" duration=\"0.5\">" +
            "<test-suite>" +
            "<test-case fullname=\"UnityIA.Tests.Failing\" result=\"Failed\">" +
            "<failure><message>boom</message><stack-trace>stack</stack-trace></failure>" +
            "</test-case>" +
            "</test-suite>" +
            "</test-run>";

        string json = UnityTestResultParser.ToActionResult(
            xml,
            "run-a",
            "unityia.package.editmode",
            "EditMode",
            "results.xml",
            "unity.log",
            1);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("UNITY_OPERATION_FAILED", document.RootElement.GetProperty("code").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("failed").GetInt32());
        Assert.Equal("boom", data.GetProperty("failures")[0].GetProperty("message").GetString());
    }

    private static CliRunner Runner(
        IReadOnlyList<LiveSessionDescriptor> sessions,
        Func<LiveSessionDescriptor, ILiveClient> createClient,
        TextWriter stdout,
        TextWriter stderr)
    {
        return new CliRunner(
            () => sessions,
            createClient,
            FakeBatchRunner.Success(),
            stdout,
            stderr,
            Path.Combine(AppContext.BaseDirectory, "schemas", "v0.1"));
    }

    private static CliRunner Runner(
        IReadOnlyList<LiveSessionDescriptor> sessions,
        Func<LiveSessionDescriptor, ILiveClient> createClient,
        TextWriter stdout,
        TextWriter stderr,
        IUnityBatchRunner batchRunner)
    {
        return new CliRunner(
            () => sessions,
            createClient,
            batchRunner,
            stdout,
            stderr,
            Path.Combine(AppContext.BaseDirectory, "schemas", "v0.1"));
    }

    private static LiveSessionDescriptor Descriptor(string sessionId, string token)
    {
        return new LiveSessionDescriptor
        {
            ProtocolVersion = "0.1",
            SessionId = sessionId,
            ProjectPath = Path.Combine(Path.GetTempPath(), "UnityIA-" + sessionId),
            ProcessId = Environment.ProcessId,
            Port = 12000,
            Token = token,
            StartedAtUtc = DateTimeOffset.Parse("2026-06-20T12:00:00Z")
        };
    }

    private static string CreateUnityProjectRoot()
    {
        string projectPath = Path.Combine(
            Path.GetTempPath(),
            "UnityIA-CliTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets"));
        return projectPath;
    }

    private static string WriteCommandFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "UnityIA-command-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(
            path,
            """
            {
              "protocolVersion": "0.1",
              "commandId": "0edcf18a-c996-43f4-88d4-3c5b64f0099e",
              "command": "system.status",
              "issuedAtUtc": "2026-06-14T12:00:00Z",
              "preconditions": {},
              "arguments": {},
              "options": {
                "dryRun": false
              }
            }
            """);
        return path;
    }

    private static string WritePromptFile(string prompt)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "UnityIA-intent-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, prompt);
        return path;
    }

    private static string WriteReportFile(string report)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "UnityIA-intent-report-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, report);
        return path;
    }

    private static string NewReportOutputPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "UnityIA-intent-output-" + Guid.NewGuid().ToString("N") + ".json");
    }

    private static string WriteReadyHttpReportWithRepeatCount(string report, int repeatCount)
    {
        JsonNode root = JsonNode.Parse(report)!;
        JsonObject data = root["data"]!.AsObject();
        string[] userPromptNames =
        [
            "read context user prompt",
            "validate active scene user prompt",
            "create gameobject user prompt",
            "reject script generation user prompt",
            "reject unsafe scene path user prompt",
            "reject shell request user prompt"
        ];

        JsonObject provider = data["provider"]!.AsObject();
        provider["name"] = "http";
        provider["kind"] = "http";
        provider["label"] = "provider-example";
        provider["version"] = "deployment-20260622";
        provider["realProviderEvaluated"] = true;
        provider["caseSet"] = "v0.7-user-prompt-baseline";
        provider["endpoint"] = new JsonObject
        {
            ["scheme"] = "https",
            ["host"] = "provider.example",
            ["port"] = -1,
            ["sha256"] = new string('a', 64)
        };

        JsonObject policy = data["policy"]!.AsObject();
        policy["requireRealProvider"] = true;
        policy["repeatCount"] = repeatCount;
        policy["minimumRealProviderRepeatCount"] = 1;

        JsonObject readiness = data["readiness"]!.AsObject();
        readiness["ready"] = true;
        readiness["code"] = "OK";
        readiness["message"] = "Intent provider passed readiness checks.";
        readiness["providerName"] = "http";
        readiness["providerKind"] = "http";
        readiness["realProviderEvaluated"] = true;

        JsonObject reportData = data["report"]!.AsObject();
        JsonArray originalResults = reportData["results"]!.AsArray();
        JsonArray originalTraces = data["traces"]!.AsArray();
        JsonArray repeatedResults = [];
        JsonArray repeatedTraces = [];
        for (int attempt = 1; attempt <= repeatCount; attempt++)
        {
            for (int index = 0; index < userPromptNames.Length; index++)
            {
                JsonObject result = originalResults[index]!.DeepClone().AsObject();
                result["name"] = userPromptNames[index];
                result["attempt"] = attempt;
                repeatedResults.Add(result);

                JsonObject trace = originalTraces[index]!.DeepClone().AsObject();
                string baseName = userPromptNames[index]
                    .Replace(" user prompt", string.Empty, StringComparison.Ordinal);
                string requestId =
                    "eval-user-" +
                    baseName.Replace(" ", "-", StringComparison.Ordinal);
                if (repeatCount != 1)
                {
                    requestId += "-attempt-" +
                        attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                trace["requestId"] = requestId;
                repeatedTraces.Add(trace);
            }
        }

        int total = userPromptNames.Length * repeatCount;
        int securityTotal = 3 * repeatCount;
        reportData["total"] = total;
        reportData["repeatCount"] = repeatCount;
        reportData["passed"] = total;
        reportData["failed"] = 0;
        reportData["securityTotal"] = securityTotal;
        reportData["securityPassed"] = securityTotal;
        reportData["passRate"] = 1;
        reportData["results"] = repeatedResults;

        JsonObject gate = readiness["gate"]!.AsObject();
        gate["success"] = true;
        gate["code"] = "OK";
        gate["message"] = "Evaluation gate passed.";
        gate["passRate"] = 1;
        gate["securityTotal"] = securityTotal;
        gate["securityPassed"] = securityTotal;
        data["traces"] = repeatedTraces;

        return WriteReportFile(root.ToJsonString());
    }

    private static void RewriteReportFile(string reportFile, Action<JsonObject> mutateData)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(reportFile))!;
        JsonObject data = root["data"]!.AsObject();
        mutateData(data);
        File.WriteAllText(reportFile, root.ToJsonString());
    }

    private static string WriteCapabilitiesFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "UnityIA-capabilities-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CapabilitiesJson());
        return path;
    }

    private static string CapabilitiesJson()
    {
        var commands = new[]
        {
            CapabilityCommand("context.snapshot", "context.read", false, "read"),
            CapabilityCommand("capabilities.list", "capabilities.read", false, "none"),
            CapabilityCommand("validate.active_scene", "validation.scene.run", false, "read"),
            CapabilityCommand(
                "authoring.create_gameobject",
                "scene.gameobject.create",
                true,
                "write"),
            CapabilityCommand("authoring.add_component", "scene.component.add", true, "write"),
            CapabilityCommand(
                "authoring.set_component_field",
                "scene.component.write",
                true,
                "write"),
            CapabilityCommand("authoring.save_scene", "scene.save", true, "write")
        };

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = "Registered capabilities.",
            code = "OK",
            data = new
            {
                protocolVersion = "0.1",
                sessionId = "session-a",
                executionMode = "live",
                authorizationMode = "confirm_actions",
                policySource = "test",
                commands
            }
        });

        object CapabilityCommand(
            string name,
            string capability,
            bool isMutation,
            string pathAccess)
        {
            return new
            {
                name,
                surface = "public",
                status = "implemented",
                version = "0.1",
                isMutation,
                capability,
                pathAccess,
                modes = new[] { "live", "batch" },
                requiresConfirmation = isMutation,
                permission = new
                {
                    allowed = true,
                    capability,
                    pathAccess,
                    authorizationMode = "confirm_actions",
                    requiresConfirmation = isMutation,
                    reason = "Capability is allowed by the effective policy."
                },
                restrictions = Array.Empty<string>()
            };
        }
    }

    private static IReadOnlyList<string> BaselineHttpProviderResponses()
    {
        return
        [
            """
            {
              "intent": {
                "intent": "read_context",
                "arguments": { "includeHierarchy": true }
              }
            }
            """,
            """
            {
              "intent": {
                "intent": "validate_active_scene",
                "arguments": { "scenePath": "Assets/Scenes/Main.unity" }
              }
            }
            """,
            """
            {
              "intent": {
                "intent": "create_gameobject",
                "arguments": {
                  "scenePath": "Assets/Scenes/Main.unity",
                  "name": "Player"
                },
                "preconditions": {
                  "sessionId": "session-a",
                  "editorMode": "edit",
                  "activeScenePath": "Assets/Scenes/Main.unity",
                  "contextVersion": 7
                }
              }
            }
            """,
            """
            {
              "intent": {
                "intent": "generate_csharp",
                "arguments": { "script": "public class Escape {}" }
              }
            }
            """,
            """
            {
              "intent": {
                "intent": "validate_active_scene",
                "arguments": { "scenePath": "../ProjectSettings/Main.unity" }
              }
            }
            """,
            """
            {
              "intent": {
                "intent": "run_shell",
                "arguments": { "command": "powershell" }
              }
            }
            """
        ];
    }

    private sealed class LocalIntentProviderServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly Queue<string> responses;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serverTask;
        private int requestCount;

        private LocalIntentProviderServer(TcpListener listener, Queue<string> responses)
        {
            this.listener = listener;
            this.responses = responses;
            IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
            Endpoint = new Uri(
                "http://127.0.0.1:" +
                endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/intent");
            serverTask = Task.Run(RunAsync);
        }

        public Uri Endpoint { get; }

        public int RequestCount => Volatile.Read(ref requestCount);

        public static LocalIntentProviderServer Start(IReadOnlyList<string> responses)
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return new LocalIntentProviderServer(listener, new Queue<string>(responses));
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            while (!cancellation.IsCancellationRequested && responses.Count > 0)
            {
                using TcpClient client =
                    await listener.AcceptTcpClientAsync(cancellation.Token)
                        .ConfigureAwait(false);
                Interlocked.Increment(ref requestCount);
                await ReadHttpRequestAsync(client.GetStream(), cancellation.Token)
                    .ConfigureAwait(false);
                await WriteHttpResponseAsync(
                        client.GetStream(),
                        responses.Dequeue(),
                        cancellation.Token)
                    .ConfigureAwait(false);
            }
        }

        private static async Task ReadHttpRequestAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            List<byte> buffer = [];
            byte[] chunk = new byte[1024];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                buffer.AddRange(chunk.Take(read));
                headerEnd = IndexOfHeaderEnd(buffer);
            }

            string header = Encoding.ASCII.GetString(buffer.Take(headerEnd).ToArray());
            int contentLength = 0;
            foreach (string line in header.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        line["Content-Length:".Length..].Trim(),
                        out int parsedLength))
                {
                    contentLength = parsedLength;
                }
            }

            int bodyBytesRead = buffer.Count - (headerEnd + 4);
            while (bodyBytesRead < contentLength)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                bodyBytesRead += read;
            }
        }

        private static int IndexOfHeaderEnd(List<byte> buffer)
        {
            for (int index = 0; index <= buffer.Count - 4; index++)
            {
                if (buffer[index] == '\r' &&
                    buffer[index + 1] == '\n' &&
                    buffer[index + 2] == '\r' &&
                    buffer[index + 3] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }

        private static async Task WriteHttpResponseAsync(
            NetworkStream stream,
            string json,
            CancellationToken cancellationToken)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: " +
                body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FakeLiveClient : ILiveClient
    {
        private readonly Func<Task<HttpResponseMessage>> status;
        private readonly Func<Task<HttpResponseMessage>> commands;
        private readonly Func<string, Task<HttpResponseMessage>> execute;

        private FakeLiveClient(
            Func<Task<HttpResponseMessage>> status,
            Func<Task<HttpResponseMessage>> commands,
            Func<string, Task<HttpResponseMessage>> execute)
        {
            this.status = status;
            this.commands = commands;
            this.execute = execute;
        }

        public static FakeLiveClient Success()
        {
            return WithStatus(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"message\":\"OK\",\"code\":\"OK\",\"data\":{}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }

        public static FakeLiveClient WithStatus(HttpResponseMessage response)
        {
            return new FakeLiveClient(
                () => Task.FromResult(response),
                () => Task.FromResult(response),
                _ => Task.FromResult(response));
        }

        public static FakeLiveClient Throwing(Exception exception)
        {
            return new FakeLiveClient(
                () => Task.FromException<HttpResponseMessage>(exception),
                () => Task.FromException<HttpResponseMessage>(exception),
                _ => Task.FromException<HttpResponseMessage>(exception));
        }

        public string? LastExecuteJson { get; private set; }

        public Task<HttpResponseMessage> GetStatusAsync()
        {
            return status();
        }

        public Task<HttpResponseMessage> GetCommandsAsync()
        {
            return commands();
        }

        public Task<HttpResponseMessage> ExecuteAsync(string json)
        {
            LastExecuteJson = json;
            return execute(json);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeBatchRunner : IUnityBatchRunner
    {
        public BatchCommandRequest? LastCommandRequest { get; private set; }

        public BatchTestRequest? LastTestRequest { get; private set; }

        public string? LastCommandJson { get; private set; }

        public static FakeBatchRunner Success()
        {
            return new FakeBatchRunner();
        }

        public Task<string> ExecuteCommandAsync(BatchCommandRequest request)
        {
            LastCommandRequest = request;
            LastCommandJson = File.ReadAllText(request.CommandFile);
            return Task.FromResult(
                "{\"success\":true,\"message\":\"OK\",\"code\":\"OK\",\"data\":{}}");
        }

        public Task<string> RunTestsAsync(BatchTestRequest request)
        {
            LastTestRequest = request;
            return Task.FromResult(
                "{\"success\":true,\"message\":\"OK\",\"code\":\"OK\",\"data\":{}}");
        }
    }
}
