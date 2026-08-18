#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root "src\VL.Text2Speech.Kokoro"
$pkg = Join-Path $root "deployment\VL.Text2Speech.Kokoro"
$out = Join-Path $root "deployment\output"
$version = "0.1.0-alpha"

function Resolve-NuGetExe {
    if ($env:NUGET_EXE -and (Test-Path $env:NUGET_EXE)) { return $env:NUGET_EXE }
    $candidates = @(
        "C:\Program Files\vvvv\vvvv_gamma_7.2-win-x64\tools\NuGet.exe",
        "C:\Program Files\vvvv\vvvv_gamma_7.2-win-x64\packs\dependencies\tools\NuGet.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    $onPath = Get-Command nuget -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "NuGet.exe not found. Set NUGET_EXE or install Gamma 7.2."
}

$nuget = Resolve-NuGetExe
Write-Host "Using NuGet: $nuget"

& (Join-Path $root "scripts\build_deploy.ps1")

$dll = Join-Path $root "lib\VL.Text2Speech.Kokoro.dll"
if (-not (Test-Path $dll)) { throw "Missing: $dll" }

$libOut = Join-Path $pkg "lib\net8.0-windows7.0"
New-Item -ItemType Directory -Force -Path $libOut, (Join-Path $pkg "docs"), $out | Out-Null

Get-ChildItem (Join-Path $root "lib") -File | Where-Object { $_.Name -ne ".gitkeep" } | ForEach-Object {
    Copy-Item -Force $_.FullName (Join-Path $libOut $_.Name)
}

$voicesSrc = Join-Path $root "lib\voices"
$voicesDst = Join-Path $libOut "voices"
if (Test-Path $voicesSrc) {
    if (Test-Path $voicesDst) { Remove-Item -Recurse -Force $voicesDst }
    Copy-Item -Recurse -Force $voicesSrc $voicesDst
}

Copy-Item -Force (Join-Path $src "VL.Text2Speech.Kokoro.vl") (Join-Path $pkg "VL.Text2Speech.Kokoro.vl")
Copy-Item -Force (Join-Path $root "docs\gamma-integration.md") (Join-Path $pkg "docs\README.md")

# Do not pack the 156 MB ONNX. Users run scripts\download_model.ps1.
& $nuget pack (Join-Path $pkg "VL.Text2Speech.Kokoro.nuspec") -OutputDirectory $out -Version $version -Properties Configuration=Release
if ($LASTEXITCODE -ne 0) { throw "nuget pack failed" }

Write-Host "Package: $out\VL.Text2Speech.Kokoro.$version.nupkg"
Write-Host ""
Write-Host "This .nupkg is for distribution of binaries. In Gamma, still use Files -> lib\VL.Text2Speech.Kokoro.dll"
Write-Host "until PackageCompiler precompile is solved (see nbod-agent-handoff/nuget-issues-handoff.md)."
