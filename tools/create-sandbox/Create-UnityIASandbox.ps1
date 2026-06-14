[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditor,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = "Stop"

$editorPath = (Resolve-Path -LiteralPath $UnityEditor).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$packagePath = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot "packages\com.unityia.authoring")).Path
$destinationPath = [System.IO.Path]::GetFullPath($Destination)

if (Test-Path -LiteralPath $destinationPath) {
    throw "Destination already exists: $destinationPath"
}

$unityArguments = "-batchmode -quit -createProject `"$destinationPath`""
$process = Start-Process `
    -FilePath $editorPath `
    -ArgumentList $unityArguments `
    -Wait `
    -PassThru `
    -WindowStyle Hidden

if ($process.ExitCode -ne 0) {
    throw "Unity project creation failed with exit code $($process.ExitCode)."
}

$manifestPath = Join-Path $destinationPath "Packages\manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$packageUri = "file:" + $packagePath.Replace("\", "/")
$manifest.dependencies | Add-Member `
    -NotePropertyName "com.unityia.authoring" `
    -NotePropertyValue $packageUri `
    -Force
$manifest | Add-Member `
    -NotePropertyName "testables" `
    -NotePropertyValue @("com.unityia.authoring") `
    -Force
$manifestJson = $manifest | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    (New-Object System.Text.UTF8Encoding($false)))

$policyDirectory = Join-Path $destinationPath ".unityia"
New-Item -ItemType Directory -Path $policyDirectory | Out-Null
Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot ".unityia\policy.example.json") `
    -Destination (Join-Path $policyDirectory "policy.json")

Write-Output $destinationPath
