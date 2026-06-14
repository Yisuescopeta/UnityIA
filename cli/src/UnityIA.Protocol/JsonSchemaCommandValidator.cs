using System.Text.Json;
using NJsonSchema;

namespace UnityIA.Protocol;

public sealed class JsonSchemaCommandValidator
{
    private static readonly HashSet<string> SupportedCommands =
    [
        "system.status",
        "system.commands.list",
        "context.get",
        "scene.list-open",
        "scene.hierarchy.get",
        "scene.object.get",
        "scene.object.create-empty",
        "scene.object.rename",
        "scene.object.set-active",
        "scene.object.set-transform",
        "scene.object.reparent",
        "scene.object.delete",
        "scene.save",
        "validation.command.validate",
        "permissions.explain"
    ];

    private readonly string schemaDirectory;

    public JsonSchemaCommandValidator(string schemaDirectory)
    {
        this.schemaDirectory = Path.GetFullPath(schemaDirectory);
    }

    public async Task<SchemaValidationResult> ValidateAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        string? command;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("command", out JsonElement commandElement) ||
                commandElement.ValueKind != JsonValueKind.String)
            {
                return Invalid(null, "#/command", "PropertyRequired");
            }

            command = commandElement.GetString();
        }
        catch (JsonException exception)
        {
            return Invalid(null, "#", "InvalidJson: " + exception.Message);
        }

        if (command is null || !SupportedCommands.Contains(command))
        {
            return Invalid(command, "#/command", "UnsupportedCommand");
        }

        string schemaPath = Path.Combine(schemaDirectory, command + ".schema.json");
        if (!File.Exists(schemaPath))
        {
            return Invalid(command, "#", "SchemaNotFound");
        }

        cancellationToken.ThrowIfCancellationRequested();
        JsonSchema schema = await JsonSchema.FromFileAsync(schemaPath).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(json);
        return new SchemaValidationResult(
            errors.Count == 0,
            command,
            errors.Select(error => new SchemaValidationError(
                    string.IsNullOrWhiteSpace(error.Path) ? "#" : error.Path,
                    error.Kind.ToString()))
                .ToArray());
    }

    private static SchemaValidationResult Invalid(
        string? command,
        string path,
        string kind)
    {
        return new SchemaValidationResult(
            false,
            command,
            [new SchemaValidationError(path, kind)]);
    }
}

