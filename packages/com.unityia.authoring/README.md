# UnityIA Authoring package

Editor-only technical base for the UnityIA package architecture.

This package is not a complete implementation of the public authoring protocol.
In roadmap terms:

- `v0.1` covers foundation docs, protocol base, and minimum contracts.
- `v0.2` covers the Unity package base, dispatcher, contracts, and local test
  surfaces.
- `v0.3` is where the controlled public Authoring API starts.

The current package contains prototype infrastructure such as contracts, core
services, dispatcher wiring, context helpers, technical command handlers, and a
developer window. Those pieces are useful for verification, but they do not by
themselves make the reserved authoring commands stable.

The initial public v0.3 commands are `context.snapshot`,
`authoring.create_gameobject`, `authoring.add_component`,
`authoring.set_component_field`, and `authoring.save_scene`.

Reserved commands are not public API until they have DTOs, validators,
registration, permissions, audit, stable `ActionResult`, and verified tests.

Use `Window > UnityIA > Command Console` to exercise the dispatcher locally.
Treat that window as a technical verification surface, not as proof that the
public authoring contract is complete.

For development, install the package into a disposable Unity 6.3 LTS project
from this repository path:

```text
<repository>/packages/com.unityia.authoring
```

Run the package EditMode tests from the Unity Test Runner before declaring any
public authoring command stable. Public mutation tests require a sandbox
`.unityia/policy.json` based on the repository `.unityia/policy.example.json`.

`com.unity.test-framework` remains listed as a package dependency because this
package ships EditMode tests under `Tests/Editor` and is expected to verify
those tests out of the box when consumed as a local package during development.
