namespace UnityIA.Contracts
{
    public static class ResultCodes
    {
        public const string Ok = "OK";
        public const string InvalidJson = "INVALID_JSON";
        public const string InvalidCommand = "INVALID_COMMAND";
        public const string UnsupportedProtocol = "UNSUPPORTED_PROTOCOL";
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string PermissionDenied = "PERMISSION_DENIED";
        public const string PathNotAllowed = "PATH_NOT_ALLOWED";
        public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
        public const string InvalidEditorState = "INVALID_EDITOR_STATE";
        public const string StaleContext = "STALE_CONTEXT";
        public const string AmbiguousTarget = "AMBIGUOUS_TARGET";
        public const string TargetNotFound = "TARGET_NOT_FOUND";
        public const string EditorBusy = "EDITOR_BUSY";
        public const string SceneNotPersisted = "SCENE_NOT_PERSISTED";
        public const string AuditUnavailable = "AUDIT_UNAVAILABLE";
        public const string RequestTimeout = "REQUEST_TIMEOUT";
        public const string UnityOperationFailed = "UNITY_OPERATION_FAILED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
