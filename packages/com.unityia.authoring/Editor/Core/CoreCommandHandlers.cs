using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    internal sealed class SystemStatusHandler : CommandHandler<EmptyArguments>
    {
        public SystemStatusHandler()
            : base(
                "system.status",
                false,
                string.Empty,
                CommandSurfaces.Technical,
                CommandPathAccess.None,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Execute(
            EmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return Results.Ok(
                "UnityIA is ready.",
                new JObject
                {
                    ["protocolVersion"] = EditorSession.ProtocolVersion,
                    ["sessionId"] = EditorSession.SessionId,
                    ["contextVersion"] = EditorStateTracker.ContextVersion,
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = ProjectPaths.ProjectRoot,
                    ["activeScenePath"] = ProjectPaths.NormalizeUnityPath(activeScene.path),
                    ["isPlaying"] = EditorApplication.isPlaying,
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["isUpdating"] = EditorApplication.isUpdating
                });
        }
    }

    internal sealed class CommandsListHandler : CommandHandler<EmptyArguments>
    {
        public CommandsListHandler()
            : base(
                "system.commands.list",
                false,
                "capabilities.read",
                CommandSurfaces.Technical,
                CommandPathAccess.None,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Execute(
            EmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return Results.Ok(
                "Registered commands.",
                new JObject
                {
                    ["commands"] = JArray.FromObject(CoreServices.Registry.List())
                });
        }
    }

    internal sealed class CapabilitiesListHandler : CommandHandler<EmptyArguments>
    {
        public CapabilitiesListHandler()
            : base(
                "capabilities.list",
                false,
                "capabilities.read",
                CommandSurfaces.Public,
                CommandPathAccess.None,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Execute(
            EmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            EffectivePolicy policy = CoreServices.Permissions.GetEffectivePolicy();
            JArray commands = new JArray();
            foreach (CommandDescriptor descriptor in CoreServices.Registry.ListDescriptors())
            {
                PermissionDecision permission = PermissionFor(descriptor, policy);
                bool requiresConfirmation = descriptor.IsMutation &&
                    descriptor.RequiresConfirmation &&
                    string.Equals(
                        policy.AuthorizationMode,
                        AuthorizationModes.ConfirmActions,
                        StringComparison.Ordinal);
                permission.RequiresConfirmation = permission.Allowed && requiresConfirmation;

                commands.Add(
                    new JObject
                    {
                        ["name"] = descriptor.Name,
                        ["surface"] = descriptor.Surface,
                        ["status"] = descriptor.Status,
                        ["version"] = EditorSession.ProtocolVersion,
                        ["isMutation"] = descriptor.IsMutation,
                        ["capability"] = descriptor.Capability,
                        ["pathAccess"] = descriptor.PathAccess,
                        ["modes"] = JArray.FromObject(descriptor.Modes),
                        ["requiresConfirmation"] = requiresConfirmation,
                        ["permission"] = JObject.FromObject(permission),
                        ["restrictions"] = Restrictions(descriptor, requiresConfirmation)
                    });
            }

            return Results.Ok(
                "Registered capabilities.",
                new JObject
                {
                    ["protocolVersion"] = EditorSession.ProtocolVersion,
                    ["sessionId"] = EditorSession.SessionId,
                    ["executionMode"] = EditorSession.ExecutionMode,
                    ["authorizationMode"] = policy.AuthorizationMode,
                    ["policySource"] = policy.Source,
                    ["commands"] = commands
                });
        }

        private static PermissionDecision PermissionFor(
            CommandDescriptor descriptor,
            EffectivePolicy policy)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Capability))
            {
                return new PermissionDecision
                {
                    Allowed = true,
                    Capability = descriptor.Capability,
                    PathAccess = descriptor.PathAccess,
                    AuthorizationMode = policy.AuthorizationMode,
                    Reason = "Command does not require a project capability."
                };
            }

            return CoreServices.Permissions.EvaluateCapability(
                new PermissionRequest
                {
                    Capability = descriptor.Capability,
                    PathAccess = descriptor.PathAccess
                });
        }

        private static JArray Restrictions(
            CommandDescriptor descriptor,
            bool requiresConfirmation)
        {
            JArray restrictions = new JArray();
            if (descriptor.Surface == CommandSurfaces.Technical)
            {
                restrictions.Add("Technical command; not part of the public authoring catalog.");
            }

            if (descriptor.PathAccess == CommandPathAccess.Read)
            {
                restrictions.Add("Requires an authorized read path when a content path is used.");
            }
            else if (descriptor.PathAccess == CommandPathAccess.Write)
            {
                restrictions.Add("Requires an authorized write path when a content path is used.");
            }

            if (requiresConfirmation)
            {
                restrictions.Add("Mutations require explicit confirm_actions approval.");
            }

            return restrictions;
        }
    }

    internal sealed class ValidateActiveSceneHandler :
        CommandHandler<ValidateActiveSceneArguments>
    {
        public ValidateActiveSceneHandler()
            : base(
                "validate.active_scene",
                false,
                "validation.scene.run",
                CommandSurfaces.Public,
                CommandPathAccess.Read,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Validate(
            ValidateActiveSceneArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return ActiveSceneValidationService.ValidateArguments(arguments);
        }

        protected override ActionResult<JObject> Execute(
            ValidateActiveSceneArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return ActiveSceneValidationService.Validate(arguments);
        }
    }

    internal sealed class ValidateCommandHandler : CommandHandler<ValidateCommandArguments>
    {
        public ValidateCommandHandler()
            : base(
                "validation.command.validate",
                false,
                string.Empty,
                CommandSurfaces.Technical,
                CommandPathAccess.None,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Validate(
            ValidateCommandArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            if (arguments == null || arguments.Envelope == null)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "A nested command envelope is required.");
            }

            return Results.Ok("Validation request is valid.");
        }

        protected override ActionResult<JObject> Execute(
            ValidateCommandArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            CommandEnvelope nested;
            string error;
            if (!CommandJson.TryDeserialize(
                    arguments.Envelope.ToString(),
                    out nested,
                    out error))
            {
                return Results.Error(ResultCodes.InvalidJson, error);
            }

            ActionResult<JObject> result = CoreServices.Dispatcher.Validate(nested);
            return Results.Ok(
                "Nested command validation completed.",
                new JObject
                {
                    ["validation"] = JObject.FromObject(result)
                });
        }
    }

    internal sealed class ExplainPermissionHandler : CommandHandler<PermissionRequest>
    {
        public ExplainPermissionHandler()
            : base(
                "permissions.explain",
                false,
                string.Empty,
                CommandSurfaces.Technical,
                CommandPathAccess.None,
                new[] { CommandExecutionModes.Live, CommandExecutionModes.Batch },
                false)
        {
        }

        protected override ActionResult<JObject> Validate(
            PermissionRequest arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return arguments == null || string.IsNullOrWhiteSpace(arguments.Capability)
                ? Results.Error(ResultCodes.ValidationFailed, "capability is required.")
                : Results.Ok("Permission request is valid.");
        }

        protected override ActionResult<JObject> Execute(
            PermissionRequest arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            PermissionDecision decision = CoreServices.Permissions.Evaluate(arguments);
            return Results.Ok(
                "Permission decision calculated.",
                JObject.FromObject(decision));
        }
    }
}
