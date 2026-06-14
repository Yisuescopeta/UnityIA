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
        public SystemStatusHandler() : base("system.status", false, string.Empty)
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
        public CommandsListHandler() : base("system.commands.list", false, string.Empty)
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

    internal sealed class ValidateCommandHandler : CommandHandler<ValidateCommandArguments>
    {
        public ValidateCommandHandler()
            : base("validation.command.validate", false, string.Empty)
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
            : base("permissions.explain", false, string.Empty)
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

