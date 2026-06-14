using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityIA.Protocol;

public sealed class ActionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }
}

public sealed class LiveSessionDescriptor
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; init; } = string.Empty;

    [JsonPropertyName("processId")]
    public int ProcessId { get; init; }

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; init; }
}

public sealed record SchemaValidationError(string Path, string Kind);

public sealed record SchemaValidationResult(
    bool IsValid,
    string? Command,
    IReadOnlyList<SchemaValidationError> Errors);

