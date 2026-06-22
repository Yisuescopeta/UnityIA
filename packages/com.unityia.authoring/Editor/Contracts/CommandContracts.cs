using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityIA.Contracts
{
    public static class CommandSurfaces
    {
        public const string Public = "public";
        public const string Technical = "technical";
    }

    public static class CommandStatuses
    {
        public const string Implemented = "implemented";
        public const string Reserved = "reserved";
    }

    public static class CommandPathAccess
    {
        public const string None = "none";
        public const string Read = "read";
        public const string Write = "write";
    }

    public static class CommandExecutionModes
    {
        public const string Live = "live";
        public const string Batch = "batch";
    }

    public static class AuthorizationModes
    {
        public const string ConfirmActions = "confirm_actions";
        public const string FullAccess = "full_access";
    }

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

        [JsonProperty("surface")]
        public string Surface { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("isMutation")]
        public bool IsMutation { get; set; }

        [JsonProperty("capability")]
        public string Capability { get; set; }

        [JsonProperty("pathAccess")]
        public string PathAccess { get; set; }

        [JsonProperty("modes")]
        public string[] Modes { get; set; }

        [JsonProperty("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; }

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

        public static string ComputeCanonicalHash(CommandEnvelope envelope)
        {
            if (envelope == null)
            {
                return string.Empty;
            }

            JsonSerializer serializer = JsonSerializer.Create(StrictSettings);
            CommandEnvelope normalized = new CommandEnvelope
            {
                ProtocolVersion = envelope.ProtocolVersion,
                CommandId = envelope.CommandId,
                Command = envelope.Command,
                IssuedAtUtc = envelope.IssuedAtUtc,
                Preconditions = envelope.Preconditions,
                Arguments = envelope.Arguments ?? new JObject(),
                Options = envelope.Options ?? new CommandOptions()
            };

            JToken token = JToken.FromObject(normalized, serializer);
            string canonicalJson = Canonicalize(token).ToString(Formatting.None);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(canonicalJson);
                byte[] hash = sha256.ComputeHash(bytes);
                return string.Concat(hash.Select(value => value.ToString("x2")));
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token == null)
            {
                return JValue.CreateNull();
            }

            if (token.Type == JTokenType.Object)
            {
                JObject source = (JObject)token;
                JObject canonical = new JObject();
                foreach (JProperty property in source.Properties()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    canonical.Add(property.Name, Canonicalize(property.Value));
                }

                return canonical;
            }

            if (token.Type == JTokenType.Array)
            {
                JArray source = (JArray)token;
                JArray canonical = new JArray();
                foreach (JToken item in source)
                {
                    canonical.Add(Canonicalize(item));
                }

                return canonical;
            }

            return token.DeepClone();
        }
    }
}
