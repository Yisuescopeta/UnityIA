using System;
using Newtonsoft.Json;

namespace UnityIA.Transport
{
    internal sealed class LiveSessionDescriptor
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        [JsonProperty("processId")]
        public int ProcessId { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("startedAtUtc")]
        public DateTimeOffset StartedAtUtc { get; set; }
    }
}

