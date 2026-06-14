using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA
{
    public static class UnityIAValidationAPI
    {
        public static ActionResult<JObject> ValidateCommand(CommandEnvelope envelope)
        {
            return Core.CoreServices.Dispatcher.Validate(envelope);
        }

        public static ActionResult<JObject> ValidateState(StateValidationRequest request)
        {
            if (request == null)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "State validation request is required.");
            }

            return Core.EditorStateValidator.Validate(
                request.Preconditions,
                request.IsMutation);
        }
    }

    public static class UnityIAPermissionsAPI
    {
        public static ActionResult<JObject> Evaluate(PermissionRequest request)
        {
            PermissionDecision decision = Core.CoreServices.Permissions.Evaluate(request);
            return decision.Allowed
                ? Results.Ok("Permission granted.", JObject.FromObject(decision))
                : Results.Error(
                    ResultCodes.PermissionDenied,
                    decision.Reason,
                    JObject.FromObject(decision));
        }

        public static ActionResult<JObject> GetEffectivePolicy()
        {
            return Results.Ok(
                "Effective policy.",
                JObject.FromObject(Core.CoreServices.Permissions.GetEffectivePolicy()));
        }

        public static ActionResult<JObject> Explain(PermissionRequest request)
        {
            return Evaluate(request);
        }
    }
}
