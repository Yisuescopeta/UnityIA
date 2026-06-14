using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA
{
    public static class UnityIAContextAPI
    {
        public static ActionResult<JObject> GetSnapshot(ContextQuery query)
        {
            return Context.ContextService.GetSnapshot(query);
        }

        public static ActionResult<JObject> GetHierarchy(HierarchyQuery query)
        {
            return Context.ContextService.GetHierarchy(query);
        }
    }
}

