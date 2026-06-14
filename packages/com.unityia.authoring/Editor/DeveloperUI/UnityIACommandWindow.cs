using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.DeveloperUI
{
    public sealed class UnityIACommandWindow : EditorWindow
    {
        private string inputJson;
        private string outputJson = string.Empty;
        private Vector2 inputScroll;
        private Vector2 outputScroll;

        [MenuItem("Window/UnityIA/Command Console")]
        public static void Open()
        {
            GetWindow<UnityIACommandWindow>("UnityIA Commands");
        }

        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(inputJson))
            {
                inputJson = CreateStatusCommand();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "This window invokes Core.CommandDispatcher directly. It does not use HTTP.",
                MessageType.Info);

            EditorGUILayout.LabelField("Command JSON", EditorStyles.boldLabel);
            inputScroll = EditorGUILayout.BeginScrollView(inputScroll, GUILayout.MinHeight(180));
            inputJson = EditorGUILayout.TextArea(inputJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Status example"))
            {
                inputJson = CreateStatusCommand();
            }

            if (GUILayout.Button("Context example"))
            {
                inputJson = CreateContextCommand();
            }

            if (GUILayout.Button("Validate"))
            {
                CommandEnvelope envelope;
                string error;
                ActionResult<JObject> result = CommandJson.TryDeserialize(
                    inputJson,
                    out envelope,
                    out error)
                    ? CoreServices.Dispatcher.Validate(envelope)
                    : Results.Error(ResultCodes.InvalidJson, error);
                outputJson = CommandJson.Serialize(result, Formatting.Indented);
            }

            if (GUILayout.Button("Execute"))
            {
                outputJson = CommandJson.Serialize(
                    CoreServices.Dispatcher.ExecuteJson(inputJson),
                    Formatting.Indented);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ActionResult", EditorStyles.boldLabel);
            outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(outputJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static string CreateStatusCommand()
        {
            return CreateReadCommand("system.status", new JObject());
        }

        private static string CreateContextCommand()
        {
            return CreateReadCommand(
                "context.get",
                new JObject { ["includeHierarchy"] = false });
        }

        private static string CreateReadCommand(string command, JObject arguments)
        {
            CommandEnvelope envelope = new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = command,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = arguments,
                Options = new CommandOptions()
            };
            return CommandJson.Serialize(envelope, Formatting.Indented);
        }
    }
}

