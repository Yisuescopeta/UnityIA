using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityIA.Contracts;
using UnityIA.Context;

namespace UnityIA.Authoring
{
    internal static class AuthoringValidation
    {
        public static ActionResult<JObject> ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "name must not be empty.");
            }

            if (name.Length > 200)
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "name must be 200 characters or fewer.");
            }

            return Results.Ok("Name is valid.");
        }

        public static ActionResult<JObject> ValidateVector(Vector3Dto vector, string field)
        {
            if (vector == null)
            {
                return Results.Ok(field + " was not provided.");
            }

            if (!IsFinite(vector.X) || !IsFinite(vector.Y) || !IsFinite(vector.Z))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    field + " components must be finite numbers.");
            }

            return Results.Ok(field + " is valid.");
        }

        public static ObjectResolution Resolve(
            ObjectReferenceDto reference,
            CommandEnvelope envelope)
        {
            string scenePath = envelope.Preconditions == null
                ? null
                : envelope.Preconditions.ActiveScenePath;
            return SceneObjectResolver.Resolve(reference, scenePath);
        }

        public static bool IsInternalPrefabObject(GameObject gameObject)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                return false;
            }

            return PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) != gameObject;
        }

        public static JObject ObjectData(GameObject gameObject)
        {
            return SceneObjectResolver.Describe(gameObject);
        }

        public static Vector3 ToVector(Vector3Dto value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

