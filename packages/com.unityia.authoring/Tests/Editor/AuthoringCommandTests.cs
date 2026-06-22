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
        public void TechnicalCreateCommandIsDirtyAndUndoable()
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

            ActionResult<JObject> result = ExecuteConfirmed(envelope);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(GameObject.Find("CreatedByUnityIA"), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            Undo.PerformUndo();

            Assert.That(GameObject.Find("CreatedByUnityIA"), Is.Null);
        }

        [Test]
        public void PublicCreateGameObjectCommandIsDirtyAndUndoable()
        {
            RequireCapabilities("scene.gameobject.create");
            CommandEnvelope envelope = MutationEnvelope(
                "authoring.create_gameobject",
                new JObject
                {
                    ["scenePath"] = TestScenePath,
                    ["name"] = "CreatedByPublicAuthoring",
                    ["position"] = new JObject
                    {
                        ["x"] = 1,
                        ["y"] = 2,
                        ["z"] = 3
                    }
                });

            ActionResult<JObject> result = ExecuteConfirmed(envelope);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(GameObject.Find("CreatedByPublicAuthoring"), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            Undo.PerformUndo();

            Assert.That(GameObject.Find("CreatedByPublicAuthoring"), Is.Null);
        }

        [Test]
        public void PublicAddComponentAndSetFieldUseClosedCatalog()
        {
            RequireCapabilities(
                "scene.gameobject.create",
                "scene.component.add",
                "scene.component.write");
            ActionResult<JObject> create = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.create_gameobject",
                    new JObject
                    {
                        ["scenePath"] = TestScenePath,
                        ["name"] = "ColliderTarget"
                    }));
            Assert.That(create.Success, Is.True, create.Message);

            JObject target = new JObject
            {
                ["scenePath"] = TestScenePath,
                ["hierarchyPath"] = "/ColliderTarget"
            };
            ActionResult<JObject> add = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.add_component",
                    new JObject
                    {
                        ["target"] = target,
                        ["componentType"] = "BoxCollider"
                    }));
            Assert.That(add.Success, Is.True, add.Message);

            GameObject gameObject = GameObject.Find("ColliderTarget");
            BoxCollider collider = gameObject.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);

            ActionResult<JObject> setBool = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.set_component_field",
                    new JObject
                    {
                        ["target"] = target,
                        ["componentType"] = "BoxCollider",
                        ["field"] = "isTrigger",
                        ["value"] = true
                    }));
            Assert.That(setBool.Success, Is.True, setBool.Message);
            Assert.That(collider.isTrigger, Is.True);

            ActionResult<JObject> setVector = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.set_component_field",
                    new JObject
                    {
                        ["target"] = target,
                        ["componentType"] = "BoxCollider",
                        ["field"] = "center",
                        ["value"] = new JObject
                        {
                            ["x"] = 1,
                            ["y"] = 2,
                            ["z"] = 3
                        }
                    }));
            Assert.That(setVector.Success, Is.True, setVector.Message);
            Assert.That(collider.center, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void PublicAddComponentRejectsUnregisteredComponent()
        {
            RequireCapabilities("scene.gameobject.create", "scene.component.add");
            ActionResult<JObject> create = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.create_gameobject",
                    new JObject
                    {
                        ["scenePath"] = TestScenePath,
                        ["name"] = "UnsupportedComponentTarget"
                    }));
            Assert.That(create.Success, Is.True, create.Message);

            ActionResult<JObject> add = CoreServices.Dispatcher.Execute(
                MutationEnvelope(
                    "authoring.add_component",
                    new JObject
                    {
                        ["target"] = new JObject
                        {
                            ["scenePath"] = TestScenePath,
                            ["hierarchyPath"] = "/UnsupportedComponentTarget"
                        },
                        ["componentType"] = "Rigidbody"
                    }));

            Assert.That(add.Success, Is.False);
            Assert.That(add.Code, Is.EqualTo(ResultCodes.ValidationFailed));
        }

        [Test]
        public void PublicSaveCommandPersistsDirtyScene()
        {
            RequireCapabilities("scene.gameobject.create", "scene.save");
            ActionResult<JObject> create = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.create_gameobject",
                    new JObject
                    {
                        ["scenePath"] = TestScenePath,
                        ["name"] = "SaveCandidate"
                    }));
            Assert.That(create.Success, Is.True, create.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            ActionResult<JObject> save = ExecuteConfirmed(
                MutationEnvelope(
                    "authoring.save_scene",
                    new JObject { ["scenePath"] = TestScenePath }));

            Assert.That(save.Success, Is.True, save.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void TechnicalSaveCommandPersistsDirtyScene()
        {
            RequireMutationPermission();
            ActionResult<JObject> create = ExecuteConfirmed(
                MutationEnvelope(
                    "scene.object.create-empty",
                    new JObject { ["name"] = "SaveCandidate" }));
            Assert.That(create.Success, Is.True, create.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);

            ActionResult<JObject> save = ExecuteConfirmed(
                MutationEnvelope(
                    "scene.save",
                    new JObject { ["scenePath"] = TestScenePath }));

            Assert.That(save.Success, Is.True, save.Message);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void ContextVersionAdvancesWhenSelectionChanges()
        {
            GameObject gameObject = new GameObject("UnityIASelectionProbe");
            try
            {
                Selection.activeObject = null;
                long before = EditorStateTracker.ContextVersion;

                Selection.activeObject = gameObject;

                Assert.That(EditorStateTracker.ContextVersion, Is.GreaterThan(before));
            }
            finally
            {
                Selection.activeObject = null;
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void MutationWithoutConfirmationDoesNotExecute()
        {
            RequireCapabilities("scene.gameobject.create");
            CommandEnvelope envelope = MutationEnvelope(
                "authoring.create_gameobject",
                new JObject
                {
                    ["scenePath"] = TestScenePath,
                    ["name"] = "NeedsApproval"
                });

            ActionResult<JObject> result = CoreServices.Dispatcher.Execute(envelope);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(ResultCodes.ConfirmationRequired));
            Assert.That(GameObject.Find("NeedsApproval"), Is.Null);
        }

        [Test]
        public void ApprovalMatchesExactPayloadOnly()
        {
            RequireCapabilities("scene.gameobject.create");
            CommandEnvelope envelope = MutationEnvelope(
                "authoring.create_gameobject",
                new JObject
                {
                    ["scenePath"] = TestScenePath,
                    ["name"] = "ApprovedOriginal"
                });
            ActionResult<JObject> first = CoreServices.Dispatcher.Execute(envelope);
            Assert.That(first.Code, Is.EqualTo(ResultCodes.ConfirmationRequired));
            Assert.That(
                CoreServices.Confirmations.ApprovePending(envelope.CommandId, true).Success,
                Is.True);

            CommandEnvelope altered = MutationEnvelope(
                "authoring.create_gameobject",
                new JObject
                {
                    ["scenePath"] = TestScenePath,
                    ["name"] = "AlteredPayload"
                });
            altered.CommandId = envelope.CommandId;

            ActionResult<JObject> result = CoreServices.Dispatcher.Execute(altered);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(ResultCodes.ConfirmationDenied));
            Assert.That(GameObject.Find("AlteredPayload"), Is.Null);
            Assert.That(GameObject.Find("ApprovedOriginal"), Is.Null);
        }

        [Test]
        public void ExpiredApprovalDoesNotExecute()
        {
            RequireCapabilities("scene.gameobject.create");
            CommandEnvelope envelope = MutationEnvelope(
                "authoring.create_gameobject",
                new JObject
                {
                    ["scenePath"] = TestScenePath,
                    ["name"] = "ExpiredApproval"
                });
            ActionResult<JObject> first = CoreServices.Dispatcher.Execute(envelope);
            Assert.That(first.Code, Is.EqualTo(ResultCodes.ConfirmationRequired));
            Assert.That(
                CoreServices.Confirmations.Approve(
                    envelope,
                    true,
                    "Expired test approval.",
                    DateTimeOffset.UtcNow.AddSeconds(-1)).Success,
                Is.True);

            ActionResult<JObject> result = CoreServices.Dispatcher.Execute(envelope);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(ResultCodes.ConfirmationRequired));
            Assert.That(GameObject.Find("ExpiredApproval"), Is.Null);
        }

        [Test]
        public void ActiveSceneValidationReportsSuccessAndMismatch()
        {
            RequireCapabilitiesForAccess(CommandPathAccess.Read, "validation.scene.run");
            CommandEnvelope valid = ReadEnvelope(
                "validate.active_scene",
                new JObject { ["scenePath"] = TestScenePath });

            ActionResult<JObject> success = CoreServices.Dispatcher.Execute(valid);

            Assert.That(success.Success, Is.True, success.Message);
            Assert.That(success.Data["summary"]?["errors"]?.Value<int>(), Is.EqualTo(0));

            CommandEnvelope mismatch = ReadEnvelope(
                "validate.active_scene",
                new JObject { ["scenePath"] = "Assets/Scenes/Other.unity" });
            ActionResult<JObject> failure = CoreServices.Dispatcher.Execute(mismatch);

            Assert.That(failure.Success, Is.False);
            Assert.That(failure.Code, Is.EqualTo(ResultCodes.ValidationFailed));
            Assert.That(failure.Data["summary"]?["errors"]?.Value<int>(), Is.GreaterThan(0));
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

        private static CommandEnvelope ReadEnvelope(string command, JObject arguments)
        {
            return new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = command,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = arguments,
                Options = new CommandOptions()
            };
        }

        private static ActionResult<JObject> ExecuteConfirmed(CommandEnvelope envelope)
        {
            ActionResult<JObject> first = CoreServices.Dispatcher.Execute(envelope);
            if (first.Code != ResultCodes.ConfirmationRequired)
            {
                return first;
            }

            ActionResult<JObject> approval =
                CoreServices.Confirmations.ApprovePending(envelope.CommandId, true);
            Assert.That(approval.Success, Is.True, approval.Message);
            return CoreServices.Dispatcher.Execute(envelope);
        }

        private static void RequireMutationPermission()
        {
            RequireCapabilities("scene.modify");
        }

        private static void RequireCapabilities(params string[] capabilities)
        {
            RequireCapabilitiesForAccess(CommandPathAccess.Write, capabilities);
        }

        private static void RequireCapabilitiesForAccess(
            string pathAccess,
            params string[] capabilities)
        {
            foreach (string capability in capabilities)
            {
                PermissionDecision decision = CoreServices.Permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = capability,
                        Path = TestScenePath,
                        PathAccess = pathAccess
                    });
                if (!decision.Allowed)
                {
                    Assert.Ignore(
                        "The development project policy does not allow " +
                        capability + " for " + TestScenePath + ".");
                }
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
