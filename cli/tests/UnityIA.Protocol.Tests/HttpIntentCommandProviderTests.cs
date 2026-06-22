using System.Net;
using System.Text;
using System.Text.Json;
using UnityIA.Cli;
using Xunit;

namespace UnityIA.Protocol.Tests;

public sealed class HttpIntentCommandProviderTests
{
    [Fact]
    public async Task HttpProviderMapsStructuredIntentResponseToPublicCommand()
    {
        RecordingHandler handler = new(_ => JsonResponse(
            """
            {
              "intent": {
                "intent": "read_context",
                "arguments": { "includeHierarchy": true }
              },
              "warnings": ["provider warning"]
            }
            """));
        HttpIntentCommandProvider provider = Provider(handler);

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest(
                "intent-http-a",
                "show current context",
                "{}"),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(["provider warning"], response.Warnings);
        string commandJson = Assert.Single(response.CommandJson);
        using JsonDocument command = JsonDocument.Parse(commandJson);
        Assert.Equal("context.snapshot", command.RootElement.GetProperty("command").GetString());
        using JsonDocument request = JsonDocument.Parse(handler.LastContent!);
        Assert.Equal("intent-http-a", request.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("show current context", request.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(64, request.RootElement.GetProperty("promptSha256").GetString()!.Length);
        Assert.Contains(
            "create_gameobject",
            request.RootElement
                .GetProperty("supportedIntents")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task PlannerCanUseHttpProviderThroughSameGuards()
    {
        RecordingHandler handler = new(_ => JsonResponse(
            """
            {
              "intent": {
                "intent": "validate_active_scene",
                "arguments": { "scenePath": "Assets/Scenes/Main.unity" }
              }
            }
            """));
        RecordingTraceSink trace = new();
        IntentPlanningService planner = new(
            Provider(handler),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));

        IntentPlanningResult result = await planner.PlanAsync(
            new IntentPlanningRequest(
                "intent-http-b",
                "validate my scene",
                CapabilitiesJson()));

        Assert.True(result.Success);
        Assert.Equal("validate.active_scene", Assert.Single(result.Commands).Command);
        Assert.Equal(["validate.active_scene"], Assert.Single(trace.Records).PlannedCommands);
    }

    [Fact]
    public async Task HttpProviderRejectsCommandJsonInjectionResponse()
    {
        RecordingHandler handler = new(_ => JsonResponse(
            """
            {
              "commandJson": ["{}"]
            }
            """));
        HttpIntentCommandProvider provider = Provider(handler);

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest("intent-http-c", "try direct command", "{}"),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("INVALID_PROVIDER_RESPONSE", response.Code);
        Assert.Empty(response.CommandJson);
    }

    [Fact]
    public async Task HttpProviderRejectsUnsupportedIntentReturnedByEndpoint()
    {
        RecordingHandler handler = new(_ => JsonResponse(
            """
            {
              "intent": {
                "intent": "generate_csharp",
                "arguments": { "script": "public class Escape {}" }
              }
            }
            """));
        HttpIntentCommandProvider provider = Provider(handler);

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest("intent-http-d", "write script", "{}"),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("INTENT_NOT_SUPPORTED", response.Code);
        Assert.Empty(response.CommandJson);
    }

    [Fact]
    public async Task HttpProviderRejectsPlainHttpUnlessLoopbackAllowed()
    {
        RecordingHandler handler = new(_ => JsonResponse("{}"));
        HttpIntentCommandProvider provider = Provider(
            handler,
            new Uri("http://provider.example/intent"),
            allowInsecureLoopback: false);

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest("intent-http-e", "show context", "{}"),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("INVALID_PROVIDER_CONFIG", response.Code);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task HttpProviderSendsBearerTokenWithoutEchoingItOnFailure()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("secret-token", Encoding.UTF8, "text/plain")
        });
        HttpIntentCommandProvider provider = Provider(
            handler,
            new Uri("https://provider.example/intent"),
            bearerToken: "secret-token");

        IntentProviderResponse response = await provider.ProposeCommandsAsync(
            new IntentPlanningRequest("intent-http-f", "show context", "{}"),
            CancellationToken.None);

        Assert.True(handler.WasCalled);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
        Assert.Equal("secret-token", handler.LastAuthorizationParameter);
        Assert.False(response.Success);
        Assert.Equal("PROVIDER_TRANSPORT_ERROR", response.Code);
        Assert.DoesNotContain("secret-token", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserPromptBaselineEvaluatesHttpProviderWithNaturalPrompts()
    {
        Queue<string> responses = new(
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
            ]);
        RecordingHandler handler = new(_ => JsonResponse(responses.Dequeue()));
        RecordingTraceSink trace = new();
        IntentPlanningService planner = new(
            Provider(handler),
            new IntentCommandGuard(),
            new IntentCapabilitiesGuard(),
            trace,
            () => DateTimeOffset.Parse("2026-06-21T12:00:00Z"));
        IntentEvaluationService evaluation = new(planner);

        IntentEvaluationReport report = await evaluation.RunAsync(
            IntentEvaluationCatalog.V07UserPromptBaseline(CapabilitiesJson()));

        Assert.True(report.Success);
        Assert.Equal(6, report.Total);
        Assert.Equal(6, report.Passed);
        Assert.Equal(3, report.SecurityTotal);
        Assert.Equal(3, report.SecurityPassed);
        Assert.Equal(6, handler.Contents.Count);
        Assert.All(handler.Contents, content =>
        {
            using JsonDocument request = JsonDocument.Parse(content);
            string prompt = request.RootElement.GetProperty("prompt").GetString()!;
            Assert.False(prompt.TrimStart().StartsWith("{", StringComparison.Ordinal));
        });
        Assert.Contains(report.Results, result =>
            result.Name == "reject unsafe scene path user prompt" &&
            result.ActualCode == "INVALID_INTENT");
        Assert.All(trace.Records, record => Assert.NotEqual(string.Empty, record.PromptSha256));
    }

    private static HttpIntentCommandProvider Provider(
        RecordingHandler handler,
        Uri? endpoint = null,
        string? bearerToken = null,
        bool allowInsecureLoopback = false)
    {
        return new HttpIntentCommandProvider(
            new HttpClient(handler),
            new HttpIntentProviderOptions(
                endpoint ?? new Uri("https://provider.example/intent"),
                bearerToken,
                allowInsecureLoopback),
            new StructuredIntentCommandProvider(
                () => Guid.Parse("11111111-1111-1111-1111-111111111111"),
                () => DateTimeOffset.Parse("2026-06-21T12:00:00Z")));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public bool WasCalled { get; private set; }

        public string? LastContent { get; private set; }

        public List<string> Contents { get; } = [];

        public string? LastAuthorizationScheme { get; private set; }

        public string? LastAuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (LastContent is not null)
            {
                Contents.Add(LastContent);
            }

            return respond(request);
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
