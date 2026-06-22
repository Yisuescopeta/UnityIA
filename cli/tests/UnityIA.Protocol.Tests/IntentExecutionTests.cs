using System.Text.Json;
using UnityIA.Cli;
using Xunit;

namespace UnityIA.Protocol.Tests;

public sealed class IntentExecutionTests
{
    [Fact]
    public async Task ExecutionRunsPlannedMutationThenPostValidationAndTracesResults()
    {
        RecordingTraceSink trace = new();
        TrackingExecutor executor = new(command =>
            command.Command == "validate.active_scene"
                ? Result(command, true, "OK", "Scene is valid.")
                : Result(command, true, "OK", "Command executed."));
        IntentExecutionService service = Service(trace, executor);

        IntentExecutionResult result = await service.ExecuteAsync(CreateGameObjectRequest());

        Assert.True(result.Success);
        Assert.Equal("OK", result.Code);
        Assert.Equal(["authoring.create_gameobject", "validate.active_scene"],
            executor.Executed.Select(command => command.Command).ToArray());
        Assert.Single(result.CommandResults);
        IntentValidationExecutionResult validation = Assert.Single(result.ValidationResults);
        Assert.Equal("Assets/Scenes/Main.unity", validation.ScenePath);
        Assert.Equal("OK", validation.Code);
        IntentTraceRecord executionTrace = trace.Records.Single(record => record.Stage == "execution");
        Assert.Equal("OK", executionTrace.Code);
        Assert.Equal(["authoring.create_gameobject"], executionTrace.PlannedCommands);
        Assert.Equal("OK", Assert.Single(executionTrace.CommandResults).Code);
        Assert.Equal("OK", Assert.Single(executionTrace.ValidationResults).Code);
    }

    [Fact]
    public async Task ExecutionStopsOnFirstCommandFailureAndSkipsValidation()
    {
        RecordingTraceSink trace = new();
        TrackingExecutor executor = new(command =>
            Result(command, false, "CONFIRMATION_REQUIRED", "Mutation needs approval."));
        IntentExecutionService service = Service(trace, executor);

        IntentExecutionResult result = await service.ExecuteAsync(CreateGameObjectRequest());

        Assert.False(result.Success);
        Assert.Equal("CONFIRMATION_REQUIRED", result.Code);
        Assert.Single(result.CommandResults);
        Assert.Empty(result.ValidationResults);
        Assert.Equal(["authoring.create_gameobject"],
            executor.Executed.Select(command => command.Command).ToArray());
        IntentTraceRecord executionTrace = trace.Records.Single(record => record.Stage == "execution");
        Assert.Equal("CONFIRMATION_REQUIRED", executionTrace.Code);
        Assert.Empty(executionTrace.ValidationResults);
    }

    [Fact]
    public async Task PostValidationFailureFailsExecutionResult()
    {
        RecordingTraceSink trace = new();
        TrackingExecutor executor = new(command =>
            command.Command == "validate.active_scene"
                ? Result(command, false, "VALIDATION_FAILED", "Scene validation failed.")
                : Result(command, true, "OK", "Command executed."));
        IntentExecutionService service = Service(trace, executor);

        IntentExecutionResult result = await service.ExecuteAsync(CreateGameObjectRequest());

        Assert.False(result.Success);
        Assert.Equal("POST_VALIDATION_FAILED", result.Code);
        Assert.Single(result.CommandResults);
        IntentValidationExecutionResult validation = Assert.Single(result.ValidationResults);
        Assert.Equal("VALIDATION_FAILED", validation.Code);
        IntentTraceRecord executionTrace = trace.Records.Single(record => record.Stage == "execution");
        Assert.Equal("POST_VALIDATION_FAILED", executionTrace.Code);
        Assert.Equal("VALIDATION_FAILED", Assert.Single(executionTrace.ValidationResults).Code);
    }

    [Fact]
    public async Task JsonExecutorConvertsInvalidActionResultToInvalidResponse()
    {
        PlannedCommand command = new(
            "11111111-1111-1111-1111-111111111111",
            "context.snapshot",
            "context.read",
            false,
            false,
            CommandJson("context.snapshot"));
        JsonPlannedCommandExecutor executor = new((_, _) => Task.FromResult("not json"));

        IntentCommandExecutionResult result =
            await executor.ExecuteAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_RESPONSE", result.Code);
        Assert.Equal("context.snapshot", result.Command);
    }

    private static IntentExecutionService Service(
        RecordingTraceSink trace,
        IPlannedCommandExecutor executor)
    {
        IntentPlanningService planner = new(
            new StructuredIntentCommandProvider(
                () => Guid.Parse("11111111-1111-1111-1111-111111111111"),
                () => DateTimeOffset.Parse("2026-06-21T12:00:00Z")),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));
        return new IntentExecutionService(
            planner,
            executor,
            trace,
            () => Guid.Parse("22222222-2222-2222-2222-222222222222"),
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));
    }

    private static IntentPlanningRequest CreateGameObjectRequest()
    {
        return new IntentPlanningRequest(
            "intent-exec",
            JsonSerializer.Serialize(new
            {
                intent = "create_gameobject",
                arguments = new
                {
                    scenePath = "Assets/Scenes/Main.unity",
                    name = "Player"
                },
                preconditions = new
                {
                    sessionId = "session-a",
                    editorMode = "edit",
                    activeScenePath = "Assets/Scenes/Main.unity",
                    contextVersion = 7
                }
            }),
            CapabilitiesJson());
    }

    private static IntentCommandExecutionResult Result(
        PlannedCommand command,
        bool success,
        string code,
        string message)
    {
        return new IntentCommandExecutionResult(
            command.CommandId,
            command.Command,
            success,
            code,
            message);
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

    private static string CommandJson(string command)
    {
        return JsonSerializer.Serialize(new
        {
            protocolVersion = "0.1",
            commandId = "11111111-1111-1111-1111-111111111111",
            command,
            issuedAtUtc = "2026-06-21T12:00:00Z",
            arguments = new { },
            options = new { dryRun = false }
        });
    }

    private sealed class TrackingExecutor : IPlannedCommandExecutor
    {
        private readonly Func<PlannedCommand, IntentCommandExecutionResult> execute;

        public TrackingExecutor(Func<PlannedCommand, IntentCommandExecutionResult> execute)
        {
            this.execute = execute;
        }

        public List<PlannedCommand> Executed { get; } = [];

        public Task<IntentCommandExecutionResult> ExecuteAsync(
            PlannedCommand command,
            CancellationToken cancellationToken)
        {
            Executed.Add(command);
            return Task.FromResult(execute(command));
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
