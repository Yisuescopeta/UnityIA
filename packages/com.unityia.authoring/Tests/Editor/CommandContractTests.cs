using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Tests
{
    public sealed class CommandContractTests
    {
        [Test]
        public void StrictEnvelopeRejectsUnknownProperties()
        {
            const string json =
                "{\"protocolVersion\":\"0.1\"," +
                "\"commandId\":\"0edcf18a-c996-43f4-88d4-3c5b64f0099e\"," +
                "\"command\":\"system.status\"," +
                "\"issuedAtUtc\":\"2026-06-14T12:00:00Z\"," +
                "\"arguments\":{},\"options\":{}," +
                "\"unexpected\":true}";

            bool success = CommandJson.TryDeserialize(json, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("unexpected"));
        }

        [Test]
        public void RegistryContainsTechnicalAndPublicCommands()
        {
            string[] expected =
            {
                "system.status",
                "system.commands.list",
                "capabilities.list",
                "validate.active_scene",
                "context.snapshot",
                "context.get",
                "scene.list-open",
                "scene.hierarchy.get",
                "scene.object.get",
                "authoring.create_gameobject",
                "authoring.add_component",
                "authoring.set_component_field",
                "authoring.save_scene",
                "scene.object.create-empty",
                "scene.object.rename",
                "scene.object.set-active",
                "scene.object.set-transform",
                "scene.object.reparent",
                "scene.object.delete",
                "scene.save",
                "validation.command.validate",
                "permissions.explain"
            };

            string[] actual = System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Select(
                    CoreServices.Registry.List(),
                    descriptor => descriptor.Name));

            CollectionAssert.IsSubsetOf(expected, actual);
            CollectionAssert.IsSubsetOf(actual, expected);
        }

        [Test]
        public void PublicAuthoringCommandsUsePublicCapabilities()
        {
            Dictionary<string, CommandDescriptorDto> descriptors =
                CoreServices.Registry.List().ToDictionary(
                    descriptor => descriptor.Name,
                    StringComparer.Ordinal);

            AssertPublicDescriptor(
                descriptors,
                "capabilities.list",
                false,
                "capabilities.read",
                CommandPathAccess.None);
            AssertPublicDescriptor(
                descriptors,
                "validate.active_scene",
                false,
                "validation.scene.run",
                CommandPathAccess.Read);
            AssertPublicDescriptor(
                descriptors,
                "context.snapshot",
                false,
                "context.read",
                CommandPathAccess.Read);
            AssertPublicDescriptor(
                descriptors,
                "authoring.create_gameobject",
                true,
                "scene.gameobject.create",
                CommandPathAccess.Write);
            AssertPublicDescriptor(
                descriptors,
                "authoring.add_component",
                true,
                "scene.component.add",
                CommandPathAccess.Write);
            AssertPublicDescriptor(
                descriptors,
                "authoring.set_component_field",
                true,
                "scene.component.write",
                CommandPathAccess.Write);
            AssertPublicDescriptor(
                descriptors,
                "authoring.save_scene",
                true,
                "scene.save",
                CommandPathAccess.Write);
        }

        [Test]
        public void RegisteredCommandsDeclareV06Metadata()
        {
            foreach (CommandDescriptorDto descriptor in CoreServices.Registry.List())
            {
                Assert.That(descriptor.Surface, Is.Not.Empty, descriptor.Name);
                Assert.That(descriptor.Status, Is.EqualTo(CommandStatuses.Implemented), descriptor.Name);
                Assert.That(descriptor.PathAccess, Is.Not.Empty, descriptor.Name);
                Assert.That(descriptor.Modes, Is.Not.Null, descriptor.Name);
                Assert.That(descriptor.Modes, Is.Not.Empty, descriptor.Name);
                CollectionAssert.Contains(
                    new[] { CommandSurfaces.Public, CommandSurfaces.Technical },
                    descriptor.Surface,
                    descriptor.Name);
                CollectionAssert.Contains(
                    new[]
                    {
                        CommandPathAccess.None,
                        CommandPathAccess.Read,
                        CommandPathAccess.Write
                    },
                    descriptor.PathAccess,
                    descriptor.Name);
            }
        }

        [Test]
        public void CapabilitiesListReportsPublicCommandMetadata()
        {
            CommandEnvelope envelope = new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = "capabilities.list",
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = new JObject(),
                Options = new CommandOptions()
            };

            ActionResult<JObject> result = CoreServices.Dispatcher.Execute(envelope);

            Assert.That(result.Success, Is.True, result.Message);
            JArray commands = (JArray)result.Data["commands"];
            JObject create = commands
                .OfType<JObject>()
                .Single(item => item["name"]?.Value<string>() == "authoring.create_gameobject");
            Assert.That(create["surface"]?.Value<string>(), Is.EqualTo(CommandSurfaces.Public));
            Assert.That(create["pathAccess"]?.Value<string>(), Is.EqualTo(CommandPathAccess.Write));
            Assert.That(create["requiresConfirmation"]?.Value<bool>(), Is.True);
            Assert.That(create["permission"], Is.Not.Null);
        }

        [Test]
        public void RepeatedCommandIdReturnsStoredTerminalResult()
        {
            CommandEnvelope envelope = new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = Guid.NewGuid().ToString("D"),
                Command = "system.status",
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = new JObject(),
                Options = new CommandOptions()
            };

            ActionResult<JObject> first = CoreServices.Dispatcher.Execute(envelope);
            ActionResult<JObject> second = CoreServices.Dispatcher.Execute(envelope);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(second.Data["commandId"]?.Value<string>(), Is.EqualTo(envelope.CommandId));
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ReusedCommandIdWithDifferentPayloadReturnsConflict()
        {
            string commandId = Guid.NewGuid().ToString("D");
            CommandEnvelope firstEnvelope = new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = commandId,
                Command = "system.status",
                IssuedAtUtc = DateTimeOffset.UtcNow,
                Arguments = new JObject(),
                Options = new CommandOptions()
            };
            CommandEnvelope secondEnvelope = new CommandEnvelope
            {
                ProtocolVersion = EditorSession.ProtocolVersion,
                CommandId = commandId,
                Command = "system.commands.list",
                IssuedAtUtc = firstEnvelope.IssuedAtUtc,
                Arguments = new JObject(),
                Options = new CommandOptions()
            };

            ActionResult<JObject> first = CoreServices.Dispatcher.Execute(firstEnvelope);
            ActionResult<JObject> second = CoreServices.Dispatcher.Execute(secondEnvelope);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
            Assert.That(second.Code, Is.EqualTo(ResultCodes.IdempotencyConflict));
            Assert.That(second.Data["commandId"]?.Value<string>(), Is.EqualTo(commandId));
            Assert.That(second.Data["storedHash"]?.Value<string>(), Is.Not.Empty);
            Assert.That(second.Data["requestHash"]?.Value<string>(), Is.Not.Empty);
        }

        [Test]
        public void PublicPermissionApisAlwaysReturnObjectData()
        {
            ActionResult<JObject> granted = UnityIAPermissionsAPI.Evaluate(
                new PermissionRequest
                {
                    Capability = "context.read",
                    PathAccess = CommandPathAccess.Read
                });
            ActionResult<JObject> denied = UnityIAPermissionsAPI.Evaluate(
                new PermissionRequest
                {
                    Capability = "scene.component.write",
                    Path = "Assets/Scenes/Main.unity",
                    PathAccess = CommandPathAccess.Write
                });

            Assert.That(granted.Data, Is.Not.Null);
            Assert.That(granted.Data.Type, Is.EqualTo(JTokenType.Object));
            Assert.That(granted.Data["warnings"], Is.Not.Null);
            Assert.That(denied.Data, Is.Not.Null);
            Assert.That(denied.Data.Type, Is.EqualTo(JTokenType.Object));
            Assert.That(denied.Data["warnings"], Is.Not.Null);
        }

        [Test]
        public void BatchEntrypointExecutesCommandFileThroughDispatcher()
        {
            string commandFile = Path.Combine(
                Path.GetTempPath(),
                "UnityIA-BatchEntrypoint-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    commandFile,
                    "{" +
                    "\"protocolVersion\":\"0.1\"," +
                    "\"commandId\":\"" + Guid.NewGuid().ToString("D") + "\"," +
                    "\"command\":\"system.status\"," +
                    "\"issuedAtUtc\":\"2026-06-14T12:00:00Z\"," +
                    "\"preconditions\":{}," +
                    "\"arguments\":{}," +
                    "\"options\":{\"dryRun\":false}" +
                    "}");

                ActionResult<JObject> result =
                    UnityIABatchEntrypoint.ExecuteCommandForArgs(
                        new[] { "-unityiaCommandFile", commandFile });

                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(result.Code, Is.EqualTo(ResultCodes.Ok));
            }
            finally
            {
                if (File.Exists(commandFile))
                {
                    File.Delete(commandFile);
                }
            }
        }

        [Test]
        public void TestApiAcceptsOnlyRegisteredEditModeSuite()
        {
            ActionResult<JObject> accepted = UnityIATestAPI.RunRegisteredSuite(
                new TestRunRequest
                {
                    Suite = UnityIATestAPI.PackageEditModeSuite,
                    Mode = UnityIATestAPI.EditMode
                });
            ActionResult<JObject> unknown = UnityIATestAPI.RunRegisteredSuite(
                new TestRunRequest
                {
                    Suite = "custom.suite",
                    Mode = UnityIATestAPI.EditMode
                });
            ActionResult<JObject> playMode = UnityIATestAPI.RunRegisteredSuite(
                new TestRunRequest
                {
                    Suite = UnityIATestAPI.PackageEditModeSuite,
                    Mode = "PlayMode"
                });

            Assert.That(accepted.Success, Is.True, accepted.Message);
            Assert.That(accepted.Data["runId"]?.Value<string>(), Is.Not.Empty);
            Assert.That(accepted.Data["suite"]?.Value<string>(),
                Is.EqualTo(UnityIATestAPI.PackageEditModeSuite));
            Assert.That(unknown.Success, Is.False);
            Assert.That(unknown.Code, Is.EqualTo(ResultCodes.TargetNotFound));
            Assert.That(playMode.Success, Is.False);
            Assert.That(playMode.Code, Is.EqualTo(ResultCodes.InvalidCommand));
        }

        private static void AssertPublicDescriptor(
            IReadOnlyDictionary<string, CommandDescriptorDto> descriptors,
            string name,
            bool isMutation,
            string capability,
            string pathAccess)
        {
            Assert.That(descriptors.ContainsKey(name), Is.True);
            Assert.That(descriptors[name].IsMutation, Is.EqualTo(isMutation));
            Assert.That(descriptors[name].Capability, Is.EqualTo(capability));
            Assert.That(descriptors[name].Surface, Is.EqualTo(CommandSurfaces.Public));
            Assert.That(descriptors[name].PathAccess, Is.EqualTo(pathAccess));
            Assert.That(descriptors[name].Capability, Is.Not.EqualTo("scene.modify"));
        }
    }
}
