using System;
using System.IO;
using NUnit.Framework;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Tests
{
    public sealed class PermissionTests
    {
        [Test]
        public void UnsafeAbsolutePathIsDenied()
        {
            PermissionDecision decision = CoreServices.Permissions.Evaluate(
                new PermissionRequest
                {
                    Capability = "scene.read",
                    Path = "C:/outside/Scene.unity"
                });

            Assert.That(decision.Allowed, Is.False);
        }

        [Test]
        public void MissingPolicyDoesNotImplicitlyGrantMutation()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityIA-PermissionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            try
            {
                PermissionService permissions = new PermissionService(projectRoot);
                EffectivePolicy policy = permissions.GetEffectivePolicy();
                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = "scene.modify",
                        Path = "Assets/Scenes/Main.unity"
                    });

                Assert.That(policy.Source, Is.EqualTo("default"));
                Assert.That(decision.Allowed, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
    }
}
