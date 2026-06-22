using System.Text.Json;

namespace UnityIA.Cli;

internal interface IPlannedCommandExecutor
{
    Task<IntentCommandExecutionResult> ExecuteAsync(
        PlannedCommand command,
        CancellationToken cancellationToken);
}

internal sealed record IntentCommandExecutionResult(
    string CommandId,
    string Command,
    bool Success,
    string Code,
    string Message);

internal sealed record IntentValidationExecutionResult(
    string ScenePath,
    string CommandId,
    string Command,
    bool Success,
    string Code,
    string Message);

internal sealed record IntentExecutionResult(
    bool Success,
    string Code,
    string Message,
    IntentPlanningResult Plan,
    IReadOnlyList<IntentCommandExecutionResult> CommandResults,
    IReadOnlyList<IntentValidationExecutionResult> ValidationResults);

internal sealed class IntentExecutionService
{
    private readonly IntentPlanningService planningService;
    private readonly IPlannedCommandExecutor executor;
    private readonly IIntentTraceSink traceSink;
    private readonly Func<Guid> newGuid;
    private readonly Func<DateTimeOffset> now;

    public IntentExecutionService(
        IntentPlanningService planningService,
        IPlannedCommandExecutor executor,
        IIntentTraceSink traceSink,
        Func<Guid>? newGuid = null,
        Func<DateTimeOffset>? now = null)
    {
        this.planningService = planningService;
        this.executor = executor;
        this.traceSink = traceSink;
        this.newGuid = newGuid ?? Guid.NewGuid;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IntentExecutionResult> ExecuteAsync(
        IntentPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        IntentPlanningResult plan =
            await planningService.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (!plan.Success)
        {
            return new IntentExecutionResult(
                false,
                plan.Code,
                plan.Message,
                plan,
                [],
                []);
        }

        List<IntentCommandExecutionResult> commandResults = [];
        foreach (PlannedCommand command in plan.Commands)
        {
            IntentCommandExecutionResult result =
                await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            commandResults.Add(result);
            if (!result.Success)
            {
                RecordExecutionTrace(request, plan, result.Code, commandResults, []);
                return new IntentExecutionResult(
                    false,
                    result.Code,
                    result.Message,
                    plan,
                    commandResults,
                    []);
            }
        }

        List<IntentValidationExecutionResult> validationResults =
            await RunPostValidationsAsync(plan, commandResults, cancellationToken)
                .ConfigureAwait(false);
        IntentValidationExecutionResult? failedValidation =
            validationResults.FirstOrDefault(result => !result.Success);
        if (failedValidation is not null)
        {
            RecordExecutionTrace(
                request,
                plan,
                "POST_VALIDATION_FAILED",
                commandResults,
                validationResults);
            return new IntentExecutionResult(
                false,
                "POST_VALIDATION_FAILED",
                failedValidation.Message,
                plan,
                commandResults,
                validationResults);
        }

        RecordExecutionTrace(request, plan, "OK", commandResults, validationResults);
        return new IntentExecutionResult(
            true,
            "OK",
            "Intent plan executed successfully.",
            plan,
            commandResults,
            validationResults);
    }

    private async Task<List<IntentValidationExecutionResult>> RunPostValidationsAsync(
        IntentPlanningResult plan,
        IReadOnlyList<IntentCommandExecutionResult> commandResults,
        CancellationToken cancellationToken)
    {
        List<IntentValidationExecutionResult> validationResults = [];
        HashSet<string> scenePaths = new(StringComparer.Ordinal);
        foreach (PlannedCommand command in plan.Commands)
        {
            if (!command.MutatesProject ||
                !commandResults.Any(result =>
                    result.CommandId == command.CommandId &&
                    result.Success) ||
                !TryReadScenePath(command.Json, out string scenePath))
            {
                continue;
            }

            if (!scenePaths.Add(scenePath))
            {
                continue;
            }

            PlannedCommand validation = CreateValidationCommand(scenePath);
            IntentCommandExecutionResult result =
                await executor.ExecuteAsync(validation, cancellationToken).ConfigureAwait(false);
            validationResults.Add(new IntentValidationExecutionResult(
                scenePath,
                result.CommandId,
                result.Command,
                result.Success,
                result.Code,
                result.Message));
        }

        return validationResults;
    }

    private PlannedCommand CreateValidationCommand(string scenePath)
    {
        Dictionary<string, object?> envelope = new(StringComparer.Ordinal)
        {
            ["protocolVersion"] = "0.1",
            ["commandId"] = newGuid().ToString("D"),
            ["command"] = "validate.active_scene",
            ["issuedAtUtc"] = now(),
            ["arguments"] = new Dictionary<string, object?>
            {
                ["scenePath"] = scenePath
            },
            ["options"] = new Dictionary<string, object?>
            {
                ["dryRun"] = false
            }
        };

        return new PlannedCommand(
            (string)envelope["commandId"]!,
            "validate.active_scene",
            "validation.scene.run",
            false,
            false,
            JsonSerializer.Serialize(envelope));
    }

    private void RecordExecutionTrace(
        IntentPlanningRequest request,
        IntentPlanningResult plan,
        string code,
        IReadOnlyList<IntentCommandExecutionResult> commandResults,
        IReadOnlyList<IntentValidationExecutionResult> validationResults)
    {
        traceSink.Record(new IntentTraceRecord(
            request.RequestId,
            string.IsNullOrWhiteSpace(request.Prompt)
                ? string.Empty
                : IntentTraceHash.Sha256(request.Prompt),
            code,
            plan.Commands.Select(command => command.Command).ToArray(),
            [],
            now())
        {
            Stage = "execution",
            CommandResults = commandResults
                .Select(result => new IntentCommandResultTrace(
                    result.CommandId,
                    result.Command,
                    result.Success,
                    result.Code))
                .ToArray(),
            ValidationResults = validationResults
                .Select(result => new IntentValidationResultTrace(
                    result.ScenePath,
                    result.Success,
                    result.Code))
                .ToArray()
        });
    }

    private static bool TryReadScenePath(string commandJson, out string scenePath)
    {
        scenePath = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(commandJson);
            if (!document.RootElement.TryGetProperty("arguments", out JsonElement arguments) ||
                arguments.ValueKind != JsonValueKind.Object ||
                !arguments.TryGetProperty("scenePath", out JsonElement scenePathElement) ||
                scenePathElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            scenePath = scenePathElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(scenePath);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class JsonPlannedCommandExecutor : IPlannedCommandExecutor
{
    private readonly Func<string, CancellationToken, Task<string>> executeJson;

    public JsonPlannedCommandExecutor(Func<string, CancellationToken, Task<string>> executeJson)
    {
        this.executeJson = executeJson;
    }

    public async Task<IntentCommandExecutionResult> ExecuteAsync(
        PlannedCommand command,
        CancellationToken cancellationToken)
    {
        string responseJson =
            await executeJson(command.Json, cancellationToken).ConfigureAwait(false);
        if (!TryReadActionResult(
                responseJson,
                out bool success,
                out string code,
                out string message))
        {
            return new IntentCommandExecutionResult(
                command.CommandId,
                command.Command,
                false,
                "INVALID_RESPONSE",
                "Executor returned invalid ActionResult JSON.");
        }

        return new IntentCommandExecutionResult(
            command.CommandId,
            command.Command,
            success,
            code,
            message);
    }

    private static bool TryReadActionResult(
        string json,
        out bool success,
        out string code,
        out string message)
    {
        success = false;
        code = string.Empty;
        message = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out JsonElement successElement) ||
                successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("code", out JsonElement codeElement) ||
                codeElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("message", out JsonElement messageElement) ||
                messageElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("data", out JsonElement dataElement) ||
                dataElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            success = successElement.GetBoolean();
            code = codeElement.GetString() ?? string.Empty;
            message = messageElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(code);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
