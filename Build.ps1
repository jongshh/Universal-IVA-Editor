param([Parameter(Mandatory=$true)][string]$KspRoot)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $KspRoot 'KSP_x64_Data/Managed/Assembly-CSharp.dll'))) { throw 'KSP 1.12.x installation required.' }
Push-Location $PSScriptRoot
try {
    & dotnet build src/UniversalIVAEditor.csproj -c Release "-p:KspRoot=$KspRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $plugins = Join-Path $PSScriptRoot 'GameData/UniversalIVAEditor/Plugins'
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/bin/Release/net48/UniversalIVAEditor.dll') -Destination (Join-Path $plugins 'UniversalIVAEditor.dll') -Force
} finally { Pop-Location }
