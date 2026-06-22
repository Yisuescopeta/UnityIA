using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA
{
    public static class UnityIATestAPI
    {
        public const string PackageEditModeSuite = "unityia.package.editmode";
        public const string EditMode = "EditMode";

        private static readonly Dictionary<string, JObject> Runs =
            new Dictionary<string, JObject>();

        public static ActionResult<JObject> RunRegisteredSuite(TestRunRequest request)
        {
            ActionResult<JObject> validation = ValidateRunRequest(request);
            if (!validation.Success)
            {
                return validation;
            }

            string runId = System.Guid.NewGuid().ToString("N");
            JObject run = new JObject
            {
                ["runId"] = runId,
                ["suite"] = request.Suite,
                ["mode"] = NormalizedMode(request),
                ["status"] = "registered",
                ["runner"] = "unityia tests run",
                ["createdAtUtc"] = System.DateTimeOffset.UtcNow.ToString("o"),
                ["warnings"] = new JArray()
            };
            Runs[runId] = run;
            return Results.Ok("Registered test suite.", run);
        }

        public static ActionResult<JObject> GetRun(TestRunQuery query)
        {
            if (query == null ||
                string.IsNullOrWhiteSpace(query.RunId) ||
                !Runs.TryGetValue(query.RunId, out JObject run))
            {
                return Results.Error(
                    ResultCodes.TargetNotFound,
                    "The test run was not found.");
            }

            return Results.Ok("Test run.", run);
        }

        private static ActionResult<JObject> ValidateRunRequest(TestRunRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Suite))
            {
                return Results.Error(ResultCodes.ValidationFailed, "suite is required.");
            }

            if (request.Suite != PackageEditModeSuite)
            {
                return Results.Error(
                    ResultCodes.TargetNotFound,
                    "The requested test suite is not registered.",
                    new JObject
                    {
                        ["suite"] = request.Suite,
                        ["registeredSuites"] = new JArray { PackageEditModeSuite }
                    });
            }

            string mode = NormalizedMode(request);
            if (mode != EditMode)
            {
                return Results.Error(
                    ResultCodes.InvalidCommand,
                    "Only EditMode package tests are registered in v0.5.",
                    new JObject
                    {
                        ["suite"] = request.Suite,
                        ["mode"] = mode
                    });
            }

            return Results.Ok("Test run request is valid.");
        }

        private static string NormalizedMode(TestRunRequest request)
        {
            return string.IsNullOrWhiteSpace(request.Mode) ? EditMode : request.Mode.Trim();
        }
    }
}
