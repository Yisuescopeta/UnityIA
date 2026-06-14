using UnityIA.Protocol;
using Xunit;

namespace UnityIA.Protocol.Tests;

public sealed class SchemaTests
{
    private static string Schemas =>
        Path.Combine(AppContext.BaseDirectory, "schemas", "v0.1");

    [Fact]
    public async Task StatusExampleIsValid()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schemas", "examples", "system.status.json"));
        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Fact]
    public async Task UnknownEnvelopePropertyIsRejected()
    {
        const string json = """
        {
          "protocolVersion": "0.1",
          "commandId": "0edcf18a-c996-43f4-88d4-3c5b64f0099e",
          "command": "system.status",
          "issuedAtUtc": "2026-06-14T12:00:00Z",
          "arguments": {},
          "options": { "dryRun": false },
          "unexpected": true
        }
        """;

        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MutationExampleIsValid()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "schemas",
                "examples",
                "scene.object.set-transform.json"));
        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Fact]
    public async Task UnknownMutationArgumentIsRejected()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "schemas",
                "examples",
                "scene.object.set-transform.json"));
        json = json.Replace(
            "\"rotationEuler\": {",
            "\"unexpected\": true, \"rotationEuler\": {",
            StringComparison.Ordinal);

        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task UnknownCommandIsRejectedBeforePathConstruction()
    {
        const string json = """
        {
          "protocolVersion": "0.1",
          "commandId": "0edcf18a-c996-43f4-88d4-3c5b64f0099e",
          "command": "../../secret",
          "issuedAtUtc": "2026-06-14T12:00:00Z",
          "arguments": {},
          "options": { "dryRun": false }
        }
        """;

        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Kind == "UnsupportedCommand");
    }
}
