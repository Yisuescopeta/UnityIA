using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Context;
using UnityIA.Core;

namespace UnityIA.Authoring
{
    internal static class AuthoringValidation
    {
        public static ActionResult<JObject> ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "name must not be empty.");
            }

            if (name.Length > 200)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "name must be 200 characters or fewer.");
            }

            return Results.Ok("Name is valid.");
        }

        public static ActionResult<JObject> ValidateVector(Vector3Dto vector, string field)
        {
            if (vector == null)
            {
                return Results.Ok(field + " was not provided.");
            }

            if (!IsFinite(vector.X) || !IsFinite(vector.Y) || !IsFinite(vector.Z))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    field + " components must be finite numbers.");
            }

            return Results.Ok(field + " is valid.");
        }

        public static ActionResult<JObject> ValidateCreateGameObject(
            CreateGameObjectArguments arguments,
            CommandEnvelope envelope)
        {
            if (arguments == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "arguments are required.");
            }

            ActionResult<JObject> scene = ValidateActiveScenePath(arguments.ScenePath);
            if (!scene.Success)
            {
                return scene;
            }

            ActionResult<JObject> name = ValidateName(arguments.Name);
            if (!name.Success)
            {
                return name;
            }

            foreach (Tuple<Vector3Dto, string> item in new[]
                     {
                         Tuple.Create(arguments.Position, "position"),
                         Tuple.Create(arguments.RotationEuler, "rotationEuler"),
                         Tuple.Create(arguments.Scale, "scale")
                     })
            {
                ActionResult<JObject> vector = ValidateVector(item.Item1, item.Item2);
                if (!vector.Success)
                {
                    return vector;
                }
            }

            if (arguments.Parent == null)
            {
                return Results.Ok("Create GameObject arguments are valid.");
            }

            ObjectResolution parent = Resolve(arguments.Parent, envelope);
            if (!parent.Success)
            {
                return parent.Error;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(parent.GameObject))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.3 cannot add children inside a prefab instance.");
            }

            return Results.Ok("Create GameObject arguments are valid.");
        }

        public static ActionResult<JObject> ValidateAddComponent(
            AddComponentArguments arguments,
            CommandEnvelope envelope)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            ActionResult<JObject> component = ValidateComponentTypeForAdd(
                arguments.ComponentType);
            if (!component.Success)
            {
                return component;
            }

            ObjectResolution target = Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            if (IsInternalPrefabObject(target.GameObject))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.3 cannot add components to internal prefab instance objects.");
            }

            if (target.GameObject.GetComponent<BoxCollider>() != null)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "BoxCollider is already present on the target GameObject.");
            }

            return Results.Ok("Add component arguments are valid.");
        }

        public static ActionResult<JObject> ValidateSetComponentField(
            SetComponentFieldArguments arguments,
            CommandEnvelope envelope)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            ObjectResolution target = Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            if (IsInternalPrefabObject(target.GameObject))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.3 cannot modify internal prefab instance objects.");
            }

            ActionResult<JObject> component = ValidateComponentAndField(
                arguments.ComponentType,
                arguments.Field,
                arguments.Value);
            if (!component.Success)
            {
                return component;
            }

            if (string.Equals(arguments.ComponentType, "BoxCollider", StringComparison.Ordinal))
            {
                ComponentResolution boxCollider = ResolveSingleBoxCollider(target.GameObject);
                return boxCollider.Success
                    ? Results.Ok("Set component field arguments are valid.")
                    : boxCollider.Error;
            }

            return Results.Ok("Set component field arguments are valid.");
        }

        public static ActionResult<JObject> ValidateSaveScene(
            SaveSceneArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ScenePath))
            {
                return Results.Error(ResultCodes.ValidationFailed, "scenePath is required.");
            }

            return ValidateActiveScenePath(arguments.ScenePath);
        }

        public static ObjectResolution Resolve(
            ObjectReferenceDto reference,
            CommandEnvelope envelope)
        {
            string scenePath = envelope == null || envelope.Preconditions == null
                ? null
                : envelope.Preconditions.ActiveScenePath;
            return SceneObjectResolver.Resolve(reference, scenePath);
        }

        public static bool IsInternalPrefabObject(GameObject gameObject)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                return false;
            }

            return PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) != gameObject;
        }

        public static JObject ObjectData(GameObject gameObject)
        {
            return SceneObjectResolver.Describe(gameObject);
        }

        public static Vector3 ToVector(Vector3Dto value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        public static JObject VectorData(Vector3 vector)
        {
            return new JObject
            {
                ["x"] = vector.x,
                ["y"] = vector.y,
                ["z"] = vector.z
            };
        }

        public static bool TryReadVectorValue(
            JToken value,
            string field,
            out Vector3 vector,
            out ActionResult<JObject> error)
        {
            vector = default(Vector3);
            error = null;
            if (value == null || value.Type == JTokenType.Null ||
                value.Type == JTokenType.Undefined)
            {
                error = Results.Error(
                    ResultCodes.ValidationFailed,
                    field + " value is required.");
                return false;
            }

            if (value.Type != JTokenType.Object)
            {
                error = Results.Error(
                    ResultCodes.ValidationFailed,
                    field + " value must be a vector object.");
                return false;
            }

            try
            {
                JsonSerializer serializer = JsonSerializer.Create(CommandJson.StrictSettings);
                Vector3Dto dto = value.ToObject<Vector3Dto>(serializer);
                ActionResult<JObject> validation = ValidateVector(dto, field);
                if (!validation.Success)
                {
                    error = validation;
                    return false;
                }

                vector = ToVector(dto);
                return true;
            }
            catch (Exception exception)
            {
                error = Results.Error(
                    ResultCodes.ValidationFailed,
                    "Invalid vector value for " + field + ": " + exception.Message);
                return false;
            }
        }

        public static bool TryReadBooleanValue(
            JToken value,
            string field,
            out bool boolean,
            out ActionResult<JObject> error)
        {
            boolean = false;
            error = null;
            if (value == null || value.Type != JTokenType.Boolean)
            {
                error = Results.Error(
                    ResultCodes.ValidationFailed,
                    field + " value must be a boolean.");
                return false;
            }

            boolean = value.Value<bool>();
            return true;
        }

        public static ComponentResolution ResolveSingleBoxCollider(GameObject gameObject)
        {
            BoxCollider[] colliders = gameObject.GetComponents<BoxCollider>();
            if (colliders.Length == 0)
            {
                return ComponentResolution.Failed(
                    ResultCodes.TargetNotFound,
                    "The target GameObject does not have a BoxCollider.");
            }

            if (colliders.Length > 1)
            {
                return ComponentResolution.Failed(
                    ResultCodes.AmbiguousTarget,
                    "The target GameObject has multiple BoxCollider components.");
            }

            return ComponentResolution.Resolved(colliders[0]);
        }

        private static ActionResult<JObject> ValidateComponentTypeForAdd(
            string componentType)
        {
            return string.Equals(componentType, "BoxCollider", StringComparison.Ordinal)
                ? Results.Ok("Component type is valid.")
                : Results.Error(
                    ResultCodes.ValidationFailed,
                    "Only BoxCollider can be added by authoring.add_component in v0.3.");
        }

        private static ActionResult<JObject> ValidateComponentAndField(
            string componentType,
            string field,
            JToken value)
        {
            if (string.IsNullOrWhiteSpace(componentType))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "componentType is required.");
            }

            if (string.IsNullOrWhiteSpace(field))
            {
                return Results.Error(ResultCodes.ValidationFailed, "field is required.");
            }

            if (string.Equals(componentType, "Transform", StringComparison.Ordinal))
            {
                if (field == "localPosition" ||
                    field == "localEulerAngles" ||
                    field == "localScale")
                {
                    Vector3 ignored;
                    ActionResult<JObject> error;
                    return TryReadVectorValue(
                        value,
                        componentType + "." + field,
                        out ignored,
                        out error)
                        ? Results.Ok("Field value is valid.")
                        : error;
                }
            }

            if (string.Equals(componentType, "BoxCollider", StringComparison.Ordinal))
            {
                if (field == "center" || field == "size")
                {
                    Vector3 ignored;
                    ActionResult<JObject> error;
                    return TryReadVectorValue(
                        value,
                        componentType + "." + field,
                        out ignored,
                        out error)
                        ? Results.Ok("Field value is valid.")
                        : error;
                }

                if (field == "isTrigger")
                {
                    bool ignored;
                    ActionResult<JObject> error;
                    return TryReadBooleanValue(
                        value,
                        componentType + "." + field,
                        out ignored,
                        out error)
                        ? Results.Ok("Field value is valid.")
                        : error;
                }
            }

            return Results.Error(
                ResultCodes.ValidationFailed,
                "The requested component field is not registered.");
        }

        private static ActionResult<JObject> ValidateActiveScenePath(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return Results.Error(ResultCodes.ValidationFailed, "scenePath is required.");
            }

            string requested = ProjectPaths.NormalizeUnityPath(scenePath);
            string active = ProjectPaths.NormalizeUnityPath(SceneManager.GetActiveScene().path);
            if (!requested.StartsWith("Assets/", StringComparison.Ordinal) ||
                !requested.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Error(
                    ResultCodes.PathNotAllowed,
                    "scenePath must reference an existing Assets/*.unity scene.");
            }

            if (string.IsNullOrWhiteSpace(active))
            {
                return Results.Error(
                    ResultCodes.SceneNotPersisted,
                    "The active scene must be saved before public authoring commands run.");
            }

            return string.Equals(requested, active, StringComparison.Ordinal)
                ? Results.Ok("Scene target is valid.")
                : Results.Error(
                    ResultCodes.InvalidEditorState,
                    "Public authoring commands can target only the expected active scene.");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal sealed class ComponentResolution
    {
        private ComponentResolution(Component component, ActionResult<JObject> error)
        {
            Component = component;
            Error = error;
        }

        public Component Component { get; }
        public ActionResult<JObject> Error { get; }
        public bool Success => Component != null && Error == null;

        public static ComponentResolution Resolved(Component component)
        {
            return new ComponentResolution(component, null);
        }

        public static ComponentResolution Failed(string code, string message)
        {
            return new ComponentResolution(null, Results.Error(code, message));
        }
    }
}
