using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    public sealed class ConfirmationService
    {
        private static readonly TimeSpan DefaultApprovalLifetime = TimeSpan.FromMinutes(5);
        private readonly Dictionary<string, ConfirmationRecord> decisions =
            new Dictionary<string, ConfirmationRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingConfirmation> pending =
            new Dictionary<string, PendingConfirmation>(StringComparer.Ordinal);

        public ConfirmationCheck Evaluate(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            string canonicalHash,
            PermissionDecision permission,
            string targetPath,
            string authorizationMode)
        {
            if (envelope == null || descriptor == null ||
                !descriptor.IsMutation ||
                !descriptor.RequiresConfirmation ||
                string.Equals(authorizationMode, AuthorizationModes.ConfirmActions,
                    StringComparison.Ordinal) == false)
            {
                return ConfirmationCheck.NotRequired();
            }

            string commandId = envelope.CommandId ?? string.Empty;
            if (decisions.TryGetValue(commandId, out ConfirmationRecord record))
            {
                if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    decisions.Remove(commandId);
                    return RequireConfirmation(
                        envelope,
                        descriptor,
                        canonicalHash,
                        permission,
                        targetPath,
                        "The previous confirmation expired.");
                }

                if (!string.Equals(record.CanonicalHash, canonicalHash, StringComparison.Ordinal))
                {
                    decisions.Remove(commandId);
                    return ConfirmationCheck.Denied(
                        "Confirmation does not match the command payload.");
                }

                decisions.Remove(commandId);
                pending.Remove(commandId);
                return record.Approved
                    ? ConfirmationCheck.Approved(record.Reason)
                    : ConfirmationCheck.Denied(record.Reason);
            }

            return RequireConfirmation(
                envelope,
                descriptor,
                canonicalHash,
                permission,
                targetPath,
                "The mutation requires explicit confirmation.");
        }

        public ActionResult<JObject> Approve(
            CommandEnvelope envelope,
            bool approved,
            string reason = null,
            DateTimeOffset? expiresAtUtc = null)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.CommandId))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "A command envelope with commandId is required.");
            }

            string hash = CommandJson.ComputeCanonicalHash(envelope);
            decisions[envelope.CommandId] = new ConfirmationRecord(
                hash,
                approved,
                string.IsNullOrWhiteSpace(reason)
                    ? (approved ? "Approved." : "Denied.")
                    : reason,
                expiresAtUtc ?? DateTimeOffset.UtcNow.Add(DefaultApprovalLifetime));
            pending.Remove(envelope.CommandId);
            return Results.Ok(
                approved ? "Command confirmation approved." : "Command confirmation denied.",
                new JObject
                {
                    ["commandId"] = envelope.CommandId,
                    ["command"] = envelope.Command,
                    ["approved"] = approved,
                    ["expiresAtUtc"] = decisions[envelope.CommandId].ExpiresAtUtc
                });
        }

        public ActionResult<JObject> ApprovePending(
            string commandId,
            bool approved,
            string reason = null,
            DateTimeOffset? expiresAtUtc = null)
        {
            if (string.IsNullOrWhiteSpace(commandId) ||
                !pending.TryGetValue(commandId, out PendingConfirmation request))
            {
                return Results.Error(
                    ResultCodes.TargetNotFound,
                    "Pending confirmation was not found.");
            }

            decisions[commandId] = new ConfirmationRecord(
                request.CanonicalHash,
                approved,
                string.IsNullOrWhiteSpace(reason)
                    ? (approved ? "Approved." : "Denied.")
                    : reason,
                expiresAtUtc ?? DateTimeOffset.UtcNow.Add(DefaultApprovalLifetime));
            pending.Remove(commandId);
            return Results.Ok(
                approved ? "Pending confirmation approved." : "Pending confirmation denied.",
                request.ToData(approved));
        }

        public IReadOnlyList<PendingConfirmation> ListPending()
        {
            return pending.Values
                .OrderBy(item => item.CreatedAtUtc)
                .ToArray();
        }

        private ConfirmationCheck RequireConfirmation(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            string canonicalHash,
            PermissionDecision permission,
            string targetPath,
            string reason)
        {
            PendingConfirmation request = new PendingConfirmation(
                envelope.CommandId,
                envelope.Command,
                descriptor.Capability,
                targetPath,
                EffectSummary(descriptor, targetPath),
                canonicalHash,
                permission == null ? null : permission.AuthorizationMode,
                DateTimeOffset.UtcNow);
            pending[envelope.CommandId] = request;
            return ConfirmationCheck.Required(reason, request);
        }

        private static string EffectSummary(CommandDescriptor descriptor, string targetPath)
        {
            string target = string.IsNullOrWhiteSpace(targetPath)
                ? "the current Editor context"
                : targetPath;
            return "Execute " + descriptor.Name + " against " + target + ".";
        }

        private sealed class ConfirmationRecord
        {
            public ConfirmationRecord(
                string canonicalHash,
                bool approved,
                string reason,
                DateTimeOffset expiresAtUtc)
            {
                CanonicalHash = canonicalHash ?? string.Empty;
                Approved = approved;
                Reason = reason ?? string.Empty;
                ExpiresAtUtc = expiresAtUtc;
            }

            public string CanonicalHash { get; }
            public bool Approved { get; }
            public string Reason { get; }
            public DateTimeOffset ExpiresAtUtc { get; }
        }
    }

    public sealed class PendingConfirmation
    {
        public PendingConfirmation(
            string commandId,
            string command,
            string capability,
            string targetPath,
            string effect,
            string canonicalHash,
            string authorizationMode,
            DateTimeOffset createdAtUtc)
        {
            CommandId = commandId ?? string.Empty;
            Command = command ?? string.Empty;
            Capability = capability ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
            Effect = effect ?? string.Empty;
            CanonicalHash = canonicalHash ?? string.Empty;
            AuthorizationMode = authorizationMode ?? AuthorizationModes.ConfirmActions;
            CreatedAtUtc = createdAtUtc;
        }

        public string CommandId { get; }
        public string Command { get; }
        public string Capability { get; }
        public string TargetPath { get; }
        public string Effect { get; }
        public string CanonicalHash { get; }
        public string AuthorizationMode { get; }
        public DateTimeOffset CreatedAtUtc { get; }

        public JObject ToData(bool? approved = null)
        {
            JObject data = new JObject
            {
                ["commandId"] = CommandId,
                ["command"] = Command,
                ["capability"] = Capability,
                ["target"] = TargetPath,
                ["effect"] = Effect,
                ["authorizationMode"] = AuthorizationMode,
                ["createdAtUtc"] = CreatedAtUtc
            };
            if (approved.HasValue)
            {
                data["approved"] = approved.Value;
            }

            return data;
        }
    }

    public sealed class ConfirmationCheck
    {
        private ConfirmationCheck(string status, string reason, PendingConfirmation pending)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            Pending = pending;
        }

        public string Status { get; }
        public string Reason { get; }
        public PendingConfirmation Pending { get; }
        public bool IsApproved => Status == "approved";
        public bool IsRequired => Status == "required";
        public bool IsDenied => Status == "denied";

        public static ConfirmationCheck NotRequired()
        {
            return new ConfirmationCheck("not_required", string.Empty, null);
        }

        public static ConfirmationCheck Required(string reason, PendingConfirmation pending)
        {
            return new ConfirmationCheck("required", reason, pending);
        }

        public static ConfirmationCheck Approved(string reason)
        {
            return new ConfirmationCheck("approved", reason, null);
        }

        public static ConfirmationCheck Denied(string reason)
        {
            return new ConfirmationCheck("denied", reason, null);
        }
    }
}
