using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityIA.Contracts
{
    public sealed class EmptyArguments
    {
    }

    public sealed class Vector3Dto
    {
        [JsonProperty("x", Required = Required.Always)]
        public float X { get; set; }

        [JsonProperty("y", Required = Required.Always)]
        public float Y { get; set; }

        [JsonProperty("z", Required = Required.Always)]
        public float Z { get; set; }
    }

    public sealed class ObjectReferenceDto
    {
        [JsonProperty("globalObjectId")]
        public string GlobalObjectId { get; set; }

        [JsonProperty("hierarchyPath")]
        public string HierarchyPath { get; set; }

        [JsonProperty("scenePath")]
        public string ScenePath { get; set; }
    }

    public sealed class ContextQuery
    {
        [JsonProperty("includeHierarchy")]
        public bool IncludeHierarchy { get; set; }
    }

    public sealed class HierarchyQuery
    {
        [JsonProperty("scenePath")]
        public string ScenePath { get; set; }
    }

    public sealed class SceneObjectQuery
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }
    }

    public sealed class CreateEmptyArguments
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("parent")]
        public ObjectReferenceDto Parent { get; set; }

        [JsonProperty("position")]
        public Vector3Dto Position { get; set; }

        [JsonProperty("rotationEuler")]
        public Vector3Dto RotationEuler { get; set; }

        [JsonProperty("scale")]
        public Vector3Dto Scale { get; set; }
    }

    public sealed class CreateGameObjectArguments
    {
        [JsonProperty("scenePath", Required = Required.Always)]
        public string ScenePath { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("parent")]
        public ObjectReferenceDto Parent { get; set; }

        [JsonProperty("position")]
        public Vector3Dto Position { get; set; }

        [JsonProperty("rotationEuler")]
        public Vector3Dto RotationEuler { get; set; }

        [JsonProperty("scale")]
        public Vector3Dto Scale { get; set; }
    }

    public sealed class AddComponentArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("componentType", Required = Required.Always)]
        public string ComponentType { get; set; }
    }

    public sealed class SetComponentFieldArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("componentType", Required = Required.Always)]
        public string ComponentType { get; set; }

        [JsonProperty("field", Required = Required.Always)]
        public string Field { get; set; }

        [JsonProperty("value", Required = Required.Always)]
        public JToken Value { get; set; }
    }

    public sealed class RenameArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }
    }

    public sealed class SetActiveArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("active", Required = Required.Always)]
        public bool Active { get; set; }
    }

    public sealed class SetTransformArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("position")]
        public Vector3Dto Position { get; set; }

        [JsonProperty("rotationEuler")]
        public Vector3Dto RotationEuler { get; set; }

        [JsonProperty("scale")]
        public Vector3Dto Scale { get; set; }
    }

    public sealed class ReparentArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }

        [JsonProperty("parent", Required = Required.AllowNull)]
        public ObjectReferenceDto Parent { get; set; }

        [JsonProperty("worldPositionStays")]
        public bool WorldPositionStays { get; set; } = true;
    }

    public sealed class DeleteArguments
    {
        [JsonProperty("target", Required = Required.Always)]
        public ObjectReferenceDto Target { get; set; }
    }

    public sealed class SaveSceneArguments
    {
        [JsonProperty("scenePath", Required = Required.Always)]
        public string ScenePath { get; set; }
    }

    public sealed class ValidateCommandArguments
    {
        [JsonProperty("envelope", Required = Required.Always)]
        public JObject Envelope { get; set; }
    }

    public sealed class ValidateActiveSceneArguments
    {
        [JsonProperty("scenePath", Required = Required.Always)]
        public string ScenePath { get; set; }
    }
}
