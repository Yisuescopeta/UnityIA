# Development

Requirements:

- Unity 6.3 LTS;
- .NET 8 SDK;
- PowerShell 7 or a shell capable of running the documented equivalent steps.

The Unity package is installed as a local package in a disposable development
project. The package itself does not depend on that host project and the
sandbox is not distributed.

Build the CLI from the repository root:

```powershell
dotnet build cli/UnityIA.sln
dotnet test cli/UnityIA.sln
```

See [sandbox.md](sandbox.md) for the Unity test project.

