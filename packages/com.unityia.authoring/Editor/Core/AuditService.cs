using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    public sealed class AuditService
    {
        private bool mutationsBlocked;

        public bool TryWriteRequest(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            PermissionDecision permission,
            out string error)
        {
            JObject entry = BaseEntry("request", envelope);
            entry["isMutation"] = descriptor.IsMutation;
            entry["capability"] = descriptor.Capability;
            entry["pathAccess"] = descriptor.PathAccess;
            entry["argumentsSha256"] = HashArguments(envelope.Arguments);
            if (permission != null)
            {
                entry["permissionAllowed"] = permission.Allowed;
                entry["permissionReason"] = permission.Reason;
                entry["permissionPath"] = permission.Path;
                entry["authorizationMode"] = permission.AuthorizationMode;
                entry["requiresConfirmation"] = permission.RequiresConfirmation;
            }

            return TryAppend(entry, descriptor.IsMutation && !envelope.Options.DryRun, out error);
        }

        public bool TryWriteResult(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            ActionResult<JObject> result,
            out string error)
        {
            JObject entry = BaseEntry("result", envelope);
            entry["isMutation"] = descriptor.IsMutation;
            entry["success"] = result.Success;
            entry["code"] = result.Code;
            return TryAppend(entry, descriptor.IsMutation && !envelope.Options.DryRun, out error);
        }

        public bool TryWriteConfirmation(
            CommandEnvelope envelope,
            CommandDescriptor descriptor,
            string decision,
            string reason,
            string canonicalHash,
            out string error)
        {
            JObject entry = BaseEntry("confirmation", envelope);
            entry["isMutation"] = descriptor.IsMutation;
            entry["capability"] = descriptor.Capability;
            entry["decision"] = decision;
            entry["reason"] = reason;
            entry["canonicalHash"] = canonicalHash;
            return TryAppend(entry, descriptor.IsMutation && !envelope.Options.DryRun, out error);
        }

        private bool TryAppend(JObject entry, bool mutation, out string error)
        {
            error = null;
            if (mutation && mutationsBlocked)
            {
                error = "Mutation auditing is blocked after a previous audit failure.";
                return false;
            }

            try
            {
                string directory = Path.Combine(ProjectPaths.ProjectRoot, ".unityia", "audit");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(
                    directory,
                    DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
                File.AppendAllText(
                    file,
                    entry.ToString(Formatting.None) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception)
            {
                if (mutation)
                {
                    mutationsBlocked = true;
                }

                error = exception.Message;
                return false;
            }
        }

        private static JObject BaseEntry(string phase, CommandEnvelope envelope)
        {
            return new JObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["phase"] = phase,
                ["protocolVersion"] = envelope.ProtocolVersion,
                ["sessionId"] = EditorSession.SessionId,
                ["commandId"] = envelope.CommandId,
                ["command"] = envelope.Command
            };
        }

        private static string HashArguments(JObject arguments)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                (arguments ?? new JObject()).ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
