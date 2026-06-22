using System;
using System.IO;
using Newtonsoft.Json;
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
                    Capability = "context.read",
                    Path = "C:/outside/Scene.unity",
                    PathAccess = CommandPathAccess.Read
                });

            Assert.That(decision.Allowed, Is.False);
        }

        [Test]
        public void MissingPolicyAllowsDocumentedReadInsideAssets()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                PermissionService permissions = new PermissionService(projectRoot);

                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = "context.read",
                        Path = "Assets/Scenes/Main.unity",
                        PathAccess = CommandPathAccess.Read
                    });

                Assert.That(decision.Allowed, Is.True, decision.Reason);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void MissingPolicyDoesNotImplicitlyGrantMutation()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                PermissionService permissions = new PermissionService(projectRoot);
                EffectivePolicy policy = permissions.GetEffectivePolicy();
                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = "scene.gameobject.create",
                        Path = "Assets/Scenes/Main.unity",
                        PathAccess = CommandPathAccess.Write
                    });

                Assert.That(policy.Source, Is.EqualTo("default"));
                Assert.That(policy.AuthorizationMode, Is.EqualTo(AuthorizationModes.ConfirmActions));
                CollectionAssert.AreEquivalent(
                    new[] { "capabilities.read", "context.read" },
                    policy.AllowedCapabilities);
                Assert.That(decision.Allowed, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [TestCase("scene.gameobject.create")]
        [TestCase("scene.component.add")]
        [TestCase("scene.component.write")]
        [TestCase("scene.save")]
        public void WriteStyleCapabilitiesDoNotReuseReadPaths(string capability)
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WritePolicy(
                    projectRoot,
                    new
                    {
                        version = EditorSession.ProtocolVersion,
                        authorizationMode = AuthorizationModes.ConfirmActions,
                        allow = new[] { capability },
                        paths = new
                        {
                            read = new[] { "Assets/**" },
                            write = Array.Empty<string>()
                        }
                    });

                PermissionService permissions = new PermissionService(projectRoot);
                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = capability,
                        Path = "Assets/Scenes/Main.unity",
                        PathAccess = CommandPathAccess.Write
                    });

                Assert.That(decision.Allowed, Is.False, decision.Reason);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void ExplicitReadPathAccessDoesNotGrantWritePath()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WritePolicy(
                    projectRoot,
                    new
                    {
                        version = EditorSession.ProtocolVersion,
                        authorizationMode = AuthorizationModes.ConfirmActions,
                        allow = new[] { "scene.component.write" },
                        paths = new
                        {
                            read = new[] { "Assets/**" },
                            write = Array.Empty<string>()
                        }
                    });

                PermissionService permissions = new PermissionService(projectRoot);
                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = "scene.component.write",
                        Path = "Assets/Scenes/Main.unity",
                        PathAccess = CommandPathAccess.Write
                    });

                Assert.That(decision.Allowed, Is.False, decision.Reason);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void FullAccessPolicyIsRejected()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WritePolicy(
                    projectRoot,
                    new
                    {
                        version = EditorSession.ProtocolVersion,
                        authorizationMode = AuthorizationModes.FullAccess,
                        allow = new[] { "context.read" },
                        paths = new
                        {
                            read = new[] { "Assets/**" },
                            write = Array.Empty<string>()
                        }
                    });

                PermissionService permissions = new PermissionService(projectRoot);
                PermissionDecision decision = permissions.Evaluate(
                    new PermissionRequest
                    {
                        Capability = "context.read",
                        Path = "Assets/Scenes/Main.unity",
                        PathAccess = CommandPathAccess.Read
                    });

                Assert.That(decision.Allowed, Is.False);
                Assert.That(decision.Reason, Does.Contain("full_access"));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        private static string CreateProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityIA-PermissionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, ".unityia"));
            return projectRoot;
        }

        private static void WritePolicy(string projectRoot, object policy)
        {
            File.WriteAllText(
                Path.Combine(projectRoot, ".unityia", "policy.json"),
                JsonConvert.SerializeObject(policy, Formatting.Indented));
        }
    }
}
