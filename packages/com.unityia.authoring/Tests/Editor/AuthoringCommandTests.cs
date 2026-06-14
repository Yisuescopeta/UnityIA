using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Tests
{
    public sealed class AuthoringCommandTests
    {
        private const string TestFolder = "Assets/Scenes/UnityIATests";
        private const string TestScenePath = TestFolder + "/AuthoringTest.unity";
        private bool createdScenesFolder;

        [SetUp]
        public void SetUp()
        {
            createdScenesFolder = EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Scenes", "UnityIATests");
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(scene, TestScenePath), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TestFolder);
            if (createdScenesFolder)
            {
                AssetDatabase.DeleteAsset("Assets/Scenes");
            }
        }

        [Test]
        public void CreateCommandIsDirtyAndUndoable()
        {
            RequireMutationPermission();
            CommandEnvelope envelope = MutationEnvelope(
                "scene.object.create-empty",
                new JObject
                {
                    ["name"] = "CreatedByUnityIA",
                    ["position"] = new JObject
                    {
                        ["x"] = 1,
                        ["y"] = 2,
                        ["z"] = 3
                    }
                });

            ActionResult<JObject> result = CoreServices.Dispatcher.Execute(envelope);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(GameObject.Find("CreatedByUnityIA"), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            Undo.PerformUndo();

            Assert.That(GameObject.Find("CreatedByUnityIA"), Is.Null);
        }

        [Test]
        public void OnlySaveCommandPersistsDirtyScene()
        {
            RequireMutationPermission();
            ActionResult<JObject> create = CoreServices.Dispatcher.Execute(
                MutationEnvelope(
                    "scene.object.create-empty",
                    new JObject { ["name"] = "SaveCandidate" }));
            Assert.That(create.Success, Is.True, create.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            ActionResult<JObject> save = CoreServices.Dispatcher.Execute(
                MutationEnvelope(
                    "scene.save",
                    new JObject { ["scenePath"] = TestScenePath }));

            Assert.That(save.Success, Is.True, save.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
        }

        private static CommandEnvelope MutationEnvelope(string command, JObject arguments)
        {
            return new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = command,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Preconditions = new CommandPreconditions
                {
                    SessionId = EditorSession.SessionId,
                    EditorMode = "edit",
                    ActiveScenePath = TestScenePath,
                    ContextVersion = EditorStateTracker.ContextVersion
                },
                Arguments = arguments,
                Options = new CommandOptions()
            };
        }

        private static void RequireMutationPermission()
        {
            PermissionDecision decision = CoreServices.Permissions.Evaluate(
                new PermissionRequest
                {
                    Capability = "scene.modify",
                    Path = TestScenePath
                });
            if (!decision.Allowed)
            {
                Assert.Ignore(
                    "The development project policy does not allow scene.modify for " +
                    TestScenePath + ".");
            }
        }

        private static bool EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (AssetDatabase.IsValidFolder(path))
            {
                return false;
            }

            AssetDatabase.CreateFolder(parent, child);
            return true;
        }
    }
}
