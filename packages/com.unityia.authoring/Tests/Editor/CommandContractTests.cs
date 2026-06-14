using System;
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
        public void RegistryContainsAllV01Commands()
        {
            string[] expected =
            {
                "system.status",
                "system.commands.list",
                "context.get",
                "scene.list-open",
                "scene.hierarchy.get",
                "scene.object.get",
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
                    Capability = "context.read"
                });
            ActionResult<JObject> denied = UnityIAPermissionsAPI.Evaluate(
                new PermissionRequest
                {
                    Capability = "scene.component.write",
                    Path = "Assets/Scenes/Main.unity"
                });

            Assert.That(granted.Data, Is.Not.Null);
            Assert.That(granted.Data.Type, Is.EqualTo(JTokenType.Object));
            Assert.That(granted.Data["warnings"], Is.Not.Null);
            Assert.That(denied.Data, Is.Not.Null);
            Assert.That(denied.Data.Type, Is.EqualTo(JTokenType.Object));
            Assert.That(denied.Data["warnings"], Is.Not.Null);
        }
    }
}
