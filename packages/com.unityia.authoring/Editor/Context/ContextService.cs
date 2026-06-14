using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Context
{
    public static class ContextService
    {
        public static ActionResult<JObject> GetSnapshot(ContextQuery query)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            JObject data = new JObject
            {
                ["sessionId"] = EditorSession.SessionId,
                ["contextVersion"] = EditorStateTracker.ContextVersion,
                ["activeScenePath"] = ProjectPaths.NormalizeUnityPath(activeScene.path),
                ["openScenes"] = GetOpenScenesArray()
            };

            if (query != null && query.IncludeHierarchy)
            {
                ActionResult<JObject> hierarchy = GetHierarchy(
                    new HierarchyQuery { ScenePath = activeScene.path });
                if (!hierarchy.Success)
                {
                    return hierarchy;
                }

                data["hierarchy"] = hierarchy.Data["objects"];
            }

            return Results.Ok("Editor context.", data);
        }

        public static ActionResult<JObject> GetHierarchy(HierarchyQuery query)
        {
            string requestedPath = ProjectPaths.NormalizeUnityPath(
                query == null || string.IsNullOrWhiteSpace(query.ScenePath)
                    ? SceneManager.GetActiveScene().path
                    : query.ScenePath);
            Scene scene = FindLoadedScene(requestedPath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return Results.Error(
                    ResultCodes.TargetNotFound,
                    "The requested scene is not loaded.");
            }

            JArray objects = new JArray();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AddHierarchy(root, objects);
            }

            return Results.Ok(
                "Scene hierarchy.",
                new JObject
                {
                    ["scenePath"] = requestedPath,
                    ["objects"] = objects
                });
        }

        public static ActionResult<JObject> GetObject(SceneObjectQuery query)
        {
            ObjectResolution resolution = SceneObjectResolver.Resolve(
                query == null ? null : query.Target,
                SceneManager.GetActiveScene().path);
            return resolution.Success
                ? Results.Ok(
                    "Scene object.",
                    SceneObjectResolver.Describe(resolution.GameObject))
                : resolution.Error;
        }

        public static JArray GetOpenScenesArray()
        {
            JArray scenes = new JArray();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                scenes.Add(
                    new JObject
                    {
                        ["path"] = ProjectPaths.NormalizeUnityPath(scene.path),
                        ["name"] = scene.name,
                        ["isLoaded"] = scene.isLoaded,
                        ["isDirty"] = scene.isDirty,
                        ["isActive"] = scene == SceneManager.GetActiveScene()
                    });
            }

            return scenes;
        }

        private static Scene FindLoadedScene(string path)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (string.Equals(
                        ProjectPaths.NormalizeUnityPath(scene.path),
                        path,
                        StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default(Scene);
        }

        private static void AddHierarchy(GameObject gameObject, JArray objects)
        {
            objects.Add(SceneObjectResolver.Describe(gameObject));
            Transform transform = gameObject.transform;
            for (int index = 0; index < transform.childCount; index++)
            {
                AddHierarchy(transform.GetChild(index).gameObject, objects);
            }
        }
    }
}

