using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    public sealed class CommandDispatcher
    {
        private readonly CommandRegistry registry;
        private readonly PermissionService permissions;
        private readonly AuditService audit;
        private readonly Dictionary<string, ActionResult<JObject>> idempotency =
            new Dictionary<string, ActionResult<JObject>>(StringComparer.Ordinal);

        public CommandDispatcher(
            CommandRegistry registry,
            PermissionService permissions,
            AuditService audit)
        {
            this.registry = registry;
            this.permissions = permissions;
            this.audit = audit;
        }

        public ActionResult<JObject> ExecuteJson(string json)
        {
            CommandEnvelope envelope;
            string error;
            if (!CommandJson.TryDeserialize(json, out envelope, out error))
            {
                return Results.Error(ResultCodes.InvalidJson, error);
            }

            return Execute(envelope);
        }

        public ActionResult<JObject> Validate(CommandEnvelope envelope)
        {
            ICommandHandler handler;
            ActionResult<JObject> validation = ValidateEnvelope(envelope, out handler);
            if (!validation.Success)
            {
                return Decorate(validation, envelope);
            }

            CommandExecutionContext context = new CommandExecutionContext(handler.Descriptor);
            ActionResult<JObject> state = EditorStateValidator.Validate(
                envelope.Preconditions,
                handler.Descriptor.IsMutation);
            if (!state.Success)
            {
                return Decorate(state, envelope);
            }

            PermissionDecision decision = permissions.Evaluate(
                new PermissionRequest
                {
                    Capability = handler.Descriptor.Capability,
                    Path = ExtractPermissionPath(envelope)
                });
            if (!string.IsNullOrEmpty(handler.Descriptor.Capability) && !decision.Allowed)
            {
                return Decorate(
                    Results.Error(
                        ResultCodes.PermissionDenied,
                        decision.Reason,
                        JObject.FromObject(decision)),
                    envelope);
            }

            ActionResult<JObject> handlerValidation = handler.Validate(envelope, context);
            return Decorate(handlerValidation, envelope);
        }

        public ActionResult<JObject> Execute(CommandEnvelope envelope)
        {
            if (envelope != null &&
                !string.IsNullOrWhiteSpace(envelope.CommandId) &&
                idempotency.TryGetValue(envelope.CommandId, out ActionResult<JObject> previous))
            {
                return previous;
            }

            ICommandHandler handler;
            ActionResult<JObject> baseValidation = ValidateEnvelope(envelope, out handler);
            if (!baseValidation.Success)
            {
                return CompleteWithoutHandler(envelope, baseValidation);
            }

            CommandExecutionContext context = new CommandExecutionContext(handler.Descriptor);
            ActionResult<JObject> state = EditorStateValidator.Validate(
                envelope.Preconditions,
                handler.Descriptor.IsMutation);
            if (!state.Success)
            {
                return Complete(envelope, handler.Descriptor, state);
            }

            PermissionDecision decision = permissions.Evaluate(
                new PermissionRequest
                {
                    Capability = handler.Descriptor.Capability,
                    Path = ExtractPermissionPath(envelope)
                });
            if (!string.IsNullOrEmpty(handler.Descriptor.Capability) && !decision.Allowed)
            {
                return Complete(
                    envelope,
                    handler.Descriptor,
                    Results.Error(
                        ResultCodes.PermissionDenied,
                        decision.Reason,
                        JObject.FromObject(decision)));
            }

            ActionResult<JObject> handlerValidation = handler.Validate(envelope, context);
            if (!handlerValidation.Success)
            {
                return Complete(envelope, handler.Descriptor, handlerValidation);
            }

            bool mutates = handler.Descriptor.IsMutation && !envelope.Options.DryRun;
            string auditError;
            bool requestAudited = audit.TryWriteRequest(
                envelope,
                handler.Descriptor,
                out auditError);
            string requestAuditError = requestAudited ? null : auditError;
            if (!requestAudited && mutates)
            {
                return Store(
                    envelope,
                    Decorate(
                        Results.Error(
                            ResultCodes.AuditUnavailable,
                            "The mutation was not executed because audit is unavailable: " +
                            auditError),
                        envelope));
            }

            if (envelope.Options.DryRun)
            {
                ActionResult<JObject> dryRun = Results.Ok(
                    "Command validation and permission checks passed.",
                    new JObject { ["dryRun"] = true });
                if (!string.IsNullOrEmpty(requestAuditError))
                {
                    Results.AddWarning(dryRun, "Audit is degraded: " + requestAuditError);
                }

                dryRun = Decorate(dryRun, envelope);
                if (!audit.TryWriteResult(
                        envelope,
                        handler.Descriptor,
                        dryRun,
                        out auditError))
                {
                    Results.AddWarning(dryRun, "Audit is degraded: " + auditError);
                }

                return Store(envelope, dryRun);
            }

            int undoGroup = -1;
            if (mutates)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("UnityIA: " + handler.Descriptor.Name);
            }

            ActionResult<JObject> result;
            try
            {
                result = handler.Execute(envelope, context) ??
                         Results.Error(
                             ResultCodes.InternalError,
                             "The command handler returned no result.");
            }
            catch (Exception exception)
            {
                result = Results.Error(
                    ResultCodes.UnityOperationFailed,
                    "Unity operation failed: " + exception.Message);
            }

            if (mutates && !result.Success)
            {
                Undo.RevertAllDownToGroup(undoGroup);
            }

            if (mutates && result.Success)
            {
                EditorStateTracker.Advance();
            }

            result = Decorate(result, envelope);
            if (!string.IsNullOrEmpty(requestAuditError))
            {
                Results.AddWarning(result, "Audit is degraded: " + requestAuditError);
            }

            if (!audit.TryWriteResult(envelope, handler.Descriptor, result, out auditError))
            {
                if (mutates)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    result = Decorate(
                        Results.Error(
                            ResultCodes.AuditUnavailable,
                            "The command result could not be audited: " + auditError),
                        envelope);
                }
                else
                {
                    Results.AddWarning(result, "Audit is degraded: " + auditError);
                }
            }
            else if (mutates && result.Success)
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return Store(envelope, result);
        }

        private ActionResult<JObject> ValidateEnvelope(
            CommandEnvelope envelope,
            out ICommandHandler handler)
        {
            handler = null;
            if (envelope == null)
            {
                return Results.Error(ResultCodes.InvalidCommand, "Command envelope is required.");
            }

            if (!string.Equals(
                    envelope.ProtocolVersion,
                    EditorSession.ProtocolVersion,
                    StringComparison.Ordinal))
            {
                return Results.Error(
                    ResultCodes.UnsupportedProtocol,
                    "Only protocol version " + EditorSession.ProtocolVersion + " is supported.");
            }

            Guid commandId;
            if (!Guid.TryParse(envelope.CommandId, out commandId))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "commandId must be a UUID.");
            }

            if (string.IsNullOrWhiteSpace(envelope.Command) ||
                !registry.TryGet(envelope.Command, out handler))
            {
                return Results.Error(
                    ResultCodes.InvalidCommand,
                    "The command is not registered.");
            }

            envelope.Arguments = envelope.Arguments ?? new JObject();
            envelope.Options = envelope.Options ?? new CommandOptions();
            if (envelope.IssuedAtUtc == default(DateTimeOffset))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "issuedAtUtc is required.");
            }

            return Results.Ok("Envelope is valid.");
        }

        private ActionResult<JObject> CompleteWithoutHandler(
            CommandEnvelope envelope,
            ActionResult<JObject> result)
        {
            return Store(envelope, Decorate(result, envelope));
        }

        private ActionResult<JObject> Complete(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            ActionResult<JObject> result)
        {
            result = Decorate(result, envelope);
            string auditError;
            if (!audit.TryWriteRequest(envelope, descriptor, out auditError) ||
                !audit.TryWriteResult(envelope, descriptor, result, out auditError))
            {
                Results.AddWarning(result, "Audit is degraded: " + auditError);
            }

            return Store(envelope, result);
        }

        private ActionResult<JObject> Store(
            CommandEnvelope envelope,
            ActionResult<JObject> result)
        {
            if (envelope != null && !string.IsNullOrWhiteSpace(envelope.CommandId))
            {
                idempotency[envelope.CommandId] = result;
            }

            return result;
        }

        private static ActionResult<JObject> Decorate(
            ActionResult<JObject> result,
            CommandEnvelope envelope)
        {
            result = result ?? Results.Error(ResultCodes.InternalError, "No result was produced.");
            result.Data = result.Data ?? new JObject();
            result.Data["commandId"] = envelope == null ? null : envelope.CommandId;
            result.Data["contextVersion"] = EditorStateTracker.ContextVersion;
            if (result.Data["warnings"] == null)
            {
                result.Data["warnings"] = new JArray();
            }

            return result;
        }

        private static string ExtractPermissionPath(CommandEnvelope envelope)
        {
            JToken path = envelope.Arguments["scenePath"];
            if (path != null && path.Type == JTokenType.String)
            {
                return path.Value<string>();
            }

            JToken targetPath = envelope.Arguments.SelectToken("target.scenePath");
            if (targetPath != null && targetPath.Type == JTokenType.String)
            {
                return targetPath.Value<string>();
            }

            return envelope.Preconditions == null
                ? null
                : envelope.Preconditions.ActiveScenePath;
        }
    }
}
