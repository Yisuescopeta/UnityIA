using Newtonsoft.Json.Linq;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA
{
    public static class UnityIAAuthoringAPI
    {
        public static ActionResult<JObject> Execute(CommandEnvelope envelope)
        {
            return CoreServices.Dispatcher.Execute(envelope);
        }

        public static ActionResult<JObject> ExecuteJson(string json)
        {
            return CoreServices.Dispatcher.ExecuteJson(json);
        }
    }
}

