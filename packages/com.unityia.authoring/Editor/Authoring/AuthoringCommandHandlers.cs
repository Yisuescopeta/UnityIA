using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Context;
using UnityIA.Core;

namespace UnityIA.Authoring
{
    [InitializeOnLoad]
    internal static class AuthoringBootstrap
    {
        static AuthoringBootstrap()
        {
            CoreServices.Registry.Register(new CreateEmptyHandler());
            CoreServices.Registry.Register(new RenameHandler());
            CoreServices.Registry.Register(new SetActiveHandler());
            CoreServices.Registry.Register(new SetTransformHandler());
            CoreServices.Registry.Register(new ReparentHandler());
            CoreServices.Registry.Register(new DeleteHandler());
            CoreServices.Registry.Register(new SaveSceneHandler());
        }
    }

    internal sealed class CreateEmptyHandler : CommandHandler<CreateEmptyArguments>
    {
        public CreateEmptyHandler()
            : base("scene.object.create-empty", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            CreateEmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ActionResult<JObject> name = AuthoringValidation.ValidateName(
                arguments == null ? null : arguments.Name);
            if (!name.Success)
            {
                return name;
            }

            ActionResult<JObject> vector = AuthoringValidation.ValidateVector(
                arguments.Position,
                "position");
            if (!vector.Success)
            {
                return vector;
            }

            vector = AuthoringValidation.ValidateVector(
                arguments.RotationEuler,
                "rotationEuler");
            if (!vector.Success)
            {
                return vector;
            }

            vector = AuthoringValidation.ValidateVector(arguments.Scale, "scale");
            if (!vector.Success)
            {
                return vector;
            }

            if (arguments.Parent != null)
            {
                ObjectResolution parent = AuthoringValidation.Resolve(arguments.Parent, envelope);
                if (!parent.Success)
                {
                    return parent.Error;
                }

                if (PrefabUtility.IsPartOfPrefabInstance(parent.GameObject))
                {
                    return Results.Error(
                        ResultCodes.InvalidEditorState,
                        "v0.1 cannot add children inside a prefab instance.");
                }
            }

            return Results.Ok("Create arguments are valid.");
        }

        protected override ActionResult<JObject> Execute(
            CreateEmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject gameObject = new GameObject(arguments.Name.Trim());
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);

            if (arguments.Parent != null)
            {
                ObjectResolution parent = AuthoringValidation.Resolve(arguments.Parent, envelope);
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
    }

    internal sealed class RenameHandler : CommandHandler<RenameArguments>
    {
        public RenameHandler() : base("scene.object.rename", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            RenameArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ActionResult<JObject> name = AuthoringValidation.ValidateName(
                arguments == null ? null : arguments.Name);
            if (!name.Success)
            {
                return name;
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            return target.Success ? Results.Ok("Target is valid.") : target.Error;
        }

        protected override ActionResult<JObject> Execute(
            RenameArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            Undo.RecordObject(target.GameObject, "Rename GameObject");
            target.GameObject.name = arguments.Name.Trim();
            EditorSceneManager.MarkSceneDirty(target.GameObject.scene);
            return Results.Ok(
                "GameObject renamed.",
                AuthoringValidation.ObjectData(target.GameObject));
        }
    }

    internal sealed class SetActiveHandler : CommandHandler<SetActiveArguments>
    {
        public SetActiveHandler()
            : base("scene.object.set-active", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            SetActiveArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            return target.Success ? Results.Ok("Target is valid.") : target.Error;
        }

        protected override ActionResult<JObject> Execute(
            SetActiveArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            Undo.RecordObject(target.GameObject, "Set GameObject active state");
            target.GameObject.SetActive(arguments.Active);
            EditorSceneManager.MarkSceneDirty(target.GameObject.scene);
            return Results.Ok(
                "GameObject active state updated.",
                AuthoringValidation.ObjectData(target.GameObject));
        }
    }

    internal sealed class SetTransformHandler : CommandHandler<SetTransformArguments>
    {
        public SetTransformHandler()
            : base("scene.object.set-transform", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            SetTransformArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            if (arguments.Position == null &&
                arguments.RotationEuler == null &&
                arguments.Scale == null)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "Provide position, rotationEuler, or scale.");
            }

            foreach (Tuple<Vector3Dto, string> item in new[]
                     {
                         Tuple.Create(arguments.Position, "position"),
                         Tuple.Create(arguments.RotationEuler, "rotationEuler"),
                         Tuple.Create(arguments.Scale, "scale")
                     })
            {
                ActionResult<JObject> vector =
                    AuthoringValidation.ValidateVector(item.Item1, item.Item2);
                if (!vector.Success)
                {
                    return vector;
                }
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            return target.Success ? Results.Ok("Target is valid.") : target.Error;
        }

        protected override ActionResult<JObject> Execute(
            SetTransformArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            Transform transform = target.GameObject.transform;
            Undo.RecordObject(transform, "Set GameObject transform");
            if (arguments.Position != null)
            {
                transform.localPosition = AuthoringValidation.ToVector(arguments.Position);
            }

            if (arguments.RotationEuler != null)
            {
                transform.localEulerAngles =
                    AuthoringValidation.ToVector(arguments.RotationEuler);
            }

            if (arguments.Scale != null)
            {
                transform.localScale = AuthoringValidation.ToVector(arguments.Scale);
            }

            EditorSceneManager.MarkSceneDirty(target.GameObject.scene);
            return Results.Ok(
                "Transform updated.",
                AuthoringValidation.ObjectData(target.GameObject));
        }
    }

    internal sealed class ReparentHandler : CommandHandler<ReparentArguments>
    {
        public ReparentHandler()
            : base("scene.object.reparent", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            ReparentArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            if (AuthoringValidation.IsInternalPrefabObject(target.GameObject))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 cannot structurally modify prefab instance contents.");
            }

            if (arguments.Parent == null)
            {
                return Results.Ok("Reparent target is valid.");
            }

            ObjectResolution parent = AuthoringValidation.Resolve(arguments.Parent, envelope);
            if (!parent.Success)
            {
                return parent.Error;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(parent.GameObject))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 cannot parent objects inside a prefab instance.");
            }

            if (target.GameObject.scene != parent.GameObject.scene)
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 reparenting is limited to the same scene.");
            }

            if (parent.GameObject.transform == target.GameObject.transform ||
                parent.GameObject.transform.IsChildOf(target.GameObject.transform))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "The requested parent would create a hierarchy cycle.");
            }

            return Results.Ok("Reparent target is valid.");
        }

        protected override ActionResult<JObject> Execute(
            ReparentArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            Transform parentTransform = null;
            if (arguments.Parent != null)
            {
                ObjectResolution parent = AuthoringValidation.Resolve(arguments.Parent, envelope);
                if (!parent.Success)
                {
                    return parent.Error;
                }

                parentTransform = parent.GameObject.transform;
            }

            Transform targetTransform = target.GameObject.transform;
            Vector3 localPosition = targetTransform.localPosition;
            Quaternion localRotation = targetTransform.localRotation;
            Vector3 localScale = targetTransform.localScale;
            Undo.SetTransformParent(targetTransform, parentTransform, "Reparent GameObject");
            if (!arguments.WorldPositionStays)
            {
                Undo.RecordObject(targetTransform, "Preserve local transform");
                targetTransform.localPosition = localPosition;
                targetTransform.localRotation = localRotation;
                targetTransform.localScale = localScale;
            }
            EditorSceneManager.MarkSceneDirty(target.GameObject.scene);
            return Results.Ok(
                "GameObject reparented.",
                AuthoringValidation.ObjectData(target.GameObject));
        }
    }

    internal sealed class DeleteHandler : CommandHandler<DeleteArguments>
    {
        public DeleteHandler() : base("scene.object.delete", true, "scene.modify")
        {
        }

        protected override ActionResult<JObject> Validate(
            DeleteArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || arguments.Target == null)
            {
                return Results.Error(ResultCodes.ValidationFailed, "target is required.");
            }

            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            return AuthoringValidation.IsInternalPrefabObject(target.GameObject)
                ? Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 cannot structurally modify prefab instance contents.")
                : Results.Ok("Delete target is valid.");
        }

        protected override ActionResult<JObject> Execute(
            DeleteArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            ObjectResolution target = AuthoringValidation.Resolve(arguments.Target, envelope);
            if (!target.Success)
            {
                return target.Error;
            }

            Scene scene = target.GameObject.scene;
            JObject deleted = AuthoringValidation.ObjectData(target.GameObject);
            Undo.DestroyObjectImmediate(target.GameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            return Results.Ok(
                "GameObject deleted.",
                new JObject { ["deleted"] = deleted });
        }
    }

    internal sealed class SaveSceneHandler : CommandHandler<SaveSceneArguments>
    {
        public SaveSceneHandler() : base("scene.save", true, "scene.save")
        {
        }

        protected override ActionResult<JObject> Validate(
            SaveSceneArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ScenePath))
            {
                return Results.Error(ResultCodes.ValidationFailed, "scenePath is required.");
            }

            string requested = ProjectPaths.NormalizeUnityPath(arguments.ScenePath);
            string active = ProjectPaths.NormalizeUnityPath(SceneManager.GetActiveScene().path);
            if (!requested.StartsWith("Assets/", StringComparison.Ordinal) ||
                !requested.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Error(
                    ResultCodes.PathNotAllowed,
                    "scenePath must reference an existing Assets/*.unity scene.");
            }

            return string.Equals(requested, active, StringComparison.Ordinal)
                ? Results.Ok("Scene save target is valid.")
                : Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 can save only the expected active scene.");
        }

        protected override ActionResult<JObject> Execute(
            SaveSceneArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
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
    }
}
