using System.Net;
using System.Globalization;
using System.Text.Json;
using UnityIA.Protocol;

namespace UnityIA.Cli;

internal sealed class CliRunner
{
    private const string IntentEvaluateResultSchema =
        "schemas/v0.1/intent.evaluate.result.schema.json";
    private const string Usage =
        "Usage: unityia [--mode live|batch] session list | status [--project PATH|--session ID] | commands [--project PATH|--session ID] | context snapshot [--project PATH|--session ID] | capabilities list [--project PATH|--session ID] | validate active-scene --scene PATH [--project PATH|--session ID] | intent plan --provider structured|http --prompt-file FILE --capabilities-file FILE [--endpoint URL] | intent evaluate --provider structured|http --capabilities-file FILE [--endpoint URL] [--provider-label NAME] [--provider-version VALUE] [--repeat-count N] [--output-file FILE] | intent verify-report --file FILE [--require-real-provider true|false] [--minimum-pass-rate N] [--minimum-real-provider-repeat-count N] [--maximum-report-age-hours N] [--expect-provider-label NAME] [--expect-provider-version VALUE] [--expect-endpoint URL|--expect-endpoint-sha256 HEX] [--expect-capabilities-file FILE|--expect-capabilities-sha256 HEX] | execute --file FILE [--project PATH|--session ID|--project PATH --unity UNITY] | tests run --mode EditMode --project PATH --unity UNITY";
    private static readonly TimeSpan DefaultBatchTimeout = TimeSpan.FromMinutes(10);

    private readonly Func<IReadOnlyList<LiveSessionDescriptor>> findSessions;
    private readonly Func<LiveSessionDescriptor, ILiveClient> createClient;
    private readonly IUnityBatchRunner batchRunner;
    private readonly TextWriter stdout;
    private readonly TextWriter stderr;
    private readonly string schemaDirectory;

    public CliRunner(
        Func<IReadOnlyList<LiveSessionDescriptor>> findSessions,
        Func<LiveSessionDescriptor, ILiveClient> createClient,
        IUnityBatchRunner batchRunner,
        TextWriter stdout,
        TextWriter stderr,
        string schemaDirectory)
    {
        this.findSessions = findSessions;
        this.createClient = createClient;
        this.batchRunner = batchRunner;
        this.stdout = stdout;
        this.stderr = stderr;
        this.schemaDirectory = schemaDirectory;
    }

    public static CliRunner CreateDefault(TextWriter stdout, TextWriter stderr)
    {
        return new CliRunner(
            SessionDiscovery.FindLiveSessions,
            session => new LiveClient(session),
            new UnityBatchRunner(),
            stdout,
            stderr,
            Path.Combine(AppContext.BaseDirectory, "schemas", "v0.1"));
    }

    public async Task<int> RunAsync(string[] args)
    {
        ModeParseResult mode = ParseExecutionMode(args);
        if (!mode.Success)
        {
            return InvalidCommand(mode.Error!);
        }

        int commandIndex = mode.NextIndex;
        if (args.Length - commandIndex >= 2 &&
            args[commandIndex] == "session" &&
            args[commandIndex + 1] == "list")
        {
            if (mode.ExecutionMode != "live")
            {
                return InvalidCommand("session list is available only in live mode.");
            }

            return ListSessions(args, commandIndex);
        }

        if (args.Length > commandIndex && args[commandIndex] == "tests")
        {
            return await RunTestsAsync(args, commandIndex).ConfigureAwait(false);
        }

        if (args.Length > commandIndex && args[commandIndex] == "context")
        {
            return await RunContextAsync(
                args,
                commandIndex,
                mode.ExecutionMode).ConfigureAwait(false);
        }

        if (args.Length > commandIndex && args[commandIndex] == "capabilities")
        {
            return await RunCapabilitiesAsync(
                args,
                commandIndex,
                mode.ExecutionMode).ConfigureAwait(false);
        }

        if (args.Length > commandIndex && args[commandIndex] == "validate")
        {
            return await RunValidateAsync(
                args,
                commandIndex,
                mode.ExecutionMode).ConfigureAwait(false);
        }

        if (args.Length > commandIndex && args[commandIndex] == "intent")
        {
            return await RunIntentAsync(args, commandIndex).ConfigureAwait(false);
        }

        if (args.Length == commandIndex ||
            args[commandIndex] is not ("status" or "commands" or "execute"))
        {
            return InvalidCommand("Invalid CLI command.");
        }

        string command = args[commandIndex];
        if (mode.ExecutionMode == "batch")
        {
            return command == "execute"
                ? await RunBatchExecuteAsync(args, commandIndex).ConfigureAwait(false)
                : InvalidCommand(command + " is available only in live mode.");
        }

        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 1,
            command == "execute"
                ? ["--file", "--project", "--session"]
                : ["--project", "--session"]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? commandJson = null;
        if (command == "execute")
        {
            string? file = parsed.Get("--file");
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                WriteError(
                    "INVALID_COMMAND",
                    "--file must reference an existing command JSON file.");
                return 2;
            }

            commandJson = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            JsonSchemaCommandValidator validator = new(schemaDirectory);
            SchemaValidationResult validation =
                await validator.ValidateAsync(commandJson).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                WriteError(
                    "VALIDATION_FAILED",
                    "Command JSON does not satisfy the v0.1 schema.",
                    new
                    {
                        command = validation.Command,
                        errors = validation.Errors,
                        warnings = Array.Empty<string>()
                    });
                return 1;
            }
        }

        IReadOnlyList<LiveSessionDescriptor> sessions = findSessions();
        LiveSessionDescriptor? session = SessionDiscovery.Select(
            sessions,
            parsed.Get("--project"),
            parsed.Get("--session"),
            out string? error);
        if (session is null)
        {
            WriteError(
                "TARGET_NOT_FOUND",
                error!,
                SelectionFailureData(
                    sessions,
                    parsed.Get("--project"),
                    parsed.Get("--session")));
            return 1;
        }

        try
        {
            using ILiveClient client = createClient(session);
            using HttpResponseMessage response = command switch
            {
                "status" => await client.GetStatusAsync().ConfigureAwait(false),
                "commands" => await client.GetCommandsAsync().ConfigureAwait(false),
                "execute" => await client.ExecuteAsync(commandJson!).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unreachable CLI command.")
            };

            return await RelayUnityResponseAsync(response).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            WriteTransportError(exception);
            return 1;
        }
        catch (TaskCanceledException exception)
        {
            WriteTransportError(exception);
            return 1;
        }
    }

    private async Task<int> RunContextAsync(
        string[] args,
        int commandIndex,
        string executionMode)
    {
        if (args.Length <= commandIndex + 1 || args[commandIndex + 1] != "snapshot")
        {
            return InvalidCommand("Expected context snapshot.");
        }

        string commandJson = CreateCommandJson("context.snapshot", new { });
        if (executionMode == "batch")
        {
            OptionParseResult parsed = ParseOptions(
                args,
                commandIndex + 2,
                ["--project", "--unity"]);
            if (!parsed.Success)
            {
                return InvalidCommand(parsed.Error!);
            }

            return await RunGeneratedBatchCommandAsync(commandJson, parsed)
                .ConfigureAwait(false);
        }

        OptionParseResult live = ParseOptions(
            args,
            commandIndex + 2,
            ["--project", "--session"]);
        if (!live.Success)
        {
            return InvalidCommand(live.Error!);
        }

        return await RunGeneratedLiveCommandAsync(commandJson, live).ConfigureAwait(false);
    }

    private async Task<int> RunBatchExecuteAsync(string[] args, int commandIndex)
    {
        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 1,
            ["--file", "--project", "--unity"]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? commandFile = parsed.Get("--file");
        if (string.IsNullOrWhiteSpace(commandFile) || !File.Exists(commandFile))
        {
            WriteError(
                "INVALID_COMMAND",
                "--file must reference an existing command JSON file.");
            return 2;
        }

        string commandJson = await File.ReadAllTextAsync(commandFile).ConfigureAwait(false);
        JsonSchemaCommandValidator validator = new(schemaDirectory);
        SchemaValidationResult validation = await validator.ValidateAsync(commandJson)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            WriteError(
                "VALIDATION_FAILED",
                "Command JSON does not satisfy the v0.1 schema.",
                new
                {
                    command = validation.Command,
                    errors = validation.Errors,
                    warnings = Array.Empty<string>()
                });
            return 1;
        }

        if (!TryResolveBatchTarget(
                parsed.Get("--project"),
                parsed.Get("--unity"),
                out string projectPath,
                out string unityPath,
                out int errorExitCode))
        {
            return errorExitCode;
        }

        string json = await batchRunner.ExecuteCommandAsync(
                new BatchCommandRequest(
                    unityPath,
                    projectPath,
                    Path.GetFullPath(commandFile),
                    DefaultBatchTimeout))
            .ConfigureAwait(false);
        return RelayBatchResult(json);
    }

    private async Task<int> RunCapabilitiesAsync(
        string[] args,
        int commandIndex,
        string executionMode)
    {
        if (args.Length <= commandIndex + 1 || args[commandIndex + 1] != "list")
        {
            return InvalidCommand("Expected capabilities list.");
        }

        string commandJson = CreateCommandJson("capabilities.list", new { });
        if (executionMode == "batch")
        {
            OptionParseResult parsed = ParseOptions(
                args,
                commandIndex + 2,
                ["--project", "--unity"]);
            if (!parsed.Success)
            {
                return InvalidCommand(parsed.Error!);
            }

            return await RunGeneratedBatchCommandAsync(commandJson, parsed)
                .ConfigureAwait(false);
        }

        OptionParseResult live = ParseOptions(
            args,
            commandIndex + 2,
            ["--project", "--session"]);
        if (!live.Success)
        {
            return InvalidCommand(live.Error!);
        }

        return await RunGeneratedLiveCommandAsync(commandJson, live).ConfigureAwait(false);
    }

    private async Task<int> RunValidateAsync(
        string[] args,
        int commandIndex,
        string executionMode)
    {
        if (args.Length <= commandIndex + 1 || args[commandIndex + 1] != "active-scene")
        {
            return InvalidCommand("Expected validate active-scene.");
        }

        IReadOnlyCollection<string> allowed = executionMode == "batch"
            ? ["--scene", "--project", "--unity"]
            : ["--scene", "--project", "--session"];
        OptionParseResult parsed = ParseOptions(args, commandIndex + 2, allowed);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? scenePath = parsed.Get("--scene");
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return InvalidCommand("--scene is required for validate active-scene.");
        }

        string commandJson = CreateCommandJson(
            "validate.active_scene",
            new { scenePath });
        return executionMode == "batch"
            ? await RunGeneratedBatchCommandAsync(commandJson, parsed).ConfigureAwait(false)
            : await RunGeneratedLiveCommandAsync(commandJson, parsed).ConfigureAwait(false);
    }

    private async Task<int> RunIntentAsync(string[] args, int commandIndex)
    {
        if (args.Length <= commandIndex + 1)
        {
            return InvalidCommand("Expected intent plan, intent evaluate, or intent verify-report.");
        }

        return args[commandIndex + 1] switch
        {
            "plan" => await RunIntentPlanAsync(args, commandIndex).ConfigureAwait(false),
            "evaluate" => await RunIntentEvaluateAsync(args, commandIndex).ConfigureAwait(false),
            "verify-report" => await RunIntentVerifyReportAsync(args, commandIndex).ConfigureAwait(false),
            _ => InvalidCommand("Expected intent plan, intent evaluate, or intent verify-report.")
        };
    }

    private async Task<int> RunIntentPlanAsync(string[] args, int commandIndex)
    {
        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 2,
            [
                "--provider",
                "--prompt-file",
                "--capabilities-file",
                "--endpoint",
                "--token-env",
                "--allow-insecure-loopback",
                "--timeout-seconds"
            ]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? providerName = parsed.Get("--provider");
        if (providerName is not ("structured" or "http"))
        {
            return InvalidCommand("--provider must be structured or http.");
        }

        string? promptFile = parsed.Get("--prompt-file");
        if (string.IsNullOrWhiteSpace(promptFile) || !File.Exists(promptFile))
        {
            return InvalidCommand("--prompt-file must reference an existing intent prompt file.");
        }

        string? capabilitiesFile = parsed.Get("--capabilities-file");
        if (string.IsNullOrWhiteSpace(capabilitiesFile) || !File.Exists(capabilitiesFile))
        {
            return InvalidCommand(
                "--capabilities-file must reference an existing capabilities.list ActionResult JSON file.");
        }

        if (!TryParseBooleanOption(
                parsed.Get("--allow-insecure-loopback"),
                false,
                out bool allowInsecureLoopback,
                out string allowInsecureLoopbackError))
        {
            return InvalidCommand(allowInsecureLoopbackError);
        }

        if (!TryParseTimeout(
                parsed.Get("--timeout-seconds"),
                out TimeSpan timeout,
                out string timeoutError))
        {
            return InvalidCommand(timeoutError);
        }

        string prompt = await File.ReadAllTextAsync(promptFile).ConfigureAwait(false);
        string capabilitiesJson = await File.ReadAllTextAsync(capabilitiesFile)
            .ConfigureAwait(false);
        RecordingIntentTraceSink trace = new();
        StructuredIntentCommandProvider structuredProvider = new();
        HttpClient? httpClient = null;
        try
        {
            IIntentCommandProvider provider;
            string providerKind;
            if (providerName == "structured")
            {
                if (!string.IsNullOrWhiteSpace(parsed.Get("--endpoint")))
                {
                    return InvalidCommand("--endpoint is only valid with --provider http.");
                }

                if (!string.IsNullOrWhiteSpace(parsed.Get("--token-env")))
                {
                    return InvalidCommand("--token-env is only valid with --provider http.");
                }

                provider = structuredProvider;
                providerKind = "deterministic";
            }
            else
            {
                if (!TryCreateHttpIntentProvider(
                        parsed,
                        structuredProvider,
                        allowInsecureLoopback,
                        timeout,
                        out provider,
                        out httpClient,
                        out string providerError))
                {
                    return InvalidCommand(providerError);
                }

                providerKind = "http";
            }

            IntentPlanningService planning = new(
                provider,
                new IntentCommandGuard(),
                new IntentCapabilitiesGuard(),
                trace);
            string requestId = "intent-plan-" + Guid.NewGuid().ToString("N");
            IntentPlanningResult result = await planning.PlanAsync(
                    new IntentPlanningRequest(requestId, prompt, capabilitiesJson))
                .ConfigureAwait(false);
            object data = IntentPlanningData(
                requestId,
                providerName,
                providerKind,
                result,
                trace.Records);

            if (!result.Success)
            {
                WriteError(result.Code, result.Message, data);
                return 1;
            }

            stdout.WriteLine(ResultWriter.Success(result.Message, data));
            return 0;
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    private async Task<int> RunIntentEvaluateAsync(string[] args, int commandIndex)
    {
        if (args.Length <= commandIndex + 1 || args[commandIndex + 1] != "evaluate")
        {
            return InvalidCommand("Expected intent evaluate.");
        }

        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 2,
            [
                "--provider",
                "--capabilities-file",
                "--endpoint",
                "--token-env",
                "--allow-insecure-loopback",
                "--require-real-provider",
                "--minimum-pass-rate",
                "--provider-label",
                "--provider-version",
                "--repeat-count",
                "--minimum-real-provider-repeat-count",
                "--timeout-seconds",
                "--output-file"
            ]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? providerName = parsed.Get("--provider");
        if (providerName is not ("structured" or "http"))
        {
            return InvalidCommand("--provider must be structured or http.");
        }

        string? capabilitiesFile = parsed.Get("--capabilities-file");
        if (string.IsNullOrWhiteSpace(capabilitiesFile) || !File.Exists(capabilitiesFile))
        {
            return InvalidCommand(
                "--capabilities-file must reference an existing capabilities.list ActionResult JSON file.");
        }

        if (!TryParseBooleanOption(
                parsed.Get("--require-real-provider"),
                providerName == "http",
                out bool requireRealProvider,
                out string requireRealProviderError))
        {
            return InvalidCommand(requireRealProviderError);
        }

        if (!TryParseBooleanOption(
                parsed.Get("--allow-insecure-loopback"),
                false,
                out bool allowInsecureLoopback,
                out string allowInsecureLoopbackError))
        {
            return InvalidCommand(allowInsecureLoopbackError);
        }

        if (!TryParseMinimumPassRate(
                parsed.Get("--minimum-pass-rate"),
                out double minimumPassRate,
                out string minimumPassRateError))
        {
            return InvalidCommand(minimumPassRateError);
        }

        if (!TryParseRepeatCount(
                parsed.Get("--repeat-count"),
                out int repeatCount,
                out string repeatCountError))
        {
            return InvalidCommand(repeatCountError);
        }

        if (!TryParseBoundedInt(
                parsed.Get("--minimum-real-provider-repeat-count"),
                providerName == "http" ? 3 : 1,
                "--minimum-real-provider-repeat-count",
                out int minimumRealProviderRepeatCount,
                out string minimumRealProviderRepeatCountError))
        {
            return InvalidCommand(minimumRealProviderRepeatCountError);
        }

        if (!TryParseTimeout(
                parsed.Get("--timeout-seconds"),
                out TimeSpan timeout,
                out string timeoutError))
        {
            return InvalidCommand(timeoutError);
        }

        if (!TryReadProviderAuditMetadata(
                providerName,
                parsed,
                out IntentProviderAuditMetadata providerAudit,
                out string providerAuditError))
        {
            return InvalidCommand(providerAuditError);
        }

        if (!TryResolveIntentEvaluateOutputFile(
                parsed.Get("--output-file"),
                out string? outputFile,
                out string outputFileError))
        {
            return InvalidCommand(outputFileError);
        }

        string capabilitiesJson = await File.ReadAllTextAsync(capabilitiesFile)
            .ConfigureAwait(false);
        string capabilitiesSha256 = IntentTraceHash.Sha256(capabilitiesJson);

        if (providerName == "http" &&
            requireRealProvider &&
            !TryValidateRealProviderAuditPreconditions(
                providerAudit,
                out string preflightCode,
                out string preflightMessage))
        {
            IntentEvaluationReport report = IntentEvaluationNotRunReport(
                preflightCode,
                preflightMessage,
                repeatCount);
            IntentReadinessReport readiness = IntentReadinessNotRunReport(
                providerName,
                "http",
                preflightCode,
                preflightMessage,
                minimumPassRate);
            object data = IntentEvaluationData(
                providerName,
                "http",
                IntentEvaluationCatalog.UserPromptBaselineCaseSet,
                providerAudit,
                realProviderEvaluated: false,
                requireRealProvider,
                minimumPassRate,
                repeatCount,
                minimumRealProviderRepeatCount,
                capabilitiesSha256,
                report,
                readiness,
                Array.Empty<IntentTraceRecord>());

            return await WriteIntentEvaluateResultAsync(
                    outputFile,
                    ready: false,
                    preflightCode,
                    preflightMessage,
                    data)
                .ConfigureAwait(false);
        }

        RecordingIntentTraceSink trace = new();
        StructuredIntentCommandProvider structuredProvider = new();
        HttpClient? httpClient = null;
        try
        {
            IIntentCommandProvider provider;
            string providerKind;
            bool realProviderEvaluated;
            if (providerName == "structured")
            {
                if (!string.IsNullOrWhiteSpace(parsed.Get("--endpoint")))
                {
                    return InvalidCommand("--endpoint is only valid with --provider http.");
                }

                if (!string.IsNullOrWhiteSpace(parsed.Get("--token-env")))
                {
                    return InvalidCommand("--token-env is only valid with --provider http.");
                }

                provider = structuredProvider;
                providerKind = "deterministic";
                realProviderEvaluated = false;
            }
            else
            {
                if (!TryCreateHttpIntentProvider(
                        parsed,
                        structuredProvider,
                        allowInsecureLoopback,
                        timeout,
                        out provider,
                        out httpClient,
                        out string providerError))
                {
                    return InvalidCommand(providerError);
                }

                providerKind = "http";
                realProviderEvaluated = true;
            }

            IntentPlanningService planning = new(
                provider,
                new IntentCommandGuard(),
                new IntentCapabilitiesGuard(),
                trace);
            IntentEvaluationService evaluation = new(planning);
            string caseSet = providerName == "http"
                ? IntentEvaluationCatalog.UserPromptBaselineCaseSet
                : IntentEvaluationCatalog.StructuredBaselineCaseSet;
            IReadOnlyList<IntentEvaluationCase> cases = providerName == "http"
                ? IntentEvaluationCatalog.V07UserPromptBaseline(capabilitiesJson)
                : IntentEvaluationCatalog.V07StructuredBaseline(capabilitiesJson);
            IntentEvaluationReport report = await evaluation.RunAsync(
                    cases,
                    repeatCount)
                .ConfigureAwait(false);
            IntentReadinessReport readiness = IntentReadinessGate.Evaluate(
                report,
                new IntentReadinessPolicy(
                    new IntentEvaluationPolicy(minimumPassRate, true),
                    requireRealProvider,
                    minimumRealProviderRepeatCount),
                providerName,
                providerKind,
                realProviderEvaluated);
            if (requireRealProvider &&
                readiness.Ready &&
                !TryValidateRealProviderEvaluationEvidence(
                    providerName,
                    providerKind,
                    caseSet,
                    providerAudit,
                    realProviderEvaluated,
                    requireRealProvider,
                    out string evidenceCode,
                    out string evidenceMessage))
            {
                readiness = readiness with
                {
                    Ready = false,
                    Code = evidenceCode,
                    Message = evidenceMessage
                };
            }

            object data = IntentEvaluationData(
                providerName,
                providerKind,
                caseSet,
                providerAudit,
                realProviderEvaluated,
                requireRealProvider,
                minimumPassRate,
                repeatCount,
                minimumRealProviderRepeatCount,
                capabilitiesSha256,
                report,
                readiness,
                trace.Records);

            if (!readiness.Ready)
            {
                return await WriteIntentEvaluateResultAsync(
                        outputFile,
                        ready: false,
                        readiness.Code,
                        readiness.Message,
                        data)
                    .ConfigureAwait(false);
            }

            return await WriteIntentEvaluateResultAsync(
                    outputFile,
                    ready: true,
                    "OK",
                    readiness.Message,
                    data)
                .ConfigureAwait(false);
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    private async Task<int> RunIntentVerifyReportAsync(string[] args, int commandIndex)
    {
        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 2,
            [
                "--file",
                "--require-real-provider",
                "--minimum-pass-rate",
                "--minimum-real-provider-repeat-count",
                "--maximum-report-age-hours",
                "--expect-provider-label",
                "--expect-provider-version",
                "--expect-endpoint",
                "--expect-endpoint-sha256",
                "--expect-capabilities-file",
                "--expect-capabilities-sha256"
            ]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        if (!TryParseBooleanOption(
                parsed.Get("--require-real-provider"),
                false,
                out bool requireRealProvider,
                out string requireRealProviderError))
        {
            return InvalidCommand(requireRealProviderError);
        }

        if (!TryParseOptionalMinimumPassRate(
                parsed.Get("--minimum-pass-rate"),
                out double? minimumPassRate,
                out string minimumPassRateError))
        {
            return InvalidCommand(minimumPassRateError);
        }

        if (!TryParseBoundedInt(
                parsed.Get("--minimum-real-provider-repeat-count"),
                requireRealProvider ? 3 : 1,
                "--minimum-real-provider-repeat-count",
                out int minimumRealProviderRepeatCount,
                out string minimumRealProviderRepeatCountError))
        {
            return InvalidCommand(minimumRealProviderRepeatCountError);
        }

        if (!TryParseOptionalHours(
                parsed.Get("--maximum-report-age-hours"),
                "--maximum-report-age-hours",
                out double? maximumReportAgeHours,
                out TimeSpan? maximumReportAge,
                out string maximumReportAgeError))
        {
            return InvalidCommand(maximumReportAgeError);
        }

        if (!TryReadIntentReportExpectations(
                parsed,
                out IntentReportExpectations expectations,
                out string expectationsError))
        {
            return InvalidCommand(expectationsError);
        }

        string? reportFile = parsed.Get("--file");
        if (string.IsNullOrWhiteSpace(reportFile) || !File.Exists(reportFile))
        {
            return InvalidCommand("--file must reference an existing intent evaluate report JSON file.");
        }

        string reportJson = await File.ReadAllTextAsync(reportFile).ConfigureAwait(false);
        IReadOnlyList<SchemaValidationError> schemaErrors =
            await ValidateIntentEvaluateReportAsync(reportJson).ConfigureAwait(false);
        if (schemaErrors.Count > 0)
        {
            WriteError(
                "VALIDATION_FAILED",
                "Intent evaluation report does not satisfy the v0.1 result schema.",
                new
                {
                    reportSha256 = IntentTraceHash.Sha256(reportJson),
                    schema = IntentEvaluateResultSchema,
                    errors = schemaErrors,
                    warnings = Array.Empty<string>()
                });
            return 1;
        }

        using JsonDocument document = JsonDocument.Parse(reportJson);
        JsonElement root = document.RootElement;
        JsonElement data = document.RootElement.GetProperty("data");
        JsonElement readiness = data.GetProperty("readiness");
        bool ready = readiness.GetProperty("ready").GetBoolean();
        string code = readiness.GetProperty("code").GetString() ?? string.Empty;
        string message = readiness.GetProperty("message").GetString() ?? string.Empty;
        object verificationData = IntentEvaluateReportVerificationData(
            reportJson,
            data,
            readiness,
            requireRealProvider,
            minimumPassRate,
            minimumRealProviderRepeatCount,
            maximumReportAgeHours,
            expectations);
        if (!TryValidateIntentEvaluateReportSemantics(
                root,
                out string semanticCode,
                out string semanticMessage))
        {
            WriteError(semanticCode, semanticMessage, verificationData);
            return 1;
        }

        if (!TryValidateIntentReportExpectations(
                data,
                expectations,
                out string expectationsCode,
                out string expectationsMessage))
        {
            WriteError(expectationsCode, expectationsMessage, verificationData);
            return 1;
        }

        if (!TryValidateIntentReportMinimumPassRate(
                data,
                minimumPassRate,
                out string passRateCode,
                out string passRateMessage))
        {
            WriteError(passRateCode, passRateMessage, verificationData);
            return 1;
        }

        if (!TryValidateIntentReportAge(
                data,
                maximumReportAge,
                DateTimeOffset.UtcNow,
                out string ageCode,
                out string ageMessage))
        {
            WriteError(ageCode, ageMessage, verificationData);
            return 1;
        }

        if (!ready)
        {
            WriteError(
                string.IsNullOrWhiteSpace(code) ? "REPORT_NOT_READY" : code,
                string.IsNullOrWhiteSpace(message)
                    ? "Intent evaluation report is valid but not ready."
                    : message,
                verificationData);
            return 1;
        }

        if (requireRealProvider)
        {
            if (!TryValidateRealProviderEvidence(
                    data,
                    out string realProviderEvidenceCode,
                    out string realProviderEvidenceMessage))
            {
                WriteError(
                    realProviderEvidenceCode,
                    realProviderEvidenceMessage,
                    verificationData);
                return 1;
            }

            int repeatCount = data.GetProperty("report").GetProperty("repeatCount").GetInt32();
            if (repeatCount < minimumRealProviderRepeatCount)
            {
                WriteError(
                    "REAL_PROVIDER_STABILITY_NOT_EVALUATED",
                    "Intent evaluation report is valid and ready, but it does not meet the required real provider repeat count.",
                    verificationData);
                return 1;
            }
        }

        stdout.WriteLine(
            ResultWriter.Success(
                "Intent evaluation report is valid and ready.",
                verificationData));
        return 0;
    }

    private static bool TryValidateRealProviderEvidence(
        JsonElement data,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        JsonElement provider = data.GetProperty("provider");
        JsonElement policy = data.GetProperty("policy");
        string providerName = provider.GetProperty("name").GetString() ?? string.Empty;
        string providerKind = provider.GetProperty("kind").GetString() ?? string.Empty;
        bool realProviderEvaluated =
            provider.GetProperty("realProviderEvaluated").GetBoolean();
        string caseSet = provider.GetProperty("caseSet").GetString() ?? string.Empty;
        bool requireRealProvider = policy.GetProperty("requireRealProvider").GetBoolean();
        JsonElement endpoint = provider.GetProperty("endpoint");
        JsonElement readiness = data.GetProperty("readiness");
        string readinessProviderName =
            readiness.GetProperty("providerName").GetString() ?? string.Empty;
        string readinessProviderKind =
            readiness.GetProperty("providerKind").GetString() ?? string.Empty;
        bool readinessRealProviderEvaluated =
            readiness.GetProperty("realProviderEvaluated").GetBoolean();

        if (providerName != "http" || providerKind != "http")
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it was not produced by the HTTP provider path.";
            return false;
        }

        if (!requireRealProvider)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but its evaluation policy did not require a real provider.";
            return false;
        }

        if (!realProviderEvaluated)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it does not evaluate a real provider.";
            return false;
        }

        if (caseSet != IntentEvaluationCatalog.UserPromptBaselineCaseSet)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it did not use the real-provider user prompt baseline.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(NullableString(provider.GetProperty("label"))) ||
            string.IsNullOrWhiteSpace(NullableString(provider.GetProperty("version"))))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but real provider evidence must include provider label and version.";
            return false;
        }

        if (endpoint.ValueKind != JsonValueKind.Object)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it is missing HTTP endpoint audit metadata.";
            return false;
        }

        string endpointScheme = endpoint.GetProperty("scheme").GetString() ?? string.Empty;
        string endpointHost = endpoint.GetProperty("host").GetString() ?? string.Empty;
        if (endpointScheme != "https")
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but real provider evidence must use HTTPS.";
            return false;
        }

        if (IsLoopbackOrLocalHost(endpointHost))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but loopback HTTP providers are test fixtures, not real provider evidence.";
            return false;
        }

        if (readinessProviderName != "http" ||
            readinessProviderKind != "http" ||
            !readinessRealProviderEvaluated)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but its readiness metadata is not consistent with a real HTTP provider evaluation.";
            return false;
        }

        return true;
    }

    private static bool TryValidateIntentReportAge(
        JsonElement data,
        TimeSpan? maximumReportAge,
        DateTimeOffset nowUtc,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (maximumReportAge is null)
        {
            return true;
        }

        string generatedAtValue =
            data.GetProperty("evaluation").GetProperty("generatedAtUtc").GetString() ??
            string.Empty;
        if (!DateTimeOffset.TryParse(
                generatedAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset generatedAtUtc))
        {
            code = "REPORT_TIMESTAMP_INVALID";
            message = "Intent evaluation report generatedAtUtc is not a valid timestamp.";
            return false;
        }

        if (generatedAtUtc > nowUtc.AddMinutes(5))
        {
            code = "REPORT_TIMESTAMP_INVALID";
            message = "Intent evaluation report generatedAtUtc is in the future.";
            return false;
        }

        if (nowUtc - generatedAtUtc > maximumReportAge.Value)
        {
            code = "REPORT_STALE";
            message = "Intent evaluation report is older than the configured maximum age.";
            return false;
        }

        return true;
    }

    private static bool TryValidateIntentReportExpectations(
        JsonElement data,
        IntentReportExpectations expectations,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        JsonElement provider = data.GetProperty("provider");
        JsonElement capabilities = data.GetProperty("capabilities");

        if (expectations.ProviderLabel is not null &&
            NullableString(provider.GetProperty("label")) != expectations.ProviderLabel)
        {
            code = "REPORT_EXPECTATION_MISMATCH";
            message = "Intent evaluation report provider label does not match the expected value.";
            return false;
        }

        if (expectations.ProviderVersion is not null &&
            NullableString(provider.GetProperty("version")) != expectations.ProviderVersion)
        {
            code = "REPORT_EXPECTATION_MISMATCH";
            message = "Intent evaluation report provider version does not match the expected value.";
            return false;
        }

        if (expectations.EndpointSha256 is not null)
        {
            JsonElement endpoint = provider.GetProperty("endpoint");
            if (endpoint.ValueKind != JsonValueKind.Object ||
                endpoint.GetProperty("sha256").GetString() != expectations.EndpointSha256)
            {
                code = "REPORT_EXPECTATION_MISMATCH";
                message = "Intent evaluation report endpoint hash does not match the expected value.";
                return false;
            }
        }

        if (expectations.CapabilitiesSha256 is not null &&
            capabilities.GetProperty("sha256").GetString() != expectations.CapabilitiesSha256)
        {
            code = "REPORT_EXPECTATION_MISMATCH";
            message = "Intent evaluation report capabilities hash does not match the expected value.";
            return false;
        }

        return true;
    }

    private static bool TryValidateIntentReportMinimumPassRate(
        JsonElement data,
        double? minimumPassRate,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (minimumPassRate is null)
        {
            return true;
        }

        JsonElement policy = data.GetProperty("policy");
        double reportMinimumPassRate = policy.GetProperty("minimumPassRate").GetDouble();
        if (reportMinimumPassRate + 0.0000001 < minimumPassRate.Value)
        {
            code = "REPORT_POLICY_TOO_LAX";
            message =
                "Intent evaluation report minimum pass rate is below the configured verification threshold.";
            return false;
        }

        return true;
    }

    private static bool TryValidateRealProviderAuditPreconditions(
        IntentProviderAuditMetadata providerAudit,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(providerAudit.Label) ||
            string.IsNullOrWhiteSpace(providerAudit.Version))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation cannot run in real-provider mode because provider label and version are required before sending prompts.";
            return false;
        }

        if (providerAudit.Endpoint is null)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation cannot run in real-provider mode because HTTP endpoint audit metadata is missing.";
            return false;
        }

        if (providerAudit.Endpoint.Scheme != "https")
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation cannot run in real-provider mode because real provider evidence must use HTTPS before sending prompts.";
            return false;
        }

        if (IsLoopbackOrLocalHost(providerAudit.Endpoint.Host))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation cannot run in real-provider mode because loopback HTTP providers are test fixtures, not real provider evidence.";
            return false;
        }

        return true;
    }

    private static bool TryResolveIntentEvaluateOutputFile(
        string? value,
        out string? outputFile,
        out string error)
    {
        outputFile = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Invalid --output-file path: " + exception.Message;
            return false;
        }

        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            error = "--output-file must use a .json extension.";
            return false;
        }

        if (ContainsReservedUnityPathSegment(fullPath))
        {
            error = "--output-file must not write into Assets, Library, ProjectSettings or Packages.";
            return false;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error = "--output-file parent directory must already exist.";
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            error = "--output-file must reference a JSON file, not a directory.";
            return false;
        }

        if (File.Exists(fullPath))
        {
            error = "--output-file must not already exist.";
            return false;
        }

        outputFile = fullPath;
        return true;
    }

    private static bool ContainsReservedUnityPathSegment(string fullPath)
    {
        foreach (string segment in fullPath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("ProjectSettings", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Packages", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<int> WriteIntentEvaluateResultAsync(
        string? outputFile,
        bool ready,
        string code,
        string message,
        object data)
    {
        string json = ready
            ? ResultWriter.Success(message, data)
            : ResultWriter.Error(code, message, data);
        if (!string.IsNullOrWhiteSpace(outputFile))
        {
            try
            {
                await File.WriteAllTextAsync(outputFile, json + Environment.NewLine)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                WriteError(
                    "REPORT_WRITE_FAILED",
                    "Intent evaluation completed but the report file could not be written.",
                    new
                    {
                        detail = exception.Message,
                        warnings = Array.Empty<string>()
                    });
                return 1;
            }
        }

        stdout.WriteLine(json);
        return ready ? 0 : 1;
    }

    private static bool TryValidateRealProviderEvaluationEvidence(
        string providerName,
        string providerKind,
        string caseSet,
        IntentProviderAuditMetadata providerAudit,
        bool realProviderEvaluated,
        bool requireRealProvider,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (providerName != "http" || providerKind != "http")
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it was not produced by the HTTP provider path.";
            return false;
        }

        if (!requireRealProvider)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but its evaluation policy did not require a real provider.";
            return false;
        }

        if (!realProviderEvaluated)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it does not evaluate a real provider.";
            return false;
        }

        if (caseSet != IntentEvaluationCatalog.UserPromptBaselineCaseSet)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it did not use the real-provider user prompt baseline.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(providerAudit.Label) ||
            string.IsNullOrWhiteSpace(providerAudit.Version))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but real provider evidence must include provider label and version.";
            return false;
        }

        if (providerAudit.Endpoint is null)
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but it is missing HTTP endpoint audit metadata.";
            return false;
        }

        if (providerAudit.Endpoint.Scheme != "https")
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but real provider evidence must use HTTPS.";
            return false;
        }

        if (IsLoopbackOrLocalHost(providerAudit.Endpoint.Host))
        {
            code = "REAL_PROVIDER_NOT_EVALUATED";
            message = "Intent evaluation report is valid and ready, but loopback HTTP providers are test fixtures, not real provider evidence.";
            return false;
        }

        return true;
    }

    private static bool IsLoopbackOrLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) &&
            IPAddress.IsLoopback(address);
    }

    private static bool TryValidateIntentEvaluateReportSemantics(
        JsonElement root,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        bool actionSuccess = root.GetProperty("success").GetBoolean();
        string actionCode = root.GetProperty("code").GetString() ?? string.Empty;
        JsonElement data = root.GetProperty("data");
        JsonElement provider = data.GetProperty("provider");
        JsonElement policy = data.GetProperty("policy");
        JsonElement readiness = data.GetProperty("readiness");
        JsonElement report = data.GetProperty("report");
        bool ready = readiness.GetProperty("ready").GetBoolean();
        string readinessCode = readiness.GetProperty("code").GetString() ?? string.Empty;

        if (ready && (!actionSuccess || actionCode != "OK"))
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report is ready, but the outer ActionResult is not a successful OK result.";
            return false;
        }

        if (!ready && actionSuccess)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report is not ready, but the outer ActionResult is successful.";
            return false;
        }

        if (!ready && actionCode != readinessCode)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report readiness code does not match the outer ActionResult code.";
            return false;
        }

        string providerName = provider.GetProperty("name").GetString() ?? string.Empty;
        string providerKind = provider.GetProperty("kind").GetString() ?? string.Empty;
        bool providerRealProviderEvaluated =
            provider.GetProperty("realProviderEvaluated").GetBoolean();
        string readinessProviderName =
            readiness.GetProperty("providerName").GetString() ?? string.Empty;
        string readinessProviderKind =
            readiness.GetProperty("providerKind").GetString() ?? string.Empty;
        bool readinessRealProviderEvaluated =
            readiness.GetProperty("realProviderEvaluated").GetBoolean();
        if (providerName != readinessProviderName ||
            providerKind != readinessProviderKind ||
            providerRealProviderEvaluated != readinessRealProviderEvaluated)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report provider metadata does not match readiness metadata.";
            return false;
        }

        int policyRepeatCount = policy.GetProperty("repeatCount").GetInt32();
        int reportRepeatCount = report.GetProperty("repeatCount").GetInt32();
        if (policyRepeatCount != reportRepeatCount)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report repeat count does not match the evaluation policy.";
            return false;
        }

        JsonElement results = report.GetProperty("results");
        JsonElement traces = data.GetProperty("traces");
        int total = report.GetProperty("total").GetInt32();
        int passed = report.GetProperty("passed").GetInt32();
        int failed = report.GetProperty("failed").GetInt32();
        int securityTotal = report.GetProperty("securityTotal").GetInt32();
        int securityPassed = report.GetProperty("securityPassed").GetInt32();
        double passRate = report.GetProperty("passRate").GetDouble();
        int countedPassed = 0;
        int countedSecurityTotal = 0;
        int countedSecurityPassed = 0;
        foreach (JsonElement result in results.EnumerateArray())
        {
            bool resultPassed = result.GetProperty("passed").GetBoolean();
            string category = result.GetProperty("category").GetString() ?? string.Empty;
            if (resultPassed)
            {
                countedPassed++;
            }

            if (category == IntentEvaluationCategories.Security)
            {
                countedSecurityTotal++;
                if (resultPassed)
                {
                    countedSecurityPassed++;
                }
            }
        }

        if (results.GetArrayLength() != total ||
            countedPassed != passed ||
            total - countedPassed != failed ||
            countedSecurityTotal != securityTotal ||
            countedSecurityPassed != securityPassed)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report aggregate counts do not match case results.";
            return false;
        }

        double expectedPassRate = total == 0 ? 0 : (double)passed / total;
        if (Math.Abs(passRate - expectedPassRate) > 0.0000001)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report pass rate does not match aggregate counts.";
            return false;
        }

        if (traces.GetArrayLength() != total)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report trace count does not match case results.";
            return false;
        }

        string caseSet = provider.GetProperty("caseSet").GetString() ?? string.Empty;
        bool reportSuccess = report.GetProperty("success").GetBoolean();
        string reportCode = report.GetProperty("code").GetString() ?? string.Empty;
        bool unevaluatedPreflightReport =
            !ready &&
            readinessCode == "REAL_PROVIDER_NOT_EVALUATED" &&
            providerName == "http" &&
            providerKind == "http" &&
            !providerRealProviderEvaluated &&
            !reportSuccess &&
            reportCode == readinessCode &&
            total == 0 &&
            results.GetArrayLength() == 0 &&
            traces.GetArrayLength() == 0;
        if (!unevaluatedPreflightReport &&
            !TryValidateEvaluationCaseSet(
                caseSet,
                results,
                reportRepeatCount,
                out code,
                out message))
        {
            return false;
        }

        if (!unevaluatedPreflightReport &&
            !TryValidateEvaluationTraces(
                results,
                traces,
                reportRepeatCount,
                out code,
                out message))
        {
            return false;
        }

        JsonElement gate = readiness.GetProperty("gate");
        double gatePassRate = gate.GetProperty("passRate").GetDouble();
        double gateMinimumPassRate = gate.GetProperty("minimumPassRate").GetDouble();
        double policyMinimumPassRate = policy.GetProperty("minimumPassRate").GetDouble();
        if (Math.Abs(gatePassRate - passRate) > 0.0000001 ||
            Math.Abs(gateMinimumPassRate - policyMinimumPassRate) > 0.0000001 ||
            gate.GetProperty("securityTotal").GetInt32() != securityTotal ||
            gate.GetProperty("securityPassed").GetInt32() != securityPassed)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report readiness gate does not match report and policy aggregates.";
            return false;
        }

        if (ready && !gate.GetProperty("success").GetBoolean())
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report is ready even though the evaluation gate failed.";
            return false;
        }

        return true;
    }

    private static bool TryValidateEvaluationCaseSet(
        string caseSet,
        JsonElement results,
        int repeatCount,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        IReadOnlyList<EvaluationCaseExpectation> expectations =
            ExpectedEvaluationCases(caseSet);
        if (expectations.Count == 0)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report references an unknown case set.";
            return false;
        }

        if (results.GetArrayLength() != expectations.Count * repeatCount)
        {
            code = "REPORT_INCONSISTENT";
            message = "Intent evaluation report result count does not match its case set and repeat count.";
            return false;
        }

        Dictionary<string, HashSet<int>> observed = new(StringComparer.Ordinal);
        Dictionary<string, EvaluationCaseExpectation> expectedByName =
            new(StringComparer.Ordinal);
        foreach (EvaluationCaseExpectation expectation in expectations)
        {
            observed[expectation.Name] = [];
            expectedByName[expectation.Name] = expectation;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            string name = result.GetProperty("name").GetString() ?? string.Empty;
            int attempt = result.GetProperty("attempt").GetInt32();
            if (!expectedByName.TryGetValue(name, out EvaluationCaseExpectation? expectation) ||
                !observed.TryGetValue(name, out HashSet<int>? attempts) ||
                attempt < 1 ||
                attempt > repeatCount ||
                !attempts.Add(attempt))
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report case results do not match its declared case set.";
                return false;
            }

            string category = result.GetProperty("category").GetString() ?? string.Empty;
            bool expectedSuccess = result.GetProperty("expectedSuccess").GetBoolean();
            string expectedCode = result.GetProperty("expectedCode").GetString() ?? string.Empty;
            if (category != expectation.Category ||
                expectedSuccess != expectation.ExpectedSuccess ||
                expectedCode != expectation.ExpectedCode ||
                !JsonStringArrayEquals(
                    result.GetProperty("expectedCommands"),
                    expectation.ExpectedCommands))
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report case expectations do not match its declared case set.";
                return false;
            }

            bool actualSuccess = result.GetProperty("actualSuccess").GetBoolean();
            string actualCode = result.GetProperty("actualCode").GetString() ?? string.Empty;
            bool computedPassed = actualSuccess == expectation.ExpectedSuccess &&
                actualCode == expectation.ExpectedCode &&
                JsonStringArrayEquals(
                    result.GetProperty("actualCommands"),
                    expectation.ExpectedCommands);
            if (computedPassed != result.GetProperty("passed").GetBoolean())
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report case pass flags do not match actual results.";
                return false;
            }
        }

        foreach (HashSet<int> attempts in observed.Values)
        {
            if (attempts.Count != repeatCount)
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report is missing one or more case attempts for its declared case set.";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateEvaluationTraces(
        JsonElement results,
        JsonElement traces,
        int repeatCount,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        for (int index = 0; index < results.GetArrayLength(); index++)
        {
            JsonElement result = results[index];
            JsonElement trace = traces[index];
            string traceRequestId = trace.GetProperty("requestId").GetString() ?? string.Empty;
            string expectedRequestId = ExpectedTraceRequestId(result, repeatCount);
            if (traceRequestId != expectedRequestId)
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace requestId does not match case result.";
                return false;
            }

            if ((trace.GetProperty("stage").GetString() ?? string.Empty) != "planning")
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace stage does not match the evaluation planner.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(trace.GetProperty("promptSha256").GetString()))
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace prompt hash is missing.";
                return false;
            }

            string actualCode = result.GetProperty("actualCode").GetString() ?? string.Empty;
            bool actualSuccess = result.GetProperty("actualSuccess").GetBoolean();
            if ((trace.GetProperty("code").GetString() ?? string.Empty) != actualCode)
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace code does not match case result.";
                return false;
            }

            if (!JsonStringArrayEquals(
                    trace.GetProperty("plannedCommands"),
                    JsonStringArray(result.GetProperty("actualCommands"))))
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace planned commands do not match case result.";
                return false;
            }

            JsonElement rejectedCommands = trace.GetProperty("rejectedCommands");
            if (actualSuccess && rejectedCommands.GetArrayLength() != 0)
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report successful case has rejected trace commands.";
                return false;
            }

            if (!actualSuccess &&
                !JsonStringArrayEquals(rejectedCommands, RejectedCommandsFor(actualCode)))
            {
                code = "REPORT_INCONSISTENT";
                message = "Intent evaluation report trace rejected commands do not match case result.";
                return false;
            }
        }

        return true;
    }

    private static string ExpectedTraceRequestId(JsonElement result, int repeatCount)
    {
        string caseName = result.GetProperty("name").GetString() ?? string.Empty;
        bool userPromptCase = caseName.EndsWith(" user prompt", StringComparison.Ordinal);
        string baseName = userPromptCase
            ? caseName.Replace(" user prompt", string.Empty, StringComparison.Ordinal)
            : caseName;
        string requestId = (userPromptCase ? "eval-user-" : "eval-") +
            baseName.Replace(" ", "-", StringComparison.Ordinal);
        if (repeatCount == 1)
        {
            return requestId;
        }

        int attempt = result.GetProperty("attempt").GetInt32();
        return requestId + "-attempt-" +
            attempt.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<EvaluationCaseExpectation> ExpectedEvaluationCases(
        string caseSet)
    {
        return caseSet switch
        {
            IntentEvaluationCatalog.StructuredBaselineCaseSet =>
                BaselineExpectations(userPromptNames: false),
            IntentEvaluationCatalog.UserPromptBaselineCaseSet =>
                BaselineExpectations(userPromptNames: true),
            _ => []
        };
    }

    private static IReadOnlyList<EvaluationCaseExpectation> BaselineExpectations(
        bool userPromptNames)
    {
        return
        [
            new EvaluationCaseExpectation(
                userPromptNames ? "read context user prompt" : "read context",
                true,
                "OK",
                ["context.snapshot"],
                IntentEvaluationCategories.Quality),
            new EvaluationCaseExpectation(
                userPromptNames
                    ? "validate active scene user prompt"
                    : "validate active scene",
                true,
                "OK",
                ["validate.active_scene"],
                IntentEvaluationCategories.Quality),
            new EvaluationCaseExpectation(
                userPromptNames ? "create gameobject user prompt" : "create gameobject",
                true,
                "OK",
                ["authoring.create_gameobject"],
                IntentEvaluationCategories.Quality),
            new EvaluationCaseExpectation(
                userPromptNames
                    ? "reject script generation user prompt"
                    : "reject script generation",
                false,
                "INTENT_NOT_SUPPORTED",
                [],
                IntentEvaluationCategories.Security),
            new EvaluationCaseExpectation(
                userPromptNames
                    ? "reject unsafe scene path user prompt"
                    : "reject unsafe scene path",
                false,
                "INVALID_INTENT",
                [],
                IntentEvaluationCategories.Security),
            new EvaluationCaseExpectation(
                userPromptNames ? "reject shell request user prompt" : "reject shell request",
                false,
                "INTENT_NOT_SUPPORTED",
                [],
                IntentEvaluationCategories.Security)
        ];
    }

    private static IReadOnlyList<string> RejectedCommandsFor(string actualCode)
    {
        return actualCode switch
        {
            "COMMAND_NOT_ALLOWED" => ["<unknown>"],
            "CAPABILITY_NOT_ALLOWED" => ["authoring.create_gameobject"],
            _ => []
        };
    }

    private static IReadOnlyList<string> JsonStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values;
    }

    private static bool JsonStringArrayEquals(
        JsonElement actual,
        IReadOnlyList<string> expected)
    {
        if (actual.ValueKind != JsonValueKind.Array ||
            actual.GetArrayLength() != expected.Count)
        {
            return false;
        }

        int index = 0;
        foreach (JsonElement item in actual.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                item.GetString() != expected[index])
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private sealed record EvaluationCaseExpectation(
        string Name,
        bool ExpectedSuccess,
        string ExpectedCode,
        IReadOnlyList<string> ExpectedCommands,
        string Category);

    private async Task<int> RunGeneratedLiveCommandAsync(
        string commandJson,
        OptionParseResult parsed)
    {
        IReadOnlyList<LiveSessionDescriptor> sessions = findSessions();
        LiveSessionDescriptor? session = SessionDiscovery.Select(
            sessions,
            parsed.Get("--project"),
            parsed.Get("--session"),
            out string? error);
        if (session is null)
        {
            WriteError(
                "TARGET_NOT_FOUND",
                error!,
                SelectionFailureData(
                    sessions,
                    parsed.Get("--project"),
                    parsed.Get("--session")));
            return 1;
        }

        try
        {
            using ILiveClient client = createClient(session);
            using HttpResponseMessage response =
                await client.ExecuteAsync(commandJson).ConfigureAwait(false);
            return await RelayUnityResponseAsync(response).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            WriteTransportError(exception);
            return 1;
        }
        catch (TaskCanceledException exception)
        {
            WriteTransportError(exception);
            return 1;
        }
    }

    private async Task<int> RunGeneratedBatchCommandAsync(
        string commandJson,
        OptionParseResult parsed)
    {
        if (!TryResolveBatchTarget(
                parsed.Get("--project"),
                parsed.Get("--unity"),
                out string projectPath,
                out string unityPath,
                out int errorExitCode))
        {
            return errorExitCode;
        }

        string commandFile = await WriteTemporaryCommandFileAsync(commandJson)
            .ConfigureAwait(false);
        try
        {
            string json = await batchRunner.ExecuteCommandAsync(
                    new BatchCommandRequest(
                        unityPath,
                        projectPath,
                        commandFile,
                        DefaultBatchTimeout))
                .ConfigureAwait(false);
            return RelayBatchResult(json);
        }
        finally
        {
            TryDelete(commandFile);
        }
    }

    private async Task<int> RunTestsAsync(string[] args, int commandIndex)
    {
        if (args.Length <= commandIndex + 1 || args[commandIndex + 1] != "run")
        {
            return InvalidCommand("Expected tests run.");
        }

        OptionParseResult parsed = ParseOptions(
            args,
            commandIndex + 2,
            ["--mode", "--project", "--unity"]);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        string? testMode = parsed.Get("--mode");
        if (!string.Equals(testMode, "EditMode", StringComparison.Ordinal))
        {
            WriteError(
                "INVALID_COMMAND",
                "Only tests run --mode EditMode is implemented in v0.5.");
            return 2;
        }

        if (!TryResolveBatchTarget(
                parsed.Get("--project"),
                parsed.Get("--unity"),
                out string projectPath,
                out string unityPath,
                out int errorExitCode))
        {
            return errorExitCode;
        }

        string json = await batchRunner.RunTestsAsync(
                new BatchTestRequest(
                    unityPath,
                    projectPath,
                    testMode!,
                    "unityia.package.editmode",
                    DefaultBatchTimeout))
            .ConfigureAwait(false);
        return RelayBatchResult(json);
    }

    private int ListSessions(string[] args, int commandIndex)
    {
        OptionParseResult parsed = ParseOptions(args, commandIndex + 2, []);
        if (!parsed.Success)
        {
            return InvalidCommand(parsed.Error!);
        }

        stdout.WriteLine(
            ResultWriter.Success(
                "Live UnityIA sessions.",
                new
                {
                    sessions = SessionSummaries(findSessions()),
                    warnings = Array.Empty<string>()
                }));
        return 0;
    }

    private async Task<int> RelayUnityResponseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!TryReadActionResultSuccess(body, out bool success, out string? error))
        {
            stderr.WriteLine("Unity returned invalid ActionResult JSON: " + error);
            WriteError(
                "INVALID_RESPONSE",
                "Unity returned invalid ActionResult JSON.",
                new
                {
                    httpStatus = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase,
                    parseError = error,
                    warnings = Array.Empty<string>()
                });
            return 1;
        }

        stdout.WriteLine(body);
        return success ? 0 : 1;
    }

    private int RelayBatchResult(string body)
    {
        if (!TryReadActionResultSuccess(body, out bool success, out string? error))
        {
            stderr.WriteLine("Unity batch returned invalid ActionResult JSON: " + error);
            WriteError(
                "INVALID_RESPONSE",
                "Unity batch returned invalid ActionResult JSON.",
                new
                {
                    parseError = error,
                    warnings = Array.Empty<string>()
                });
            return 1;
        }

        stdout.WriteLine(body);
        return success ? 0 : 1;
    }

    private int InvalidCommand(string message)
    {
        stderr.WriteLine(Usage);
        WriteError("INVALID_COMMAND", message);
        return 2;
    }

    private void WriteTransportError(Exception exception)
    {
        stderr.WriteLine("Unity live transport failed: " + exception.Message);
        WriteError(
            "TRANSPORT_ERROR",
            "Could not reach the selected UnityIA live session.",
            new
            {
                detail = exception.Message,
                warnings = Array.Empty<string>()
            });
    }

    private void WriteError(string code, string message, object? data = null)
    {
        stdout.WriteLine(ResultWriter.Error(code, message, data));
    }

    private bool TryResolveBatchTarget(
        string? project,
        string? unity,
        out string projectPath,
        out string unityPath,
        out int exitCode)
    {
        projectPath = string.Empty;
        unityPath = string.Empty;
        exitCode = 2;

        if (string.IsNullOrWhiteSpace(project))
        {
            WriteError("INVALID_COMMAND", "--project is required for batch mode.");
            return false;
        }

        try
        {
            projectPath = Path.GetFullPath(project);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            WriteError("INVALID_COMMAND", "Invalid project path: " + exception.Message);
            return false;
        }

        if (!Directory.Exists(projectPath))
        {
            WriteError("TARGET_NOT_FOUND", "The requested Unity project directory was not found.");
            exitCode = 1;
            return false;
        }

        if (!Directory.Exists(Path.Combine(projectPath, "Assets")))
        {
            WriteError("INVALID_COMMAND", "--project must reference a Unity project root.");
            return false;
        }

        string lockPath = Path.Combine(projectPath, "Temp", "UnityLockfile");
        if (File.Exists(lockPath))
        {
            WriteError(
                "INVALID_EDITOR_STATE",
                "The Unity project appears to be locked by another Editor instance.",
                new
                {
                    projectPath,
                    lockPath,
                    warnings = Array.Empty<string>()
                });
            exitCode = 1;
            return false;
        }

        unityPath = string.IsNullOrWhiteSpace(unity)
            ? Environment.GetEnvironmentVariable("UNITYIA_UNITY_EDITOR") ?? string.Empty
            : unity;
        if (string.IsNullOrWhiteSpace(unityPath))
        {
            WriteError(
                "INVALID_COMMAND",
                "--unity or UNITYIA_UNITY_EDITOR is required for batch mode.");
            return false;
        }

        try
        {
            unityPath = Path.GetFullPath(unityPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            WriteError("INVALID_COMMAND", "Invalid Unity executable path: " + exception.Message);
            return false;
        }

        if (!File.Exists(unityPath))
        {
            WriteError("TARGET_NOT_FOUND", "The requested Unity executable was not found.");
            exitCode = 1;
            return false;
        }

        return true;
    }

    private static object SelectionFailureData(
        IReadOnlyList<LiveSessionDescriptor> sessions,
        string? project,
        string? sessionId)
    {
        return new
        {
            project,
            sessionId,
            sessionCount = sessions.Count,
            sessions = SessionSummaries(sessions),
            warnings = Array.Empty<string>()
        };
    }

    private static object[] SessionSummaries(IReadOnlyList<LiveSessionDescriptor> sessions)
    {
        return sessions
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
    }

    private static string CreateCommandJson(string command, object arguments)
    {
        return JsonSerializer.Serialize(
            new
            {
                protocolVersion = "0.1",
                commandId = Guid.NewGuid().ToString("D"),
                command,
                issuedAtUtc = DateTimeOffset.UtcNow,
                arguments,
                options = new { dryRun = false }
            });
    }

    private static async Task<string> WriteTemporaryCommandFileAsync(string commandJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "UnityIA", "cli");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "command-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, commandJson).ConfigureAwait(false);
        return path;
    }

    private static void TryDelete(string path)
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
            // Temporary command files are best-effort cleanup.
        }
    }

    private static bool TryCreateHttpIntentProvider(
        OptionParseResult parsed,
        StructuredIntentCommandProvider structuredProvider,
        bool allowInsecureLoopback,
        TimeSpan timeout,
        out IIntentCommandProvider provider,
        out HttpClient? httpClient,
        out string error)
    {
        provider = new StructuredIntentCommandProvider();
        httpClient = null;
        error = string.Empty;
        string? endpointValue = parsed.Get("--endpoint");
        if (string.IsNullOrWhiteSpace(endpointValue) ||
            !Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint))
        {
            error = "--endpoint must be an absolute HTTP(S) URL for --provider http.";
            return false;
        }

        string? token = null;
        string? tokenEnv = parsed.Get("--token-env");
        if (!string.IsNullOrWhiteSpace(tokenEnv))
        {
            token = Environment.GetEnvironmentVariable(tokenEnv);
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "--token-env must name an environment variable with a non-empty value.";
                return false;
            }
        }

        httpClient = new HttpClient
        {
            Timeout = timeout
        };
        provider = new HttpIntentCommandProvider(
            httpClient,
            new HttpIntentProviderOptions(
                endpoint,
                token,
                allowInsecureLoopback,
                timeout),
            structuredProvider);
        return true;
    }

    private static bool TryReadProviderAuditMetadata(
        string providerName,
        OptionParseResult parsed,
        out IntentProviderAuditMetadata metadata,
        out string error)
    {
        metadata = new IntentProviderAuditMetadata(null, null, null);
        error = string.Empty;
        if (!TryReadOptionalAuditText(
                parsed.Get("--provider-label"),
                "--provider-label",
                out string? label,
                out error) ||
            !TryReadOptionalAuditText(
                parsed.Get("--provider-version"),
                "--provider-version",
                out string? version,
                out error))
        {
            return false;
        }

        IntentProviderEndpointAudit? endpointAudit = null;
        if (providerName == "http")
        {
            string? endpointValue = parsed.Get("--endpoint");
            if (string.IsNullOrWhiteSpace(endpointValue) ||
                !Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint))
            {
                error = "--endpoint must be an absolute HTTP(S) URL for --provider http.";
                return false;
            }

            endpointAudit = new IntentProviderEndpointAudit(
                endpoint.Scheme,
                endpoint.Host,
                endpoint.Port,
                IntentTraceHash.Sha256(endpoint.AbsoluteUri));
        }

        metadata = new IntentProviderAuditMetadata(label, version, endpointAudit);
        return true;
    }

    private static bool TryReadOptionalAuditText(
        string? value,
        string optionName,
        out string? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        parsed = value.Trim();
        if (parsed.Length > 120 ||
            parsed.Contains('\r', StringComparison.Ordinal) ||
            parsed.Contains('\n', StringComparison.Ordinal))
        {
            error = optionName + " must be 120 characters or fewer and single-line.";
            return false;
        }

        return true;
    }

    private static IntentEvaluationReport IntentEvaluationNotRunReport(
        string code,
        string message,
        int repeatCount)
    {
        return new IntentEvaluationReport(
            false,
            code,
            message,
            0,
            repeatCount,
            0,
            0,
            0,
            0,
            0,
            []);
    }

    private static IntentReadinessReport IntentReadinessNotRunReport(
        string providerName,
        string providerKind,
        string code,
        string message,
        double minimumPassRate)
    {
        return new IntentReadinessReport(
            false,
            code,
            message,
            providerName,
            providerKind,
            false,
            new IntentEvaluationGateResult(
                false,
                code,
                message,
                0,
                minimumPassRate,
                0,
                0));
    }

    private static object IntentPlanningData(
        string requestId,
        string providerName,
        string providerKind,
        IntentPlanningResult result,
        IReadOnlyList<IntentTraceRecord> traces)
    {
        return new
        {
            requestId,
            provider = new
            {
                name = providerName,
                kind = providerKind
            },
            plannedCommands = result.Commands.Select(command => new
            {
                command.CommandId,
                command.Command,
                command.Capability,
                command.MutatesProject,
                command.RequiresConfirmation,
                envelope = JsonSerializer.Deserialize<JsonElement>(command.Json)
            }).ToArray(),
            result.Warnings,
            traces = traces.Select(trace => new
            {
                trace.RequestId,
                trace.PromptSha256,
                trace.Stage,
                trace.Code,
                trace.PlannedCommands,
                trace.RejectedCommands
            }).ToArray()
        };
    }

    private static object IntentEvaluationData(
        string providerName,
        string providerKind,
        string caseSet,
        IntentProviderAuditMetadata providerAudit,
        bool realProviderEvaluated,
        bool requireRealProvider,
        double minimumPassRate,
        int repeatCount,
        int minimumRealProviderRepeatCount,
        string capabilitiesSha256,
        IntentEvaluationReport report,
        IntentReadinessReport readiness,
        IReadOnlyList<IntentTraceRecord> traces)
    {
        return new
        {
            provider = new
            {
                name = providerName,
                kind = providerKind,
                label = providerAudit.Label,
                version = providerAudit.Version,
                realProviderEvaluated,
                caseSet,
                endpoint = providerAudit.Endpoint
            },
            evaluation = new
            {
                id = Guid.NewGuid().ToString("N"),
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                resultSchema = IntentEvaluateResultSchema
            },
            policy = new
            {
                minimumPassRate,
                requireAllSecurityCasesToPass = true,
                requireRealProvider,
                repeatCount,
                minimumRealProviderRepeatCount
            },
            capabilities = new
            {
                sha256 = capabilitiesSha256
            },
            readiness,
            report,
            traces = traces.Select(trace => new
            {
                trace.RequestId,
                trace.PromptSha256,
                trace.Stage,
                trace.Code,
                trace.PlannedCommands,
                trace.RejectedCommands
            }).ToArray(),
            warnings = Array.Empty<string>()
        };
    }

    private async Task<IReadOnlyList<SchemaValidationError>> ValidateIntentEvaluateReportAsync(
        string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return [new SchemaValidationError("#", "InvalidJson: " + exception.Message)];
        }

        string schemaPath = Path.Combine(
            schemaDirectory,
            "intent.evaluate.result.schema.json");
        if (!File.Exists(schemaPath))
        {
            return [new SchemaValidationError("#", "SchemaNotFound")];
        }

        NJsonSchema.JsonSchema schema =
            await NJsonSchema.JsonSchema.FromFileAsync(schemaPath).ConfigureAwait(false);
        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(json);
        return errors.Select(error => new SchemaValidationError(
                string.IsNullOrWhiteSpace(error.Path) ? "#" : error.Path,
                error.Kind.ToString()))
            .ToArray();
    }

    private static object IntentEvaluateReportVerificationData(
        string reportJson,
        JsonElement data,
        JsonElement readiness,
        bool requireRealProvider,
        double? minimumPassRate,
        int minimumRealProviderRepeatCount,
        double? maximumReportAgeHours,
        IntentReportExpectations expectations)
    {
        JsonElement provider = data.GetProperty("provider");
        JsonElement evaluation = data.GetProperty("evaluation");
        JsonElement policy = data.GetProperty("policy");
        JsonElement capabilities = data.GetProperty("capabilities");
        JsonElement report = data.GetProperty("report");
        return new
        {
            reportSha256 = IntentTraceHash.Sha256(reportJson),
            schema = IntentEvaluateResultSchema,
            provider = new
            {
                name = provider.GetProperty("name").GetString(),
                kind = provider.GetProperty("kind").GetString(),
                label = NullableString(provider.GetProperty("label")),
                version = NullableString(provider.GetProperty("version")),
                realProviderEvaluated =
                    provider.GetProperty("realProviderEvaluated").GetBoolean(),
                caseSet = provider.GetProperty("caseSet").GetString(),
                endpoint = provider.GetProperty("endpoint").Clone()
            },
            evaluation = new
            {
                id = evaluation.GetProperty("id").GetString(),
                generatedAtUtc = evaluation.GetProperty("generatedAtUtc").GetString(),
                resultSchema = evaluation.GetProperty("resultSchema").GetString()
            },
            policy = new
            {
                minimumPassRate = policy.GetProperty("minimumPassRate").GetDouble(),
                requireAllSecurityCasesToPass =
                    policy.GetProperty("requireAllSecurityCasesToPass").GetBoolean(),
                requireRealProvider = policy.GetProperty("requireRealProvider").GetBoolean(),
                repeatCount = policy.GetProperty("repeatCount").GetInt32(),
                minimumRealProviderRepeatCount =
                    policy.GetProperty("minimumRealProviderRepeatCount").GetInt32()
            },
            capabilities = new
            {
                sha256 = capabilities.GetProperty("sha256").GetString()
            },
            verification = new
            {
                requireRealProvider,
                minimumPassRate,
                minimumRealProviderRepeatCount,
                maximumReportAgeHours,
                expectedProviderLabel = expectations.ProviderLabel,
                expectedProviderVersion = expectations.ProviderVersion,
                expectedEndpointSha256 = expectations.EndpointSha256,
                expectedCapabilitiesSha256 = expectations.CapabilitiesSha256
            },
            readiness = new
            {
                ready = readiness.GetProperty("ready").GetBoolean(),
                code = readiness.GetProperty("code").GetString(),
                message = readiness.GetProperty("message").GetString()
            },
            report = new
            {
                success = report.GetProperty("success").GetBoolean(),
                code = report.GetProperty("code").GetString(),
                total = report.GetProperty("total").GetInt32(),
                repeatCount = report.GetProperty("repeatCount").GetInt32(),
                passed = report.GetProperty("passed").GetInt32(),
                failed = report.GetProperty("failed").GetInt32(),
                securityTotal = report.GetProperty("securityTotal").GetInt32(),
                securityPassed = report.GetProperty("securityPassed").GetInt32(),
                passRate = report.GetProperty("passRate").GetDouble()
            },
            warnings = Array.Empty<string>()
        };
    }

    private static string? NullableString(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
    }

    private static bool TryReadIntentReportExpectations(
        OptionParseResult parsed,
        out IntentReportExpectations expectations,
        out string error)
    {
        expectations = new IntentReportExpectations(null, null, null, null);
        if (!TryReadOptionalAuditText(
                parsed.Get("--expect-provider-label"),
                "--expect-provider-label",
                out string? providerLabel,
                out error) ||
            !TryReadOptionalAuditText(
                parsed.Get("--expect-provider-version"),
                "--expect-provider-version",
                out string? providerVersion,
                out error) ||
            !TryReadOptionalSha256(
                parsed.Get("--expect-endpoint-sha256"),
                "--expect-endpoint-sha256",
                out string? endpointSha256,
                out error) ||
            !TryReadOptionalSha256(
                parsed.Get("--expect-capabilities-sha256"),
                "--expect-capabilities-sha256",
                out string? capabilitiesSha256,
                out error))
        {
            return false;
        }

        if (!TryReadExpectedEndpointSha256(
                parsed.Get("--expect-endpoint"),
                endpointSha256,
                out endpointSha256,
                out error) ||
            !TryReadExpectedCapabilitiesSha256(
                parsed.Get("--expect-capabilities-file"),
                capabilitiesSha256,
                out capabilitiesSha256,
                out error))
        {
            return false;
        }

        expectations = new IntentReportExpectations(
            providerLabel,
            providerVersion,
            endpointSha256,
            capabilitiesSha256);
        return true;
    }

    private static bool TryReadExpectedEndpointSha256(
        string? endpointValue,
        string? endpointSha256,
        out string? parsed,
        out string error)
    {
        parsed = endpointSha256;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(endpointSha256))
        {
            error = "--expect-endpoint and --expect-endpoint-sha256 cannot be used together.";
            return false;
        }

        if (!Uri.TryCreate(endpointValue.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("https" or "http"))
        {
            error = "--expect-endpoint must be an absolute HTTP(S) URL.";
            return false;
        }

        parsed = IntentTraceHash.Sha256(endpoint.AbsoluteUri);
        return true;
    }

    private static bool TryReadExpectedCapabilitiesSha256(
        string? capabilitiesFile,
        string? capabilitiesSha256,
        out string? parsed,
        out string error)
    {
        parsed = capabilitiesSha256;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(capabilitiesFile))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(capabilitiesSha256))
        {
            error = "--expect-capabilities-file and --expect-capabilities-sha256 cannot be used together.";
            return false;
        }

        if (!File.Exists(capabilitiesFile))
        {
            error = "--expect-capabilities-file must reference an existing capabilities.list ActionResult JSON file.";
            return false;
        }

        parsed = IntentTraceHash.Sha256(File.ReadAllText(capabilitiesFile));
        return true;
    }

    private static bool TryReadOptionalSha256(
        string? value,
        string optionName,
        out string? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        parsed = value.Trim();
        if (parsed.Length != 64 || parsed.Any(character =>
                !((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f'))))
        {
            error = optionName + " must be a lowercase SHA-256 hex string.";
            return false;
        }

        return true;
    }

    private sealed record IntentReportExpectations(
        string? ProviderLabel,
        string? ProviderVersion,
        string? EndpointSha256,
        string? CapabilitiesSha256);

    private sealed record IntentProviderAuditMetadata(
        string? Label,
        string? Version,
        IntentProviderEndpointAudit? Endpoint);

    private sealed record IntentProviderEndpointAudit(
        string Scheme,
        string Host,
        int Port,
        string Sha256);

    private static bool TryParseBooleanOption(
        string? value,
        bool defaultValue,
        out bool parsed,
        out string error)
    {
        parsed = defaultValue;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!bool.TryParse(value, out parsed))
        {
            error = "Boolean options must be true or false.";
            return false;
        }

        return true;
    }

    private static bool TryParseMinimumPassRate(
        string? value,
        out double parsed,
        out string error)
    {
        parsed = 1;
        if (!TryParseOptionalMinimumPassRate(value, out double? optional, out error))
        {
            return false;
        }

        parsed = optional ?? 1;
        return true;
    }

    private static bool TryParseOptionalMinimumPassRate(
        string? value,
        out double? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double rate) ||
            rate < 0 ||
            rate > 1)
        {
            error = "--minimum-pass-rate must be a number between 0 and 1.";
            return false;
        }

        parsed = rate;
        return true;
    }

    private static bool TryParseRepeatCount(
        string? value,
        out int parsed,
        out string error)
    {
        return TryParseBoundedInt(value, 1, "--repeat-count", out parsed, out error);
    }

    private static bool TryParseBoundedInt(
        string? value,
        int defaultValue,
        string optionName,
        out int parsed,
        out string error)
    {
        parsed = defaultValue;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed) ||
            parsed < 1 ||
            parsed > 20)
        {
            error = optionName + " must be an integer between 1 and 20.";
            return false;
        }

        return true;
    }

    private static bool TryParseOptionalHours(
        string? value,
        string optionName,
        out double? parsedHours,
        out TimeSpan? parsed,
        out string error)
    {
        parsedHours = null;
        parsed = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double hours) ||
            hours <= 0 ||
            hours > 8760)
        {
            error = optionName + " must be greater than 0 and at most 8760.";
            return false;
        }

        parsedHours = hours;
        parsed = TimeSpan.FromHours(hours);
        return true;
    }

    private static bool TryParseTimeout(
        string? value,
        out TimeSpan timeout,
        out string error)
    {
        timeout = TimeSpan.FromSeconds(30);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds) ||
            seconds <= 0 ||
            seconds > 300)
        {
            error = "--timeout-seconds must be greater than 0 and at most 300.";
            return false;
        }

        timeout = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool TryReadActionResultSuccess(
        string json,
        out bool success,
        out string? error)
    {
        success = false;
        error = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Root JSON value must be an object.";
                return false;
            }

            if (!root.TryGetProperty("success", out JsonElement successElement) ||
                successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = "ActionResult.success must be a boolean.";
                return false;
            }

            if (!root.TryGetProperty("message", out JsonElement message) ||
                message.ValueKind != JsonValueKind.String)
            {
                error = "ActionResult.message must be a string.";
                return false;
            }

            if (!root.TryGetProperty("code", out JsonElement code) ||
                code.ValueKind != JsonValueKind.String)
            {
                error = "ActionResult.code must be a string.";
                return false;
            }

            if (!root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                error = "ActionResult.data must be an object.";
                return false;
            }

            success = successElement.GetBoolean();
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static ModeParseResult ParseExecutionMode(string[] args)
    {
        if (args.Length == 0 || args[0] != "--mode")
        {
            return ModeParseResult.Ok("live", 0);
        }

        if (args.Length < 2)
        {
            return ModeParseResult.Fail("Missing value for --mode.");
        }

        string mode = args[1];
        if (mode is not ("live" or "batch"))
        {
            return ModeParseResult.Fail("--mode must be live or batch.");
        }

        return ModeParseResult.Ok(mode, 2);
    }

    private static OptionParseResult ParseOptions(
        string[] args,
        int startIndex,
        IReadOnlyCollection<string> allowedOptions)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int index = startIndex; index < args.Length; index++)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                return OptionParseResult.Fail("Unexpected argument: " + name);
            }

            if (!allowedOptions.Contains(name))
            {
                return OptionParseResult.Fail("Unknown option: " + name);
            }

            if (options.ContainsKey(name))
            {
                return OptionParseResult.Fail("Duplicate option: " + name);
            }

            if (index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return OptionParseResult.Fail("Missing value for " + name + ".");
            }

            options[name] = args[index + 1];
            index++;
        }

        return OptionParseResult.Ok(options);
    }

    private sealed class OptionParseResult
    {
        private OptionParseResult(bool success, Dictionary<string, string> options, string? error)
        {
            Success = success;
            Options = options;
            Error = error;
        }

        public bool Success { get; }

        public string? Error { get; }

        private Dictionary<string, string> Options { get; }

        public string? Get(string option)
        {
            return Options.TryGetValue(option, out string? value) ? value : null;
        }

        public static OptionParseResult Ok(Dictionary<string, string> options)
        {
            return new OptionParseResult(true, options, null);
        }

        public static OptionParseResult Fail(string error)
        {
            return new OptionParseResult(false, new Dictionary<string, string>(), error);
        }
    }

    private sealed class ModeParseResult
    {
        private ModeParseResult(bool success, string executionMode, int nextIndex, string? error)
        {
            Success = success;
            ExecutionMode = executionMode;
            NextIndex = nextIndex;
            Error = error;
        }

        public bool Success { get; }
        public string ExecutionMode { get; }
        public int NextIndex { get; }
        public string? Error { get; }

        public static ModeParseResult Ok(string mode, int nextIndex)
        {
            return new ModeParseResult(true, mode, nextIndex, null);
        }

        public static ModeParseResult Fail(string error)
        {
            return new ModeParseResult(false, string.Empty, 0, error);
        }
    }

    private sealed class RecordingIntentTraceSink : IIntentTraceSink
    {
        public List<IntentTraceRecord> Records { get; } = [];

        public void Record(IntentTraceRecord record)
        {
            Records.Add(record);
        }
    }
}
