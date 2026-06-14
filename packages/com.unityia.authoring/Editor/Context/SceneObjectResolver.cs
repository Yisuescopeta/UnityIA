using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Context
{
    public sealed class ObjectResolution
    {
        private ObjectResolution(GameObject gameObject, ActionResult<JObject> error)
        {
            GameObject = gameObject;
            Error = error;
        }

        public GameObject GameObject { get; }
        public ActionResult<JObject> Error { get; }
        public bool Success => GameObject != null && Error == null;

        public static ObjectResolution Resolved(GameObject gameObject)
        {
            return new ObjectResolution(gameObject, null);
        }

        public static ObjectResolution Failed(string code, string message)
        {
            return new ObjectResolution(null, Results.Error(code, message));
        }
    }

    public static class SceneObjectResolver
    {
        public static ObjectResolution Resolve(
            ObjectReferenceDto reference,
            string expectedActiveScenePath)
        {
            if (reference == null)
            {
                return ObjectResolution.Failed(
                    ResultCodes.ValidationFailed,
                    "An object reference is required.");
            }

            bool hasGlobalId = !string.IsNullOrWhiteSpace(reference.GlobalObjectId);
            bool hasHierarchyPath = !string.IsNullOrWhiteSpace(reference.HierarchyPath);
            if (!hasGlobalId && !hasHierarchyPath)
            {
                return ObjectResolution.Failed(
                    ResultCodes.ValidationFailed,
                    "Provide globalObjectId or hierarchyPath.");
            }

            GameObject byGlobalId = null;
            GameObject byHierarchy = null;

            if (hasGlobalId)
            {
                GlobalObjectId globalId;
                if (!GlobalObjectId.TryParse(reference.GlobalObjectId, out globalId))
                {
                    return ObjectResolution.Failed(
                        ResultCodes.ValidationFailed,
                        "globalObjectId is invalid.");
                }

                UnityEngine.Object resolved =
                    GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                byGlobalId = resolved as GameObject;
                if (byGlobalId == null)
                {
                    return ObjectResolution.Failed(
                        ResultCodes.TargetNotFound,
                        "globalObjectId did not resolve to a loaded scene GameObject.");
                }
            }

            if (hasHierarchyPath)
            {
                string requestedScene = ProjectPaths.NormalizeUnityPath(
                    string.IsNullOrWhiteSpace(reference.ScenePath)
                        ? expectedActiveScenePath
                        : reference.ScenePath);
                string activeScenePath = ProjectPaths.NormalizeUnityPath(
                    SceneManager.GetActiveScene().path);
                if (!string.Equals(
                        requestedScene,
                        activeScenePath,
                        StringComparison.Ordinal))
                {
                    return ObjectResolution.Failed(
                        ResultCodes.InvalidEditorState,
                        "hierarchyPath resolution is limited to the expected active scene.");
                }

                Scene scene = FindLoadedScene(requestedScene);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return ObjectResolution.Failed(
                        ResultCodes.TargetNotFound,
                        "The referenced scene is not loaded.");
                }

                List<GameObject> matches = ResolveHierarchyPath(
                    scene,
                    reference.HierarchyPath);
                if (matches.Count == 0)
                {
                    return ObjectResolution.Failed(
                        ResultCodes.TargetNotFound,
                        "hierarchyPath did not resolve to an object.");
                }

                if (matches.Count > 1)
                {
                    return ObjectResolution.Failed(
                        ResultCodes.AmbiguousTarget,
                        "hierarchyPath resolves to multiple objects.");
                }

                byHierarchy = matches[0];
            }

            GameObject result = byGlobalId ?? byHierarchy;
            if (byGlobalId != null && byHierarchy != null && byGlobalId != byHierarchy)
            {
                return ObjectResolution.Failed(
                    ResultCodes.AmbiguousTarget,
                    "globalObjectId and hierarchyPath resolve to different objects.");
            }

            string expectedScene = ProjectPaths.NormalizeUnityPath(
                string.IsNullOrWhiteSpace(reference.ScenePath)
                    ? expectedActiveScenePath
                    : reference.ScenePath);
            if (!string.IsNullOrEmpty(expectedScene) &&
                !string.Equals(
                    ProjectPaths.NormalizeUnityPath(result.scene.path),
                    expectedScene,
                    StringComparison.Ordinal))
            {
                return ObjectResolution.Failed(
                    ResultCodes.TargetNotFound,
                    "The resolved object is not in the expected scene.");
            }

            if (!IsSupportedSceneObject(result))
            {
                return ObjectResolution.Failed(
                    ResultCodes.InvalidEditorState,
                    "v0.1 supports only GameObjects in normal loaded .unity scenes.");
            }

            return ObjectResolution.Resolved(result);
        }

        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            Stack<string> segments = new Stack<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                segments.Push(EscapePathSegment(current.name));
                current = current.parent;
            }

            return "/" + string.Join("/", segments.ToArray());
        }

        public static string TryGetGlobalObjectId(GameObject gameObject)
        {
            if (gameObject == null || string.IsNullOrWhiteSpace(gameObject.scene.path))
            {
                return null;
            }

            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            string value = id.ToString();
            return value.Contains("-00000000000000000000000000000000-")
                ? null
                : value;
        }

        public static JObject Describe(GameObject gameObject)
        {
            string globalId = TryGetGlobalObjectId(gameObject);
            Transform transform = gameObject.transform;
            return new JObject
            {
                ["scenePath"] = ProjectPaths.NormalizeUnityPath(gameObject.scene.path),
                ["hierarchyPath"] = GetHierarchyPath(gameObject),
                ["name"] = gameObject.name,
                ["globalObjectId"] = globalId == null ? JValue.CreateNull() : globalId,
                ["globalObjectIdAvailable"] = globalId != null,
                ["activeSelf"] = gameObject.activeSelf,
                ["activeInHierarchy"] = gameObject.activeInHierarchy,
                ["position"] = VectorToJson(transform.localPosition),
                ["rotationEuler"] = VectorToJson(transform.localEulerAngles),
                ["scale"] = VectorToJson(transform.localScale)
            };
        }

        private static bool IsSupportedSceneObject(GameObject gameObject)
        {
            string path = ProjectPaths.NormalizeUnityPath(gameObject.scene.path);
            return gameObject.scene.IsValid() &&
                   gameObject.scene.isLoaded &&
                   path.StartsWith("Assets/", StringComparison.Ordinal) &&
                   path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                   !PrefabUtility.IsPartOfPrefabAsset(gameObject);
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

        private static List<GameObject> ResolveHierarchyPath(Scene scene, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
            {
                return new List<GameObject>();
            }

            string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(UnescapePathSegment)
                .ToArray();
            if (segments.Length == 0)
            {
                return new List<GameObject>();
            }

            List<GameObject> current = scene.GetRootGameObjects()
                .Where(root => root.name == segments[0])
                .ToList();

            for (int index = 1; index < segments.Length && current.Count > 0; index++)
            {
                string segment = segments[index];
                current = current
                    .SelectMany(parent => DirectChildren(parent.transform))
                    .Where(child => child.name == segment)
                    .Select(child => child.gameObject)
                    .ToList();
            }

            return current;
        }

        private static IEnumerable<Transform> DirectChildren(Transform parent)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                yield return parent.GetChild(index);
            }
        }

        private static string EscapePathSegment(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private static string UnescapePathSegment(string value)
        {
            return value.Replace("~1", "/").Replace("~0", "~");
        }

        private static JObject VectorToJson(Vector3 vector)
        {
            return new JObject
            {
                ["x"] = vector.x,
                ["y"] = vector.y,
                ["z"] = vector.z
            };
        }
    }
}

