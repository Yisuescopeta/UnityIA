# Architecture

The required execution path is:

```text
CLI or Developer UI
  -> versioned JSON protocol
  -> authentication and queue
  -> Core.CommandDispatcher
  -> validation
  -> permissions
  -> audit request record
  -> registered handler
  -> audit result record
  -> ActionResult JSON
```

## Dependency direction

```text
Contracts <- Core <- Context <- Authoring
                  ^             |
                  |             |
             DeveloperUI   bootstrap registers handlers
                  ^
                  |
              Transport
```

`Transport` references only `Contracts` and `Core`. It receives JSON,
authenticates, queues work for the Editor main thread, invokes
`Core.CommandDispatcher`, and returns the serialized `ActionResult`.

`Authoring` never references `Transport`. `Core` never references `Authoring`;
the Authoring bootstrap registers handlers through the Core registry.

## Persistent and temporary state

- Unity scenes, prefabs, and assets are the persistent authoring truth.
- The open Editor state is editable operational state.
- Play Mode is temporary runtime state and rejects v0.1 mutations.
- A domain reload creates a new UnityIA session and invalidates previous context
  versions, object assumptions, tokens, and in-memory idempotency results.

## Version boundaries

- v0.1: live Editor mode, scene GameObjects, explicit scene save.
- v0.2: explicit batch mode, selected components, scene open/create, prefab
  instantiation, and optional evaluation of in-Editor JSON Schema validation.
- v0.3+: controlled asset/prefab editing, Play Mode control, and remote tests.

