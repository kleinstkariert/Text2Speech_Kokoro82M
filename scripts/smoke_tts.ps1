#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$german = $args -contains "--german"
if ($german) {
    & (Join-Path $root "scripts\download_german_pack.ps1")
}
else {
    & (Join-Path $root "scripts\download_model.ps1")
}
& (Join-Path $root "scripts\build_deploy.ps1")
$forward = @($args | Where-Object { $_ -ne "--german" })
if ($german) { $forward = @("--german") + $forward }
dotnet run --project (Join-Path $root "tools\KokoroSmoke\KokoroSmoke.csproj") -c Release -- @forward
if ($LASTEXITCODE -ne 0) { throw "KokoroSmoke failed" }
