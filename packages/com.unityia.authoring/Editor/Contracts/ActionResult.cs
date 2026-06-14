using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityIA.Contracts
{
    public sealed class ActionResult<TData>
    {
        [JsonProperty("success", Required = Required.Always)]
        public bool Success { get; set; }

        [JsonProperty("message", Required = Required.Always)]
        public string Message { get; set; }

        [JsonProperty("code", Required = Required.Always)]
        public string Code { get; set; }

        [JsonProperty("data", Required = Required.Always)]
        public TData Data { get; set; }
    }

    public static class Results
    {
        public static ActionResult<JObject> Ok(
            string message,
            JObject data = null,
            IEnumerable<string> warnings = null)
        {
            JObject resultData = data ?? new JObject();
            EnsureWarnings(resultData, warnings);
            return new ActionResult<JObject>
            {
                Success = true,
                Message = message ?? string.Empty,
                Code = ResultCodes.Ok,
                Data = resultData
            };
        }

        public static ActionResult<JObject> Error(
            string code,
            string message,
            JObject data = null,
            IEnumerable<string> warnings = null)
        {
            JObject resultData = data ?? new JObject();
            EnsureWarnings(resultData, warnings);
            return new ActionResult<JObject>
            {
                Success = false,
                Message = message ?? string.Empty,
                Code = string.IsNullOrWhiteSpace(code) ? ResultCodes.InternalError : code,
                Data = resultData
            };
        }

        internal static ActionResult<TData> Ok<TData>(string message, TData data)
        {
            return new ActionResult<TData>
            {
                Success = true,
                Message = message ?? string.Empty,
                Code = ResultCodes.Ok,
                Data = data
            };
        }

        internal static ActionResult<TData> Error<TData>(string code, string message, TData data)
        {
            return new ActionResult<TData>
            {
                Success = false,
                Message = message ?? string.Empty,
                Code = string.IsNullOrWhiteSpace(code) ? ResultCodes.InternalError : code,
                Data = data
            };
        }

        public static void AddWarning(ActionResult<JObject> result, string warning)
        {
            if (result == null || string.IsNullOrWhiteSpace(warning))
            {
                return;
            }

            JArray warnings = result.Data["warnings"] as JArray;
            if (warnings == null)
            {
                warnings = new JArray();
                result.Data["warnings"] = warnings;
            }

            warnings.Add(warning);
        }

        private static void EnsureWarnings(JObject data, IEnumerable<string> warnings)
        {
            if (data["warnings"] == null)
            {
                data["warnings"] = warnings == null ? new JArray() : JArray.FromObject(warnings);
            }
        }
    }
}
