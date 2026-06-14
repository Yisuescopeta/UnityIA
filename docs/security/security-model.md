# Security model

UnityIA is deny-by-default for mutations. If `.unityia/policy.json` is absent,
read-only operations remain available and all mutations are denied.

The v0.1 live server:

- binds only to `127.0.0.1`;
- requires a random bearer token for every endpoint;
- accepts at most 1 MiB per request;
- times out client waits after 30 seconds;
- has no WebSocket, streaming, or arbitrary route execution;
- executes Unity API work only on the Editor main thread.

Only normalized `Assets/` paths may be authorized as content paths.
`Packages`, `ProjectSettings`, `Library`, `UserSettings`, `Temp`, absolute
paths, parent traversal, and paths escaping through links are rejected.

Audit logs redact command payloads by recording a SHA-256 arguments hash
instead. Tokens and secrets are never logged.

