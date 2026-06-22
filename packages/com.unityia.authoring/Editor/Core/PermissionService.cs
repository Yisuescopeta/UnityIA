using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    internal sealed class PolicyFile
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("authorizationMode")]
        public string AuthorizationMode { get; set; }

        [JsonProperty("allow")]
        public List<string> Allow { get; set; } = new List<string>();

        [JsonProperty("paths")]
        public PolicyPaths Paths { get; set; } = new PolicyPaths();
    }

    internal sealed class PolicyPaths
    {
        [JsonProperty("read")]
        public List<string> Read { get; set; } = new List<string>();

        [JsonProperty("write")]
        public List<string> Write { get; set; } = new List<string>();
    }

    public sealed class PermissionService
    {
        private static readonly HashSet<string> DefaultReadCapabilities =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "capabilities.read",
                "context.read"
            };

        private readonly string projectRoot;

        public PermissionService(string projectRoot = null)
        {
            this.projectRoot = string.IsNullOrWhiteSpace(projectRoot)
                ? ProjectPaths.ProjectRoot
                : Path.GetFullPath(projectRoot);
        }

        public PermissionDecision Evaluate(PermissionRequest request)
        {
            return Evaluate(request, true);
        }

        public PermissionDecision EvaluateCapability(PermissionRequest request)
        {
            return Evaluate(request, false);
        }

        private PermissionDecision Evaluate(PermissionRequest request, bool checkPath)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Capability))
            {
                return Decision(false, request, "A capability is required.");
            }

            PolicyFile policy;
            string source;
            string error;
            if (!TryLoadPolicy(out policy, out source, out error))
            {
                return Decision(false, request, error);
            }

            string pathAccess;
            if (!TryNormalizePathAccess(request.PathAccess, out pathAccess, out error))
            {
                return Decision(false, request, error, policy.AuthorizationMode);
            }

            bool allowed = source == "default"
                ? DefaultReadCapabilities.Contains(request.Capability)
                : policy.Allow.Contains(request.Capability, StringComparer.Ordinal);

            if (!allowed)
            {
                return Decision(
                    false,
                    request,
                    "Capability is not allowed by the effective policy.",
                    policy.AuthorizationMode);
            }

            if (checkPath && !string.IsNullOrWhiteSpace(request.Path))
            {
                if (pathAccess == CommandPathAccess.None)
                {
                    return Decision(
                        false,
                        request,
                        "A content path requires explicit read or write path access metadata.",
                        policy.AuthorizationMode);
                }

                string normalized;
                if (!TryNormalizeAssetsPath(request.Path, out normalized, out error))
                {
                    return Decision(false, request, error, policy.AuthorizationMode);
                }

                IReadOnlyList<string> patterns = pathAccess == CommandPathAccess.Write
                    ? policy.Paths.Write
                    : policy.Paths.Read;
                if (!MatchesAny(normalized, patterns))
                {
                    return Decision(
                        false,
                        new PermissionRequest
                        {
                            Capability = request.Capability,
                            Path = normalized,
                            PathAccess = pathAccess
                        },
                        "Path is not allowed by the effective policy.",
                        policy.AuthorizationMode);
                }

                request = new PermissionRequest
                {
                    Capability = request.Capability,
                    Path = normalized,
                    PathAccess = pathAccess
                };
            }
            else
            {
                request = new PermissionRequest
                {
                    Capability = request.Capability,
                    Path = request.Path,
                    PathAccess = pathAccess
                };
            }

            return Decision(
                true,
                request,
                checkPath && !string.IsNullOrWhiteSpace(request.Path)
                    ? "Capability and path are allowed."
                    : "Capability is allowed by the effective policy.",
                policy.AuthorizationMode);
        }

        public EffectivePolicy GetEffectivePolicy()
        {
            PolicyFile policy;
            string source;
            string error;
            if (!TryLoadPolicy(out policy, out source, out error))
            {
                return new EffectivePolicy
                {
                    Version = EditorSession.ProtocolVersion,
                    Source = "invalid: " + error,
                    AuthorizationMode = null
                };
            }

            return new EffectivePolicy
            {
                Version = string.IsNullOrWhiteSpace(policy.Version)
                    ? EditorSession.ProtocolVersion
                    : policy.Version,
                Source = source,
                AuthorizationMode = policy.AuthorizationMode,
                AllowedCapabilities = source == "default"
                    ? DefaultReadCapabilities.OrderBy(value => value).ToList()
                    : policy.Allow.OrderBy(value => value).ToList(),
                ReadPaths = policy.Paths.Read.ToList(),
                WritePaths = policy.Paths.Write.ToList()
            };
        }

        private bool TryLoadPolicy(
            out PolicyFile policy,
            out string source,
            out string error)
        {
            string path = Path.Combine(projectRoot, ".unityia", "policy.json");
            error = null;

            if (!File.Exists(path))
            {
                source = "default";
                policy = new PolicyFile
                {
                    Version = EditorSession.ProtocolVersion,
                    AuthorizationMode = AuthorizationModes.ConfirmActions,
                    Allow = DefaultReadCapabilities.ToList(),
                    Paths = new PolicyPaths
                    {
                        Read = new List<string> { "Assets/**" }
                    }
                };
                return true;
            }

            source = ProjectPaths.NormalizeUnityPath(path);
            try
            {
                policy = JsonConvert.DeserializeObject<PolicyFile>(
                    File.ReadAllText(path),
                    CommandJson.StrictSettings);
                if (policy == null)
                {
                    error = "The policy file is empty.";
                    policy = new PolicyFile();
                    return false;
                }

                policy.Allow = policy.Allow ?? new List<string>();
                policy.Paths = policy.Paths ?? new PolicyPaths();
                policy.Paths.Read = policy.Paths.Read ?? new List<string>();
                policy.Paths.Write = policy.Paths.Write ?? new List<string>();
                if (!TryNormalizeAuthorizationMode(policy.AuthorizationMode, out error))
                {
                    return false;
                }

                policy.AuthorizationMode = AuthorizationModes.ConfirmActions;
                return true;
            }
            catch (Exception exception)
            {
                policy = new PolicyFile();
                error = "The policy file is invalid: " + exception.Message;
                return false;
            }
        }

        private static bool TryNormalizeAuthorizationMode(string value, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "The policy file must set authorizationMode to confirm_actions.";
                return false;
            }

            if (string.Equals(value, AuthorizationModes.FullAccess, StringComparison.Ordinal))
            {
                error = "authorizationMode full_access is reserved and is not implemented.";
                return false;
            }

            if (!string.Equals(value, AuthorizationModes.ConfirmActions, StringComparison.Ordinal))
            {
                error = "authorizationMode must be confirm_actions.";
                return false;
            }

            return true;
        }

        private static bool TryNormalizePathAccess(
            string value,
            out string pathAccess,
            out string error)
        {
            error = null;
            pathAccess = string.IsNullOrWhiteSpace(value)
                ? CommandPathAccess.None
                : value;

            if (pathAccess == CommandPathAccess.None ||
                pathAccess == CommandPathAccess.Read ||
                pathAccess == CommandPathAccess.Write)
            {
                return true;
            }

            error = "pathAccess must be none, read, or write.";
            return false;
        }

        private bool TryNormalizeAssetsPath(
            string path,
            out string normalized,
            out string error)
        {
            normalized = ProjectPaths.NormalizeUnityPath(path);
            error = null;

            if (Path.IsPathRooted(path) ||
                normalized.Contains("../") ||
                normalized.Equals("..", StringComparison.Ordinal) ||
                !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Only normalized child paths of Assets/ are allowed.";
                return false;
            }

            string fullPath = Path.GetFullPath(
                Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets")) +
                                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "The path escapes the project Assets directory.";
                return false;
            }

            string current = assetsRoot.TrimEnd(Path.DirectorySeparatorChar);
            string relative = fullPath.Substring(assetsRoot.Length);
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        error = "Paths traversing symbolic links or reparse points are not allowed.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool MatchesAny(string path, IEnumerable<string> patterns)
        {
            foreach (string rawPattern in patterns ?? Enumerable.Empty<string>())
            {
                string pattern = ProjectPaths.NormalizeUnityPath(rawPattern);
                if (pattern.EndsWith("/**", StringComparison.Ordinal))
                {
                    string prefix = pattern.Substring(0, pattern.Length - 3).TrimEnd('/');
                    if (path.Equals(prefix, StringComparison.Ordinal) ||
                        path.StartsWith(prefix + "/", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (path.Equals(pattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static PermissionDecision Decision(
            bool allowed,
            PermissionRequest request,
            string reason,
            string authorizationMode = null,
            bool requiresConfirmation = false)
        {
            return new PermissionDecision
            {
                Allowed = allowed,
                Capability = request == null ? null : request.Capability,
                Path = request == null ? null : request.Path,
                PathAccess = request == null ? null : request.PathAccess,
                AuthorizationMode = authorizationMode,
                RequiresConfirmation = requiresConfirmation,
                Reason = reason
            };
        }
    }
}
