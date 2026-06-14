using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA
{
    public static class UnityIATestAPI
    {
        private static readonly Dictionary<string, JObject> Runs =
            new Dictionary<string, JObject>();

        public static ActionResult<JObject> RunRegisteredSuite(TestRunRequest request)
        {
            return Results.Error(
                ResultCodes.InvalidCommand,
                "Registered test execution is reserved for v0.3.",
                new JObject
                {
                    ["suite"] = request == null ? null : request.Suite
                });
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
    }
}

