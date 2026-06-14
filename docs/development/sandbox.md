# Unity sandbox

Create a temporary Unity 6.3 LTS project outside the package directory. Add the
local package through Package Manager using:

```text
<repository>/packages/com.unityia.authoring
```

The sandbox is used to:

1. compile every Editor assembly;
2. run package EditMode tests;
3. open `Window > UnityIA > Command Console`;
4. verify the live HTTP transport and CLI integration.

`tools/create-sandbox/Create-UnityIASandbox.ps1` automates project creation when
the Unity executable is available. It is a developer utility and is not exposed
through any UnityIA command.

