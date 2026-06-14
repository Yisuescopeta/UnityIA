using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityIA.Contracts
{
    public sealed class StateValidationRequest
    {
        [JsonProperty("preconditions")]
        public CommandPreconditions Preconditions { get; set; }

        [JsonProperty("isMutation")]
        public bool IsMutation { get; set; }
    }

    public sealed class PermissionRequest
    {
        [JsonProperty("capability", Required = Required.Always)]
        public string Capability { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }
    }

    public sealed class PermissionDecision
    {
        [JsonProperty("allowed")]
        public bool Allowed { get; set; }

        [JsonProperty("capability")]
        public string Capability { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    public sealed class EffectivePolicy
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("allowedCapabilities")]
        public List<string> AllowedCapabilities { get; set; } = new List<string>();

        [JsonProperty("readPaths")]
        public List<string> ReadPaths { get; set; } = new List<string>();

        [JsonProperty("writePaths")]
        public List<string> WritePaths { get; set; } = new List<string>();
    }

    public sealed class TestRunRequest
    {
        [JsonProperty("suite", Required = Required.Always)]
        public string Suite { get; set; }
    }

    public sealed class TestRunQuery
    {
        [JsonProperty("runId", Required = Required.Always)]
        public string RunId { get; set; }
    }
}

