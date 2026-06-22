using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    [InitializeOnLoad]
    public static class EditorSession
    {
        public const string ProtocolVersion = "0.1";

        static EditorSession()
        {
            SessionId = Guid.NewGuid().ToString("D");
            ExecutionMode = CommandExecutionModes.Live;
        }

        public static string SessionId { get; }
        public static string ExecutionMode { get; set; }
    }

    [InitializeOnLoad]
    public static class EditorStateTracker
    {
        private static long contextVersion = 1;
        private static string selectionFingerprint;

        static EditorStateTracker()
        {
            selectionFingerprint = GetSelectionFingerprint();
            EditorApplication.hierarchyChanged += Advance;
            EditorApplication.projectChanged += Advance;
            Selection.selectionChanged += RefreshSelectionVersion;
            Undo.undoRedoPerformed += Advance;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public static long ContextVersion
        {
            get
            {
                RefreshSelectionVersion();
                return contextVersion;
            }
        }

        public static void Advance()
        {
            contextVersion++;
        }

        private static void RefreshSelectionVersion()
        {
            string current = GetSelectionFingerprint();
            if (!string.Equals(current, selectionFingerprint, StringComparison.Ordinal))
            {
                selectionFingerprint = current;
                Advance();
            }
        }

        private static string GetSelectionFingerprint()
        {
            int[] ids = Selection.instanceIDs ?? new int[0];
            Array.Sort(ids);
            return string.Join(",", ids);
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
