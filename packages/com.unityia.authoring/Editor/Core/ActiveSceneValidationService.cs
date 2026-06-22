using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    internal static class ActiveSceneValidationService
    {
        private static readonly IActiveSceneValidator[] Validators =
        {
            new EditorReadyValidator(),
            new RequestedSceneLoadedValidator(),
            new ActiveScenePersistedValidator(),
            new ActiveSceneMatchesRequestValidator()
        };

        public static ActionResult<JObject> Validate(ValidateActiveSceneArguments arguments)
        {
            ActionResult<JObject> argumentValidation = ValidateArguments(arguments);
            if (!argumentValidation.Success)
            {
                return argumentValidation;
            }

            string requested = ProjectPaths.NormalizeUnityPath(arguments.ScenePath);
            Scene activeScene = SceneManager.GetActiveScene();
            ActiveSceneValidationContext context = new ActiveSceneValidationContext(
                requested,
                ProjectPaths.NormalizeUnityPath(activeScene.path),
                activeScene);
            JArray results = new JArray();
            foreach (IActiveSceneValidator validator in Validators)
            {
                validator.Validate(context, results);
            }

            int errors = CountSeverity(results, "error");
            int warnings = CountSeverity(results, "warning");
            int infos = CountSeverity(results, "info");
            JObject data = new JObject
            {
                ["scenePath"] = requested,
                ["activeScenePath"] = context.ActiveScenePath,
                ["results"] = results,
                ["summary"] = new JObject
                {
                    ["errors"] = errors,
                    ["warnings"] = warnings,
                    ["info"] = infos
                }
            };

            return errors > 0
                ? Results.Error(
                    ResultCodes.ValidationFailed,
                    "Active scene validation failed.",
                    data)
                : Results.Ok("Active scene validation completed.", data);
        }

        public static ActionResult<JObject> ValidateArguments(
            ValidateActiveSceneArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ScenePath))
            {
                return Results.Error(ResultCodes.ValidationFailed, "scenePath is required.");
            }

            string requested = ProjectPaths.NormalizeUnityPath(arguments.ScenePath);
            if (System.IO.Path.IsPathRooted(arguments.ScenePath) ||
                requested.Contains("../") ||
                requested.Equals("..", StringComparison.Ordinal) ||
                !requested.StartsWith("Assets/", StringComparison.Ordinal) ||
                !requested.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Error(
                    ResultCodes.PathNotAllowed,
                    "scenePath must be a normalized Assets/*.unity path.");
            }

            return Results.Ok("Active scene validation arguments are valid.");
        }

        private static int CountSeverity(JArray results, string severity)
        {
            int count = 0;
            foreach (JObject result in results)
            {
                if (string.Equals(
                        result["severity"] == null ? null : result["severity"].Value<string>(),
                        severity,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static void Add(
            JArray results,
            string severity,
            string validatorId,
            string code,
            string message,
            string target = null)
        {
            JObject result = new JObject
            {
                ["severity"] = severity,
                ["validatorId"] = validatorId,
                ["code"] = code,
                ["message"] = message
            };
            if (!string.IsNullOrWhiteSpace(target))
            {
                result["target"] = target;
            }

            results.Add(result);
        }

        private interface IActiveSceneValidator
        {
            void Validate(ActiveSceneValidationContext context, JArray results);
        }

        private sealed class ActiveSceneValidationContext
        {
            public ActiveSceneValidationContext(
                string requestedScenePath,
                string activeScenePath,
                Scene activeScene)
            {
                RequestedScenePath = requestedScenePath ?? string.Empty;
                ActiveScenePath = activeScenePath ?? string.Empty;
                ActiveScene = activeScene;
            }

            public string RequestedScenePath { get; }
            public string ActiveScenePath { get; }
            public Scene ActiveScene { get; }
        }

        private sealed class EditorReadyValidator : IActiveSceneValidator
        {
            public void Validate(ActiveSceneValidationContext context, JArray results)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Add(
                        results,
                        "error",
                        "unityia.editor.ready",
                        ResultCodes.EditorBusy,
                        "The Unity Editor is compiling or importing assets.");
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Add(
                        results,
                        "error",
                        "unityia.editor.mode",
                        ResultCodes.InvalidEditorState,
                        "Validation must run in Edit Mode.");
                    return;
                }

                Add(
                    results,
                    "info",
                    "unityia.editor.ready",
                    ResultCodes.Ok,
                    "Editor state is ready for validation.");
            }
        }

        private sealed class RequestedSceneLoadedValidator : IActiveSceneValidator
        {
            public void Validate(ActiveSceneValidationContext context, JArray results)
            {
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (scene.isLoaded &&
                        string.Equals(
                            ProjectPaths.NormalizeUnityPath(scene.path),
                            context.RequestedScenePath,
                            StringComparison.Ordinal))
                    {
                        Add(
                            results,
                            "info",
                            "unityia.scene.loaded",
                            ResultCodes.Ok,
                            "Requested scene is loaded.",
                            context.RequestedScenePath);
                        return;
                    }
                }

                Add(
                    results,
                    "error",
                    "unityia.scene.loaded",
                    ResultCodes.TargetNotFound,
                    "The requested scene is not loaded.",
                    context.RequestedScenePath);
            }
        }

        private sealed class ActiveScenePersistedValidator : IActiveSceneValidator
        {
            public void Validate(ActiveSceneValidationContext context, JArray results)
            {
                if (string.IsNullOrWhiteSpace(context.ActiveScenePath))
                {
                    Add(
                        results,
                        "error",
                        "unityia.scene.persisted",
                        ResultCodes.SceneNotPersisted,
                        "The active scene must be saved before validation can pass.");
                    return;
                }

                if (!context.ActiveScenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !context.ActiveScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    Add(
                        results,
                        "error",
                        "unityia.scene.persisted",
                        ResultCodes.PathNotAllowed,
                        "The active scene must be an Assets/*.unity scene.",
                        context.ActiveScenePath);
                    return;
                }

                Add(
                    results,
                    "info",
                    "unityia.scene.persisted",
                    ResultCodes.Ok,
                    "Active scene is persisted under Assets.",
                    context.ActiveScenePath);
            }
        }

        private sealed class ActiveSceneMatchesRequestValidator : IActiveSceneValidator
        {
            public void Validate(ActiveSceneValidationContext context, JArray results)
            {
                if (!string.Equals(
                        context.ActiveScenePath,
                        context.RequestedScenePath,
                        StringComparison.Ordinal))
                {
                    Add(
                        results,
                        "error",
                        "unityia.scene.active",
                        ResultCodes.InvalidEditorState,
                        "The active scene does not match scenePath.",
                        context.RequestedScenePath);
                    return;
                }

                Add(
                    results,
                    "info",
                    "unityia.scene.active",
                    ResultCodes.Ok,
                    "Requested scene is active.",
                    context.RequestedScenePath);
            }
        }
    }
}
