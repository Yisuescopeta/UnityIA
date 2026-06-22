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

    [Theory]
    [InlineData("capabilities.list.json")]
    [InlineData("validate.active_scene.json")]
    [InlineData("context.snapshot.json")]
    [InlineData("authoring.create_gameobject.json")]
    [InlineData("authoring.add_component.json")]
    [InlineData("authoring.set_component_field.json")]
    [InlineData("authoring.save_scene.json")]
    public async Task PublicCommandExamplesAreValid(string fileName)
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schemas", "examples", fileName));
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
    public async Task PublicSetFieldRejectsUnregisteredComponent()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "schemas",
                "examples",
                "authoring.set_component_field.json"));
        json = json.Replace(
            "\"componentType\": \"BoxCollider\"",
            "\"componentType\": \"Rigidbody\"",
            StringComparison.Ordinal);

        SchemaValidationResult result =
            await new JsonSchemaCommandValidator(Schemas).ValidateAsync(json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PublicSetFieldRejectsMismatchedValueType()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "schemas",
                "examples",
                "authoring.set_component_field.json"));
        json = json.Replace(
            "\"field\": \"isTrigger\",\n    \"value\": true",
            "\"field\": \"isTrigger\",\n    \"value\": { \"x\": 1, \"y\": 1, \"z\": 1 }",
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

    [Fact]
    public async Task PolicyExampleIsValid()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schemas", "common", "policy.schema.json"));
        NJsonSchema.JsonSchema schema =
            await NJsonSchema.JsonSchema.FromJsonAsync(json);
        const string policy = """
        {
          "version": "0.1",
          "authorizationMode": "confirm_actions",
          "allow": [
            "context.read",
            "capabilities.read",
            "validation.scene.run"
          ],
          "paths": {
            "read": ["Assets/**"],
            "write": ["Assets/Scenes/**"]
          }
        }
        """;

        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(policy);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task PolicyRejectsFullAccess()
    {
        string json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schemas", "common", "policy.schema.json"));
        NJsonSchema.JsonSchema schema =
            await NJsonSchema.JsonSchema.FromJsonAsync(json);
        string policy = """
        {
          "version": "0.1",
          "authorizationMode": "full_access",
          "allow": ["context.read"],
          "paths": {
            "read": ["Assets/**"],
            "write": []
          }
        }
        """;

        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(policy);

        Assert.NotEmpty(errors);
    }
}
