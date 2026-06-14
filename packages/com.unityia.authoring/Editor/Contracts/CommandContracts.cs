using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityIA.Contracts
{
    public sealed class CommandEnvelope
    {
        [JsonProperty("protocolVersion", Required = Required.Always)]
        public string ProtocolVersion { get; set; }

        [JsonProperty("commandId", Required = Required.Always)]
        public string CommandId { get; set; }

        [JsonProperty("command", Required = Required.Always)]
        public string Command { get; set; }

        [JsonProperty("issuedAtUtc", Required = Required.Always)]
        public DateTimeOffset IssuedAtUtc { get; set; }

        [JsonProperty("preconditions", NullValueHandling = NullValueHandling.Include)]
        public CommandPreconditions Preconditions { get; set; }

        [JsonProperty("arguments", Required = Required.Always)]
        public JObject Arguments { get; set; } = new JObject();

        [JsonProperty("options", Required = Required.Always)]
        public CommandOptions Options { get; set; } = new CommandOptions();
    }

    public sealed class CommandPreconditions
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("editorMode")]
        public string EditorMode { get; set; }

        [JsonProperty("activeScenePath")]
        public string ActiveScenePath { get; set; }

        [JsonProperty("contextVersion")]
        public long? ContextVersion { get; set; }
    }

    public sealed class CommandOptions
    {
        [JsonProperty("dryRun")]
        public bool DryRun { get; set; }
    }

    public sealed class CommandDescriptorDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("isMutation")]
        public bool IsMutation { get; set; }

        [JsonProperty("capability")]
        public string Capability { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public static class CommandJson
    {
        public static readonly JsonSerializerSettings StrictSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.DateTimeOffset
        };

        public static bool TryDeserialize(
            string json,
            out CommandEnvelope envelope,
            out string error)
        {
            envelope = null;
            error = null;

            try
            {
                envelope = JsonConvert.DeserializeObject<CommandEnvelope>(json, StrictSettings);
                if (envelope == null)
                {
                    error = "The command body is empty.";
                    return false;
                }

                envelope.Arguments = envelope.Arguments ?? new JObject();
                envelope.Options = envelope.Options ?? new CommandOptions();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryConvertArguments<TArguments>(
            JObject arguments,
            out TArguments value,
            out string error)
        {
            value = default(TArguments);
            error = null;

            try
            {
                JsonSerializer serializer = JsonSerializer.Create(StrictSettings);
                value = (arguments ?? new JObject()).ToObject<TArguments>(serializer);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static string Serialize<T>(T value, Formatting formatting = Formatting.None)
        {
            return JsonConvert.SerializeObject(value, formatting, StrictSettings);
        }
    }
}

