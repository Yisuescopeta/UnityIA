using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityIA.Core
{
    [InitializeOnLoad]
    public static class EditorSession
    {
        public const string ProtocolVersion = "0.1";

        static EditorSession()
        {
            SessionId = Guid.NewGuid().ToString("D");
        }

        public static string SessionId { get; }
    }

    [InitializeOnLoad]
    public static class EditorStateTracker
    {
        private static long contextVersion = 1;

        static EditorStateTracker()
        {
            EditorApplication.hierarchyChanged += Advance;
            EditorApplication.projectChanged += Advance;
            Selection.selectionChanged += Advance;
            Undo.undoRedoPerformed += Advance;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public static long ContextVersion => contextVersion;

        public static void Advance()
        {
            contextVersion++;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            Advance();
        }

        private static void OnSceneClosed(Scene scene)
        {
            Advance();
        }

        private static void OnSceneSaved(Scene scene)
        {
            Advance();
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            Advance();
        }
    }

    public static class ProjectPaths
    {
        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent == null ? string.Empty : parent.FullName;
            }
        }

        public static string NormalizeUnityPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }
    }
}
