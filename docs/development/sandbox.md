# Unity sandbox

Create a temporary Unity 6.3 LTS project outside the package directory. The
sandbox is disposable and must not be committed.

## Manual setup

Add the local package through Package Manager using:

```text
<repository>/packages/com.unityia.authoring
```

Recommended manual verification:

1. Open the disposable Unity project.
2. Add the local package from disk with the path above.
3. Confirm the package compiles without assembly errors.
4. Open `Window > UnityIA > Command Console`.
5. Run `system.status` or `context.snapshot` through the console.
6. Run package EditMode tests from the Unity Test Runner.

For public authoring mutation tests, create a development-only
`.unityia/policy.json` in the disposable project based on
`<repository>/.unityia/policy.example.json`. Do not commit sandbox policy files
from a Unity project.

## Automated setup

When the Unity executable is available, the helper can create a disposable
project, install the local package, mark it as testable, and copy the
development policy:

```powershell
tools/create-sandbox/Create-UnityIASandbox.ps1 `
  -UnityEditor "C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe" `
  -Destination "C:/Temp/UnityIA-Sandbox"
```

The sandbox is used to:

1. compile every Editor assembly;
2. run package EditMode tests;
3. open `Window > UnityIA > Command Console`;
4. verify the live HTTP transport and CLI integration.

`tools/create-sandbox/Create-UnityIASandbox.ps1` automates project creation when
the Unity executable is available. It is a developer utility and is not exposed
through any UnityIA command.

Run EditMode tests in batch with:

```powershell
& "C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe" `
  -batchmode `
  -nographics `
  -projectPath "C:/Temp/UnityIA-Sandbox" `
  -runTests `
  -testPlatform EditMode `
  -testResults "C:/Temp/UnityIA-Sandbox/UnityIA-EditMode-TestResults.xml" `
  -logFile "C:/Temp/UnityIA-Sandbox/UnityIA-EditMode.log"
```

Do not add `-quit` to this `-runTests` invocation; the Test Runner exits Unity
after writing the results.

The v0.5 CLI wrapper runs the same controlled path and emits an `ActionResult`
JSON summary:

```powershell
$env:UNITYIA_UNITY_EDITOR="C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe"

dotnet run --project cli/src/UnityIA.Cli/UnityIA.Cli.csproj -- `
  tests run `
  --mode EditMode `
  --project "C:/Temp/UnityIA-Sandbox"
```

Batch command execution is also available for command JSON files:

```powershell
dotnet run --project cli/src/UnityIA.Cli/UnityIA.Cli.csproj -- `
  --mode batch `
  execute `
  --file schemas/examples/context.snapshot.json `
  --project "C:/Temp/UnityIA-Sandbox"
```

## Verification boundary

Do not mark v0.3 capabilities as stable only because the source, docs, schemas,
or tests exist in the repository. They also need successful .NET 8 CLI tests and
Unity EditMode verification in this sandbox.
