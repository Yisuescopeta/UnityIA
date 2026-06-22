using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Authoring;
using UnityIA.Contracts;
using UnityIA.Context;
using UnityIA.Core;

namespace UnityIA
{
    public static class UnityIAAuthoringAPI
    {
        public static ActionResult<JObject> Execute(CommandEnvelope envelope)
        {
            return CoreServices.Dispatcher.Execute(envelope);
        }

        public static ActionResult<JObject> ExecuteJson(string json)
        {
            return CoreServices.Dispatcher.ExecuteJson(json);
        }

        public static ActionResult<JObject> CreateGameObject(
            CreateGameObjectArguments arguments,
            CommandEnvelope envelope)
        {
            ActionResult<JObject> validation =
                AuthoringValidation.ValidateCreateGameObject(arguments, envelope);
            if (!validation.Success)
            {
                return validation;
            }

            Scene scene = SceneManager.GetActiveScene();
            GameObject gameObject = new GameObject(arguments.Name.Trim());
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);

            if (arguments.Parent != null)
            {
                ObjectResolution parent = AuthoringValidation.Resolve(
                    arguments.Parent,
                    envelope);
                if (!parent.Success)
                {
                    return parent.Error;
                }

                Undo.SetTransformParent(
                    gameObject.transform,
                    parent.GameObject.transform,
                    "Parent " + gameObject.name);
            }

            Undo.RegisterCompleteObjectUndo(gameObject.transform, "Set initial transform");
            if (arguments.Position != null)
            {
                gameObject.transform.localPosition =
                    AuthoringValidation.ToVector(arguments.Position);
            }

            if (arguments.RotationEuler != null)
            {
                gameObject.transform.localEulerAngles =
                    AuthoringValidation.ToVector(arguments.RotationEuler);
            }

            if (arguments.Scale != null)
            {
                gameObject.transform.localScale =
                    AuthoringValidation.ToVector(arguments.Scale);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return Results.Ok(
                "GameObject created.",
                AuthoringValidation.ObjectData(gameObject));
        }

        public static ActionResult<JObject> AddComponent(
            AddComponentArguments arguments,
            CommandEnvelope envelope)
        {
            ActionResult<JObject> validation =
                AuthoringValidation.ValidateAddComponent(arguments, envelope);
            if (!validation.Success)
            {
                return validation;
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            BoxCollider collider = Undo.AddComponent<BoxCollider>(target.GameObject);
            EditorSceneManager.MarkSceneDirty(target.GameObject.scene);
            return Results.Ok(
                "Component added.",
                new JObject
                {
                    ["gameObject"] = AuthoringValidation.ObjectData(target.GameObject),
                    ["component"] = ComponentData(collider)
                });
        }

        public static ActionResult<JObject> SetComponentField(
            SetComponentFieldArguments arguments,
            CommandEnvelope envelope)
        {
            ActionResult<JObject> validation =
                AuthoringValidation.ValidateSetComponentField(arguments, envelope);
            if (!validation.Success)
            {
                return validation;
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            if (arguments.ComponentType == "Transform")
            {
                return SetTransformField(target.GameObject, arguments);
            }

            if (arguments.ComponentType == "BoxCollider")
            {
                ComponentResolution component =
                    AuthoringValidation.ResolveSingleBoxCollider(target.GameObject);
                if (!component.Success)
                {
                    return component.Error;
                }

                return SetBoxColliderField(
                    target.GameObject,
                    (BoxCollider)component.Component,
                    arguments);
            }

            return Results.Error(
                ResultCodes.ValidationFailed,
                "The requested component type is not registered.");
        }

        public static ActionResult<JObject> SaveScene(SaveSceneArguments arguments)
        {
            ActionResult<JObject> validation = AuthoringValidation.ValidateSaveScene(arguments);
            if (!validation.Success)
            {
                return validation;
            }

            Scene scene = SceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene);
            return saved
                ? Results.Ok(
                    "Scene saved.",
                    new JObject
                    {
                        ["scenePath"] = ProjectPaths.NormalizeUnityPath(scene.path)
                    })
                : Results.Error(
                    ResultCodes.UnityOperationFailed,
                    "Unity did not save the scene.");
        }

        private static ActionResult<JObject> SetTransformField(
            GameObject gameObject,
            SetComponentFieldArguments arguments)
        {
            Vector3 vector;
            ActionResult<JObject> error;
            if (!AuthoringValidation.TryReadVectorValue(
                    arguments.Value,
                    "Transform." + arguments.Field,
                    out vector,
                    out error))
            {
                return error;
            }

            Transform transform = gameObject.transform;
            Undo.RecordObject(transform, "Set Transform field");
            if (arguments.Field == "localPosition")
            {
                transform.localPosition = vector;
            }
            else if (arguments.Field == "localEulerAngles")
            {
                transform.localEulerAngles = vector;
            }
            else if (arguments.Field == "localScale")
            {
                transform.localScale = vector;
            }
            else
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "The requested Transform field is not registered.");
            }

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            return Results.Ok(
                "Component field updated.",
                FieldResult(gameObject, transform, arguments.Field));
        }

        private static ActionResult<JObject> SetBoxColliderField(
            GameObject gameObject,
            BoxCollider collider,
            SetComponentFieldArguments arguments)
        {
            Undo.RecordObject(collider, "Set BoxCollider field");
            if (arguments.Field == "center" || arguments.Field == "size")
            {
                Vector3 vector;
                ActionResult<JObject> error;
                if (!AuthoringValidation.TryReadVectorValue(
                        arguments.Value,
                        "BoxCollider." + arguments.Field,
                        out vector,
                        out error))
                {
                    return error;
                }

                if (arguments.Field == "center")
                {
                    collider.center = vector;
                }
                else
                {
                    collider.size = vector;
                }
            }
            else if (arguments.Field == "isTrigger")
            {
                bool boolean;
                ActionResult<JObject> error;
                if (!AuthoringValidation.TryReadBooleanValue(
                        arguments.Value,
                        "BoxCollider." + arguments.Field,
                        out boolean,
                        out error))
                {
                    return error;
                }

                collider.isTrigger = boolean;
            }
            else
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "The requested BoxCollider field is not registered.");
            }

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            return Results.Ok(
                "Component field updated.",
                FieldResult(gameObject, collider, arguments.Field));
        }

        private static JObject FieldResult(
            GameObject gameObject,
            Component component,
            string field)
        {
            return new JObject
            {
                ["gameObject"] = AuthoringValidation.ObjectData(gameObject),
                ["component"] = ComponentData(component),
                ["field"] = field
            };
        }

        private static JObject ComponentData(Component component)
        {
            Transform transform = component as Transform;
            if (transform != null)
            {
                return new JObject
                {
                    ["componentType"] = "Transform",
                    ["localPosition"] = AuthoringValidation.VectorData(transform.localPosition),
                    ["localEulerAngles"] =
                        AuthoringValidation.VectorData(transform.localEulerAngles),
                    ["localScale"] = AuthoringValidation.VectorData(transform.localScale)
                };
            }

            BoxCollider collider = component as BoxCollider;
            if (collider != null)
            {
                return new JObject
                {
                    ["componentType"] = "BoxCollider",
                    ["center"] = AuthoringValidation.VectorData(collider.center),
                    ["size"] = AuthoringValidation.VectorData(collider.size),
                    ["isTrigger"] = collider.isTrigger
                };
            }

            return new JObject
            {
                ["componentType"] = component == null ? null : component.GetType().Name
            };
        }
    }
}
