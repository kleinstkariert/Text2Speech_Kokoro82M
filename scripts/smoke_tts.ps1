#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $root "scripts\download_model.ps1")
& (Join-Path $root "scripts\build_deploy.ps1")
dotnet run --project (Join-Path $root "tools\KokoroSmoke\KokoroSmoke.csproj") -c Release -- @args
if ($LASTEXITCODE -ne 0) { throw "KokoroSmoke failed" }
