using System.Text.Json;

namespace UnityIA.Cli;

internal sealed class StructuredIntentCommandProvider : IIntentCommandProvider
{
    private readonly Func<Guid> newGuid;
    private readonly Func<DateTimeOffset> now;

    public StructuredIntentCommandProvider(
        Func<Guid>? newGuid = null,
        Func<DateTimeOffset>? now = null)
    {
        this.newGuid = newGuid ?? Guid.NewGuid;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<IntentProviderResponse> ProposeCommandsAsync(
        IntentPlanningRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseRequest(request.Prompt, out JsonElement root, out string error))
        {
            return Task.FromResult(Fail("INVALID_INTENT", error));
        }

        if (!TryGetString(root, "intent", out string intent))
        {
            return Task.FromResult(Fail("INVALID_INTENT", "intent is required."));
        }

        JsonElement arguments = root.TryGetProperty("arguments", out JsonElement args)
            ? args
            : default;
        if (arguments.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
        {
            return Task.FromResult(Fail("INVALID_INTENT", "arguments must be an object."));
        }

        IntentProviderResponse response = intent switch
        {
            "read_context" => BuildReadContext(arguments),
            "validate_active_scene" => BuildValidateActiveScene(arguments),
            "create_gameobject" => BuildCreateGameObject(root, arguments),
            _ => Fail("INTENT_NOT_SUPPORTED", "Unsupported structured intent: " + intent)
        };

        return Task.FromResult(response);
    }

    private IntentProviderResponse BuildReadContext(JsonElement arguments)
    {
        Dictionary<string, object?> commandArguments = [];
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in arguments.EnumerateObject())
            {
                if (property.Name != "includeHierarchy")
                {
                    return Fail(
                        "INVALID_INTENT",
                        "Unsupported read_context argument: " + property.Name);
                }

                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return Fail("INVALID_INTENT", "includeHierarchy must be a boolean.");
                }

                commandArguments["includeHierarchy"] = property.Value.GetBoolean();
            }
        }

        return SingleCommand("context.snapshot", commandArguments);
    }

    private IntentProviderResponse BuildValidateActiveScene(JsonElement arguments)
    {
        if (!TryGetScenePath(arguments, out string scenePath, out string error))
        {
            return Fail("INVALID_INTENT", error);
        }

        return SingleCommand(
            "validate.active_scene",
            new Dictionary<string, object?>
            {
                ["scenePath"] = scenePath
            });
    }

    private IntentProviderResponse BuildCreateGameObject(
        JsonElement root,
        JsonElement arguments)
    {
        if (!TryGetScenePath(arguments, out string scenePath, out string error))
        {
            return Fail("INVALID_INTENT", error);
        }

        if (!TryGetString(arguments, "name", out string name) ||
            name.Length > 200)
        {
            return Fail("INVALID_INTENT", "name is required and must be 200 characters or fewer.");
        }

        Dictionary<string, object?> commandArguments = new(StringComparer.Ordinal)
        {
            ["scenePath"] = scenePath,
            ["name"] = name
        };

        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            if (property.Name is "scenePath" or "name")
            {
                continue;
            }

            if (property.Name is "position" or "rotationEuler" or "scale")
            {
                if (!TryReadVector3(property.Value, out Dictionary<string, object?> vector))
                {
                    return Fail("INVALID_INTENT", property.Name + " must be a vector3 object.");
                }

                commandArguments[property.Name] = vector;
                continue;
            }

            return Fail("INVALID_INTENT", "Unsupported create_gameobject argument: " + property.Name);
        }

        if (!root.TryGetProperty("preconditions", out JsonElement preconditions) ||
            preconditions.ValueKind != JsonValueKind.Object)
        {
            return Fail(
                "INVALID_INTENT",
                "create_gameobject requires explicit edit-mode preconditions.");
        }

        if (!TryReadPreconditions(preconditions, out Dictionary<string, object?> commandPreconditions))
        {
            return Fail(
                "INVALID_INTENT",
                "preconditions must include sessionId, editorMode edit, activeScenePath, and contextVersion.");
        }

        return SingleCommand("authoring.create_gameobject", commandArguments, commandPreconditions);
    }

    private IntentProviderResponse SingleCommand(
        string command,
        Dictionary<string, object?> arguments,
        Dictionary<string, object?>? preconditions = null)
    {
        Dictionary<string, object?> envelope = new(StringComparer.Ordinal)
        {
            ["protocolVersion"] = "0.1",
            ["commandId"] = newGuid().ToString("D"),
            ["command"] = command,
            ["issuedAtUtc"] = now(),
            ["arguments"] = arguments,
            ["options"] = new Dictionary<string, object?>
            {
                ["dryRun"] = false
            }
        };

        if (preconditions is not null)
        {
            envelope["preconditions"] = preconditions;
        }

        return new IntentProviderResponse(
            [JsonSerializer.Serialize(envelope)],
            []);
    }

    private static IntentProviderResponse Fail(string code, string message)
    {
        return new IntentProviderResponse([], [], false, code, message);
    }

    private static bool TryParseRequest(
        string prompt,
        out JsonElement root,
        out string error)
    {
        root = default;
        error = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(prompt);
            JsonElement parsedRoot = document.RootElement;
            if (parsedRoot.ValueKind != JsonValueKind.Object)
            {
                error = "Structured intent must be a JSON object.";
                return false;
            }

            foreach (JsonProperty property in parsedRoot.EnumerateObject())
            {
                if (property.Name is not ("intent" or "arguments" or "preconditions"))
                {
                    error = "Unsupported structured intent property: " + property.Name;
                    return false;
                }
            }

            root = parsedRoot.Clone();
            return true;
        }
        catch (JsonException exception)
        {
            error = "Invalid structured intent JSON: " + exception.Message;
            return false;
        }
    }

    private static bool TryGetScenePath(
        JsonElement arguments,
        out string scenePath,
        out string error)
    {
        scenePath = string.Empty;
        error = string.Empty;
        if (!TryGetString(arguments, "scenePath", out scenePath))
        {
            error = "scenePath is required.";
            return false;
        }

        if (!IsSafeScenePath(scenePath))
        {
            error = "scenePath must be a normalized Assets/**/*.unity path.";
            return false;
        }

        return true;
    }

    private static bool IsSafeScenePath(string scenePath)
    {
        return scenePath.StartsWith("Assets/", StringComparison.Ordinal) &&
            scenePath.EndsWith(".unity", StringComparison.Ordinal) &&
            !scenePath.Contains("..", StringComparison.Ordinal) &&
            !scenePath.Contains('\\', StringComparison.Ordinal) &&
            !Path.IsPathRooted(scenePath);
    }

    private static bool TryReadPreconditions(
        JsonElement preconditions,
        out Dictionary<string, object?> commandPreconditions)
    {
        commandPreconditions = [];
        if (!TryGetString(preconditions, "sessionId", out string sessionId) ||
            !TryGetString(preconditions, "editorMode", out string editorMode) ||
            editorMode != "edit" ||
            !TryGetString(preconditions, "activeScenePath", out string activeScenePath) ||
            !preconditions.TryGetProperty("contextVersion", out JsonElement contextVersion) ||
            contextVersion.ValueKind != JsonValueKind.Number ||
            !contextVersion.TryGetInt32(out int contextVersionValue) ||
            contextVersionValue < 1)
        {
            return false;
        }

        commandPreconditions["sessionId"] = sessionId;
        commandPreconditions["editorMode"] = editorMode;
        commandPreconditions["activeScenePath"] = activeScenePath;
        commandPreconditions["contextVersion"] = contextVersionValue;
        return true;
    }

    private static bool TryReadVector3(
        JsonElement value,
        out Dictionary<string, object?> vector)
    {
        vector = [];
        if (value.ValueKind != JsonValueKind.Object ||
            !TryGetDouble(value, "x", out double x) ||
            !TryGetDouble(value, "y", out double y) ||
            !TryGetDouble(value, "z", out double z))
        {
            return false;
        }

        vector["x"] = x;
        vector["y"] = y;
        vector["z"] = z;
        return true;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDouble(JsonElement root, string propertyName, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return property.TryGetDouble(out value);
    }
}
