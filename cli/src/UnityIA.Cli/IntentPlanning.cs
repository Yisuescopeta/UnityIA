using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnityIA.Cli;

internal interface IIntentCommandProvider
{
    Task<IntentProviderResponse> ProposeCommandsAsync(
        IntentPlanningRequest request,
        CancellationToken cancellationToken);
}

internal interface IIntentTraceSink
{
    void Record(IntentTraceRecord record);
}

internal sealed record IntentPlanningRequest(
    string RequestId,
    string Prompt,
    string CapabilitiesJson);

internal sealed record IntentProviderResponse(
    IReadOnlyList<string> CommandJson,
    IReadOnlyList<string> Warnings,
    bool Success = true,
    string Code = "OK",
    string Message = "OK");

internal sealed record IntentPlanningResult(
    bool Success,
    string Code,
    string Message,
    IReadOnlyList<PlannedCommand> Commands,
    IReadOnlyList<string> Warnings);

internal sealed record PlannedCommand(
    string CommandId,
    string Command,
    string Capability,
    bool MutatesProject,
    bool RequiresConfirmation,
    string Json);

internal sealed record IntentTraceRecord(
    string RequestId,
    string PromptSha256,
    string Code,
    IReadOnlyList<string> PlannedCommands,
    IReadOnlyList<string> RejectedCommands,
    DateTimeOffset CreatedAtUtc)
{
    public string Stage { get; init; } = "planning";

    public IReadOnlyList<IntentCommandResultTrace> CommandResults { get; init; } = [];

    public IReadOnlyList<IntentValidationResultTrace> ValidationResults { get; init; } = [];
}

internal sealed record IntentCommandResultTrace(
    string CommandId,
    string Command,
    bool Success,
    string Code);

internal sealed record IntentValidationResultTrace(
    string ScenePath,
    bool Success,
    string Code);

internal sealed class IntentPlanningService
{
    private readonly IIntentCommandProvider provider;
    private readonly IntentCommandGuard guard;
    private readonly IntentCapabilitiesGuard capabilitiesGuard;
    private readonly IIntentTraceSink traceSink;
    private readonly Func<DateTimeOffset> now;

    public IntentPlanningService(
        IIntentCommandProvider provider,
        IntentCommandGuard guard,
        IntentCapabilitiesGuard capabilitiesGuard,
        IIntentTraceSink traceSink,
        Func<DateTimeOffset>? now = null)
    {
        this.provider = provider;
        this.guard = guard;
        this.capabilitiesGuard = capabilitiesGuard;
        this.traceSink = traceSink;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IntentPlanningResult> PlanAsync(
        IntentPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return Failure(request, "INVALID_REQUEST", "RequestId is required.", [], []);
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Failure(request, "INVALID_REQUEST", "Prompt is required.", [], []);
        }

        IntentCapabilitiesResult capabilities =
            capabilitiesGuard.TryRead(request.CapabilitiesJson);
        if (!capabilities.Success)
        {
            return Failure(
                request,
                "CAPABILITIES_REQUIRED",
                capabilities.Message,
                [],
                []);
        }

        IntentProviderResponse response =
            await provider.ProposeCommandsAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            return Failure(
                request,
                response.Code,
                response.Message,
                [],
                []);
        }

        List<PlannedCommand> commands = [];
        List<string> rejected = [];
        foreach (string commandJson in response.CommandJson)
        {
            IntentCommandGuardResult result = guard.TryPlan(commandJson);
            if (!result.Success)
            {
                rejected.Add(result.Command ?? "<unknown>");
                return Failure(
                    request,
                    "COMMAND_NOT_ALLOWED",
                    result.Message,
                    commands.Select(command => command.Command).ToArray(),
                    rejected);
            }

            IntentCapabilitiesDecision capabilitiesDecision =
                capabilitiesGuard.Evaluate(result.CommandPlan!, capabilities.Snapshot!);
            if (!capabilitiesDecision.Success)
            {
                rejected.Add(result.CommandPlan!.Command);
                return Failure(
                    request,
                    "CAPABILITY_NOT_ALLOWED",
                    capabilitiesDecision.Message,
                    commands.Select(command => command.Command).ToArray(),
                    rejected);
            }

            commands.Add(result.CommandPlan!);
        }

        traceSink.Record(new IntentTraceRecord(
            request.RequestId,
            IntentTraceHash.Sha256(request.Prompt),
            "OK",
            commands.Select(command => command.Command).ToArray(),
            [],
            now()));

        return new IntentPlanningResult(
            true,
            "OK",
            "Intent was mapped to public UnityIA commands.",
            commands,
            response.Warnings);
    }

    private IntentPlanningResult Failure(
        IntentPlanningRequest request,
        string code,
        string message,
        IReadOnlyList<string> plannedCommands,
        IReadOnlyList<string> rejectedCommands)
    {
        traceSink.Record(new IntentTraceRecord(
            request.RequestId,
            string.IsNullOrWhiteSpace(request.Prompt)
                ? string.Empty
                : IntentTraceHash.Sha256(request.Prompt),
            code,
            plannedCommands,
            rejectedCommands,
            now()));

        return new IntentPlanningResult(
            false,
            code,
            message,
            [],
            []);
    }

}

internal static class IntentTraceHash
{
    public static string Sha256(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal sealed class IntentCapabilitiesGuard
{
    public IntentCapabilitiesResult TryRead(string capabilitiesJson)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesJson))
        {
            return IntentCapabilitiesResult.Fail(
                "A successful capabilities.list ActionResult is required.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(capabilitiesJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return IntentCapabilitiesResult.Fail(
                    "Capabilities ActionResult root must be an object.");
            }

            if (!root.TryGetProperty("success", out JsonElement success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !success.GetBoolean())
            {
                return IntentCapabilitiesResult.Fail(
                    "A successful capabilities.list ActionResult is required.");
            }

            if (!root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("commands", out JsonElement commands) ||
                commands.ValueKind != JsonValueKind.Array)
            {
                return IntentCapabilitiesResult.Fail(
                    "Capabilities data.commands must be an array.");
            }

            Dictionary<string, CapabilityCommand> byName =
                new(StringComparer.Ordinal);
            foreach (JsonElement command in commands.EnumerateArray())
            {
                if (command.ValueKind != JsonValueKind.Object ||
                    !TryGetString(command, "name", out string name))
                {
                    continue;
                }

                byName[name] = new CapabilityCommand(
                    name,
                    TryGetString(command, "surface", out string surface) ? surface : string.Empty,
                    TryGetString(command, "status", out string status) ? status : string.Empty,
                    TryGetString(command, "capability", out string capability)
                        ? capability
                        : string.Empty,
                    TryGetBoolean(command, "isMutation", out bool isMutation) && isMutation,
                    TryGetBoolean(command, "requiresConfirmation", out bool requiresConfirmation) &&
                        requiresConfirmation,
                    TryGetPermissionAllowed(command, out bool allowed) && allowed,
                    TryGetPermissionReason(command));
            }

            return IntentCapabilitiesResult.Ok(new IntentCapabilitiesSnapshot(byName));
        }
        catch (JsonException exception)
        {
            return IntentCapabilitiesResult.Fail(
                "Invalid capabilities.list ActionResult JSON: " + exception.Message);
        }
    }

    public IntentCapabilitiesDecision Evaluate(
        PlannedCommand command,
        IntentCapabilitiesSnapshot snapshot)
    {
        if (!snapshot.Commands.TryGetValue(command.Command, out CapabilityCommand? capability))
        {
            return IntentCapabilitiesDecision.Fail(
                "Command is not present in capabilities.list: " + command.Command);
        }

        if (capability.Surface != "public")
        {
            return IntentCapabilitiesDecision.Fail(
                "Intent providers may only use public commands from capabilities.list.");
        }

        if (capability.Status != "implemented")
        {
            return IntentCapabilitiesDecision.Fail(
                "Command is not implemented according to capabilities.list.");
        }

        if (capability.Capability != command.Capability)
        {
            return IntentCapabilitiesDecision.Fail(
                "Command capability does not match capabilities.list.");
        }

        if (capability.IsMutation != command.MutatesProject)
        {
            return IntentCapabilitiesDecision.Fail(
                "Command mutation metadata does not match capabilities.list.");
        }

        if (!capability.PermissionAllowed)
        {
            return IntentCapabilitiesDecision.Fail(
                "Command is not allowed by the effective policy: " +
                capability.PermissionReason);
        }

        if (command.MutatesProject && !capability.RequiresConfirmation)
        {
            return IntentCapabilitiesDecision.Fail(
                "Mutating IA commands require confirm_actions according to capabilities.list.");
        }

        if (command.RequiresConfirmation != capability.RequiresConfirmation)
        {
            return IntentCapabilitiesDecision.Fail(
                "Command confirmation metadata does not match capabilities.list.");
        }

        return IntentCapabilitiesDecision.Ok();
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetBoolean(JsonElement root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetPermissionAllowed(JsonElement command, out bool allowed)
    {
        allowed = false;
        if (!command.TryGetProperty("permission", out JsonElement permission) ||
            permission.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return TryGetBoolean(permission, "allowed", out allowed);
    }

    private static string TryGetPermissionReason(JsonElement command)
    {
        if (!command.TryGetProperty("permission", out JsonElement permission) ||
            permission.ValueKind != JsonValueKind.Object ||
            !TryGetString(permission, "reason", out string reason))
        {
            return "Permission decision is missing.";
        }

        return reason;
    }
}

internal sealed record IntentCapabilitiesSnapshot(
    IReadOnlyDictionary<string, CapabilityCommand> Commands);

internal sealed record CapabilityCommand(
    string Name,
    string Surface,
    string Status,
    string Capability,
    bool IsMutation,
    bool RequiresConfirmation,
    bool PermissionAllowed,
    string PermissionReason);

internal sealed record IntentCapabilitiesResult(
    bool Success,
    string Message,
    IntentCapabilitiesSnapshot? Snapshot)
{
    public static IntentCapabilitiesResult Ok(IntentCapabilitiesSnapshot snapshot)
    {
        return new IntentCapabilitiesResult(true, "OK", snapshot);
    }

    public static IntentCapabilitiesResult Fail(string message)
    {
        return new IntentCapabilitiesResult(false, message, null);
    }
}

internal sealed record IntentCapabilitiesDecision(bool Success, string Message)
{
    public static IntentCapabilitiesDecision Ok()
    {
        return new IntentCapabilitiesDecision(true, "OK");
    }

    public static IntentCapabilitiesDecision Fail(string message)
    {
        return new IntentCapabilitiesDecision(false, message);
    }
}

internal sealed class IntentCommandGuard
{
    private static readonly IReadOnlyDictionary<string, PublicCommandMetadata> PublicCommands =
        new Dictionary<string, PublicCommandMetadata>(StringComparer.Ordinal)
        {
            ["context.snapshot"] = new("context.read", false),
            ["capabilities.list"] = new("capabilities.read", false),
            ["validate.active_scene"] = new("validation.scene.run", false),
            ["authoring.create_gameobject"] = new("scene.gameobject.create", true),
            ["authoring.add_component"] = new("scene.component.add", true),
            ["authoring.set_component_field"] = new("scene.component.write", true),
            ["authoring.save_scene"] = new("scene.save", true)
        };

    public IntentCommandGuardResult TryPlan(string commandJson)
    {
        if (string.IsNullOrWhiteSpace(commandJson))
        {
            return IntentCommandGuardResult.Fail(null, "Command JSON is required.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(commandJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return IntentCommandGuardResult.Fail(null, "Command JSON root must be an object.");
            }

            if (!TryGetString(root, "protocolVersion", out string protocolVersion) ||
                protocolVersion != "0.1")
            {
                return IntentCommandGuardResult.Fail(
                    null,
                    "Only protocolVersion 0.1 command envelopes are allowed.");
            }

            if (!TryGetString(root, "commandId", out string commandId) ||
                !Guid.TryParse(commandId, out _))
            {
                return IntentCommandGuardResult.Fail(null, "A valid commandId is required.");
            }

            if (!TryGetString(root, "command", out string command))
            {
                return IntentCommandGuardResult.Fail(null, "Command name is required.");
            }

            if (!PublicCommands.TryGetValue(command, out PublicCommandMetadata? metadata))
            {
                return IntentCommandGuardResult.Fail(
                    command,
                    "Intent providers may only emit documented public commands.");
            }

            if (!root.TryGetProperty("arguments", out JsonElement arguments) ||
                arguments.ValueKind != JsonValueKind.Object)
            {
                return IntentCommandGuardResult.Fail(command, "Command arguments must be an object.");
            }

            if (!root.TryGetProperty("options", out JsonElement options) ||
                options.ValueKind != JsonValueKind.Object)
            {
                return IntentCommandGuardResult.Fail(command, "Command options must be an object.");
            }

            return IntentCommandGuardResult.Ok(new PlannedCommand(
                commandId,
                command,
                metadata.Capability,
                metadata.MutatesProject,
                metadata.MutatesProject,
                commandJson));
        }
        catch (JsonException exception)
        {
            return IntentCommandGuardResult.Fail(null, "Invalid command JSON: " + exception.Message);
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record PublicCommandMetadata(string Capability, bool MutatesProject);
}

internal sealed record IntentCommandGuardResult(
    bool Success,
    string? Command,
    string Message,
    PlannedCommand? CommandPlan)
{
    public static IntentCommandGuardResult Ok(PlannedCommand command)
    {
        return new IntentCommandGuardResult(true, command.Command, "OK", command);
    }

    public static IntentCommandGuardResult Fail(string? command, string message)
    {
        return new IntentCommandGuardResult(false, command, message, null);
    }
}
