using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    public static class EditorStateValidator
    {
        public static ActionResult<JObject> Validate(
            CommandPreconditions preconditions,
            bool isMutation)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return Results.Error(
                    ResultCodes.EditorBusy,
                    "The Unity Editor is compiling or importing assets.");
            }

            if (!isMutation)
            {
                return Results.Ok("Editor state is valid.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "v0.1 mutations are only allowed in Edit Mode.");
            }

            if (preconditions == null)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "Mutation commands require preconditions.");
            }

            if (!string.Equals(
                    preconditions.SessionId,
                    EditorSession.SessionId,
                    StringComparison.Ordinal))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "The command session does not match the current Editor session.");
            }

            if (!string.Equals(preconditions.EditorMode, "edit", StringComparison.Ordinal))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "Mutation commands must require editorMode 'edit'.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            string expectedScene = ProjectPaths.NormalizeUnityPath(preconditions.ActiveScenePath);
            string actualScene = ProjectPaths.NormalizeUnityPath(activeScene.path);
            if (string.IsNullOrEmpty(actualScene))
            {
                return Results.Error(
                    ResultCodes.SceneNotPersisted,
                    "The active scene must be saved before UnityIA can mutate it.");
            }

            if (string.IsNullOrEmpty(expectedScene) ||
                !string.Equals(expectedScene, actualScene, StringComparison.Ordinal))
            {
                return Results.Error(
                    ResultCodes.InvalidEditorState,
                    "The active scene does not match the command precondition.",
                    new JObject
                    {
                        ["expectedActiveScenePath"] = expectedScene,
                        ["actualActiveScenePath"] = actualScene
                    });
            }

            if (!preconditions.ContextVersion.HasValue ||
                preconditions.ContextVersion.Value != EditorStateTracker.ContextVersion)
            {
                return Results.Error(
                    ResultCodes.StaleContext,
                    "The command contextVersion is stale.",
                    new JObject
                    {
                        ["expectedContextVersion"] = EditorStateTracker.ContextVersion,
                        ["receivedContextVersion"] = preconditions.ContextVersion
                    });
            }

            return Results.Ok("Editor state is valid.");
        }
    }
}
