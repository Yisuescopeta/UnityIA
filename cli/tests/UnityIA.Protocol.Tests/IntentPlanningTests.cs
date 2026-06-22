using System.Text.Json;
using UnityIA.Cli;
using Xunit;

namespace UnityIA.Protocol.Tests;

public sealed class IntentPlanningTests
{
    [Fact]
    public async Task PlannerAcceptsPublicReadCommandsAndTracesPromptHash()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(
            [
                CommandJson("context.snapshot")
            ],
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-a",
                "show me the current Unity context",
                CapabilitiesJson()));

        Assert.True(result.Success);
        Assert.Equal("OK", result.Code);
        PlannedCommand command = Assert.Single(result.Commands);
        Assert.Equal("context.snapshot", command.Command);
        Assert.Equal("context.read", command.Capability);
        Assert.False(command.MutatesProject);
        Assert.False(command.RequiresConfirmation);
        IntentTraceRecord record = Assert.Single(trace.Records);
        Assert.Equal("intent-a", record.RequestId);
        Assert.NotEqual("show me the current Unity context", record.PromptSha256);
        Assert.Equal(64, record.PromptSha256.Length);
        Assert.Equal(["context.snapshot"], record.PlannedCommands);
    }

    [Fact]
    public async Task PlannerMarksAuthoringMutationsAsRequiringConfirmation()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(
            [
                CommandJson(
                    "authoring.create_gameobject",
                    new { scenePath = "Assets/Scenes/Main.unity", name = "Player" })
            ],
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-b",
                "create a player object",
                CapabilitiesJson()));

        Assert.True(result.Success);
        PlannedCommand command = Assert.Single(result.Commands);
        Assert.Equal("authoring.create_gameobject", command.Command);
        Assert.Equal("scene.gameobject.create", command.Capability);
        Assert.True(command.MutatesProject);
        Assert.True(command.RequiresConfirmation);
    }

    [Fact]
    public async Task PlannerRejectsTechnicalCommandsFromProvider()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(
            [
                CommandJson("system.status")
            ],
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-c",
                "check status using internals",
                CapabilitiesJson()));

        Assert.False(result.Success);
        Assert.Equal("COMMAND_NOT_ALLOWED", result.Code);
        Assert.Empty(result.Commands);
        IntentTraceRecord record = Assert.Single(trace.Records);
        Assert.Equal(["system.status"], record.RejectedCommands);
    }

    [Fact]
    public async Task PlannerRejectsMalformedCommandEnvelope()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(["{\"command\":\"context.snapshot\""], trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-d",
                "show me context",
                CapabilitiesJson()));

        Assert.False(result.Success);
        Assert.Equal("COMMAND_NOT_ALLOWED", result.Code);
        Assert.Empty(result.Commands);
        Assert.Equal(["<unknown>"], Assert.Single(trace.Records).RejectedCommands);
    }

    [Fact]
    public async Task PlannerRejectsEmptyPromptBeforeCallingProvider()
    {
        RecordingTraceSink trace = new();
        TrackingProvider provider = new([]);
        IntentPlanningService service = new(
            provider,
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest("intent-e", " ", CapabilitiesJson()));

        Assert.False(result.Success);
        Assert.Equal("INVALID_REQUEST", result.Code);
        Assert.False(provider.WasCalled);
        IntentTraceRecord record = Assert.Single(trace.Records);
        Assert.Equal(string.Empty, record.PromptSha256);
    }

    [Fact]
    public async Task PlannerRejectsPlanWithoutSuccessfulCapabilities()
    {
        RecordingTraceSink trace = new();
        TrackingProvider provider = new([CommandJson("context.snapshot")]);
        IntentPlanningService service = new(
            provider,
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest("intent-f", "show context", string.Empty));

        Assert.False(result.Success);
        Assert.Equal("CAPABILITIES_REQUIRED", result.Code);
        Assert.Empty(result.Commands);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task PlannerRejectsCommandDeniedByEffectivePolicy()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(
            [
                CommandJson(
                    "authoring.create_gameobject",
                    new { scenePath = "Assets/Scenes/Main.unity", name = "Player" })
            ],
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-g",
                "create a player",
                CapabilitiesJson("authoring.create_gameobject", allowed: false)));

        Assert.False(result.Success);
        Assert.Equal("CAPABILITY_NOT_ALLOWED", result.Code);
        Assert.Empty(result.Commands);
        Assert.Equal(["authoring.create_gameobject"], Assert.Single(trace.Records).RejectedCommands);
    }

    [Fact]
    public async Task PlannerRejectsMutationWithoutConfirmActionsCapabilityMetadata()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = Service(
            [
                CommandJson(
                    "authoring.create_gameobject",
                    new { scenePath = "Assets/Scenes/Main.unity", name = "Player" })
            ],
            trace);

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-h",
                "create a player",
                CapabilitiesJson(
                    "authoring.create_gameobject",
                    requiresConfirmation: false)));

        Assert.False(result.Success);
        Assert.Equal("CAPABILITY_NOT_ALLOWED", result.Code);
        Assert.Empty(result.Commands);
        Assert.Equal(["authoring.create_gameobject"], Assert.Single(trace.Records).RejectedCommands);
    }

    [Fact]
    public async Task StructuredProviderMapsReadContextIntent()
    {
        StructuredIntentCommandProvider provider = FixedStructuredProvider();

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest(
                "intent-i",
                JsonSerializer.Serialize(new
                {
                    intent = "read_context",
                    arguments = new { includeHierarchy = true }
                }),
                CapabilitiesJson()),
            CancellationToken.None);

        Assert.True(response.Success);
        string commandJson = Assert.Single(response.CommandJson);
        using JsonDocument command = JsonDocument.Parse(commandJson);
        Assert.Equal("context.snapshot", command.RootElement.GetProperty("command").GetString());
        Assert.True(
            command.RootElement
                .GetProperty("arguments")
                .GetProperty("includeHierarchy")
                .GetBoolean());
        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            command.RootElement.GetProperty("commandId").GetString());
    }

    [Fact]
    public async Task PlannerAcceptsStructuredCreateGameObjectIntent()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = new(
            FixedStructuredProvider(),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-j",
                JsonSerializer.Serialize(new
                {
                    intent = "create_gameobject",
                    arguments = new
                    {
                        scenePath = "Assets/Scenes/Main.unity",
                        name = "Player",
                        position = new { x = 1, y = 2, z = 3 }
                    },
                    preconditions = new
                    {
                        sessionId = "session-a",
                        editorMode = "edit",
                        activeScenePath = "Assets/Scenes/Main.unity",
                        contextVersion = 7
                    }
                }),
                CapabilitiesJson()));

        Assert.True(result.Success);
        PlannedCommand command = Assert.Single(result.Commands);
        Assert.Equal("authoring.create_gameobject", command.Command);
        Assert.True(command.RequiresConfirmation);
        using JsonDocument commandJson = JsonDocument.Parse(command.Json);
        Assert.Equal(
            7,
            commandJson.RootElement
                .GetProperty("preconditions")
                .GetProperty("contextVersion")
                .GetInt32());
        Assert.Equal(["authoring.create_gameobject"], Assert.Single(trace.Records).PlannedCommands);
    }

    [Fact]
    public async Task PlannerRejectsUnsupportedStructuredIntent()
    {
        RecordingTraceSink trace = new();
        IntentPlanningService service = new(
            FixedStructuredProvider(),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));

        IntentPlanningResult result = await service.PlanAsync(
            new IntentPlanningRequest(
                "intent-k",
                JsonSerializer.Serialize(new
                {
                    intent = "generate_csharp",
                    arguments = new { script = "anything" }
                }),
                CapabilitiesJson()));

        Assert.False(result.Success);
        Assert.Equal("INTENT_NOT_SUPPORTED", result.Code);
        Assert.Empty(result.Commands);
        IntentTraceRecord record = Assert.Single(trace.Records);
        Assert.Equal("INTENT_NOT_SUPPORTED", record.Code);
        Assert.Empty(record.PlannedCommands);
    }

    [Fact]
    public async Task StructuredProviderRejectsUnsafeScenePath()
    {
        StructuredIntentCommandProvider provider = FixedStructuredProvider();

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest(
                "intent-l",
                JsonSerializer.Serialize(new
                {
                    intent = "validate_active_scene",
                    arguments = new { scenePath = "../ProjectSettings/Main.unity" }
                }),
                CapabilitiesJson()),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("INVALID_INTENT", response.Code);
        Assert.Empty(response.CommandJson);
    }

    private static IntentPlanningService Service(
        IReadOnlyList<string> commandJson,
        RecordingTraceSink trace)
    {
        return new IntentPlanningService(
            new TrackingProvider(commandJson),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));
    }

    private static StructuredIntentCommandProvider FixedStructuredProvider()
    {
        return new StructuredIntentCommandProvider(
            () => Guid.Parse("11111111-1111-1111-1111-111111111111"),
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));
    }

    private static string CapabilitiesJson(
        string? overrideCommand = null,
        bool allowed = true,
        bool? requiresConfirmation = null)
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
            bool effectiveAllowed = name == overrideCommand ? allowed : true;
            bool effectiveRequiresConfirmation = name == overrideCommand &&
                requiresConfirmation.HasValue
                    ? requiresConfirmation.Value
                    : isMutation;
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
                requiresConfirmation = effectiveRequiresConfirmation,
                permission = new
                {
                    allowed = effectiveAllowed,
                    capability,
                    pathAccess,
                    authorizationMode = "confirm_actions",
                    requiresConfirmation = effectiveAllowed && effectiveRequiresConfirmation,
                    reason = effectiveAllowed
                        ? "Capability is allowed by the effective policy."
                        : "Capability is not allowed by the effective policy."
                },
                restrictions = Array.Empty<string>()
            };
        }
    }

    private static string CommandJson(string command, object? arguments = null)
    {
        return JsonSerializer.Serialize(new
        {
            protocolVersion = "0.1",
            commandId = Guid.NewGuid().ToString("D"),
            command,
            issuedAtUtc = "2026-06-21T12:00:00Z",
            arguments = arguments ?? new { },
            options = new { dryRun = false }
        });
    }

    private sealed class TrackingProvider : IIntentCommandProvider
    {
        private readonly IReadOnlyList<string> commandJson;

        public TrackingProvider(IReadOnlyList<string> commandJson)
        {
            this.commandJson = commandJson;
        }

        public bool WasCalled { get; private set; }

        public Task<IntentProviderResponse> ProposeCommandsAsync(
            IntentPlanningRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new IntentProviderResponse(commandJson, []));
        }
    }

    private sealed class RecordingTraceSink : IIntentTraceSink
    {
        public List<IntentTraceRecord> Records { get; } = [];

        public void Record(IntentTraceRecord record)
        {
            Records.Add(record);
        }
    }
}
