#Requires -Version 5.1
# Build VL.Text2Speech.Kokoro and copy the main DLL plus ALL runtime deps into lib\.
# Gamma file-references only the main DLL; CLR/native probes lib\ for KokoroSharp, ONNX Runtime, voices.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "src\VL.Text2Speech.Kokoro\VL.Text2Speech.Kokoro.csproj"
$libDir = Join-Path $root "lib"
$buildOut = Join-Path $root "src\VL.Text2Speech.Kokoro\bin\Release\net8.0-windows"

Write-Host "Quit vvvv Gamma before building (unlocks lib\VL.Text2Speech.Kokoro.dll)."
Write-Host ""

dotnet build $proj -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { throw "VL.Text2Speech.Kokoro build failed" }

$builtDll = Join-Path $buildOut "VL.Text2Speech.Kokoro.dll"
if (-not (Test-Path $builtDll)) { throw "Build output missing: $builtDll" }

New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$skipExact = @(
    "VL.Core.dll",
    "Stride.Core.dll",
    "Stride.Core.Mathematics.dll",
    "NuGet.Versioning.dll",
    "ServiceWire.dll"
)

function ShouldSkip([string]$name) {
    if ($name -like "VL.Text2Speech.Kokoro.*") { return $false }
    if ($name -like "Microsoft.ML.OnnxRuntime*") { return $false }
    foreach ($exact in $skipExact) {
        if ($name.Equals($exact, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    if ($name.StartsWith("Microsoft.Extensions.", [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($name.StartsWith("Microsoft.Win32.", [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $false
}

$copied = 0
Get-ChildItem $buildOut -File | ForEach-Object {
    $name = $_.Name
    if ($name -notmatch '\.(dll|deps\.json|config|pdb)$') { return }
    if (ShouldSkip $name) { return }
    Copy-Item -Force $_.FullName (Join-Path $libDir $name)
    $copied++
}

# Native ONNX Runtime for Windows x64 only (do not copy android/ios/linux over the same filename).
$nativeDir = Join-Path $buildOut "runtimes\win-x64\native"
if (Test-Path $nativeDir) {
    Get-ChildItem $nativeDir -File -Filter "*.dll" | ForEach-Object {
        Copy-Item -Force $_.FullName (Join-Path $libDir $_.Name)
        $copied++
    }
}

function Copy-Tree($src, $destName) {
    if (-not (Test-Path $src)) { return $false }
    $dest = Join-Path $libDir $destName
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    Copy-Item -Recurse -Force $src $dest
    return $true
}

$voicesCopied = $false
$voiceSources = @(
    (Join-Path $buildOut "voices"),
    (Join-Path $buildOut "runtimes\any\native\voices")
)
foreach ($src in $voiceSources) {
    if (Copy-Tree $src "voices") { $voicesCopied = $true; break }
}

# NuGet content files sometimes land under the package cache, not build output.
if (-not $voicesCopied) {
    $pkgVoice = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\kokorosharp") -Recurse -Directory -Filter "voices" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($pkgVoice) {
        Copy-Tree $pkgVoice.FullName "voices" | Out-Null
        $voicesCopied = $true
        Write-Host "Copied voices from NuGet cache: $($pkgVoice.FullName)"
    }
}

$npyCount = 0
$voicesDir = Join-Path $libDir "voices"
if (Test-Path $voicesDir) {
    $npyCount = @(Get-ChildItem $voicesDir -Filter "*.npy" -Recurse).Count
}

$mainDest = Join-Path $libDir "VL.Text2Speech.Kokoro.dll"
Copy-Item -Force $builtDll $mainDest

$required = @(
    "VL.Text2Speech.Kokoro.dll",
    "KokoroSharp.dll",
    "Microsoft.ML.OnnxRuntime.dll",
    "MisakiSharp.dll",
    "NumSharp.dll",
    "NAudio.dll"
)
$missing = @()
foreach ($dll in $required) {
    if (-not (Test-Path (Join-Path $libDir $dll))) { $missing += $dll }
}
if (-not (Test-Path (Join-Path $libDir "onnxruntime.dll"))) {
    $missing += "onnxruntime.dll (native)"
}
if ($npyCount -lt 1) {
    $missing += "voices\\*.npy"
}
if ($missing.Count -gt 0) {
    Write-Host "Build output tree:"
    Get-ChildItem $buildOut -Recurse -File | Select-Object -First 80 | ForEach-Object { Write-Host "  $($_.FullName.Substring($buildOut.Length))" }
    throw "Deploy incomplete. Missing in lib: $($missing -join ', ')"
}

$final = Get-Item $mainDest
Write-Host ""
Write-Host "Deployed to lib\. File copies: $copied  voices: $npyCount"
Get-ChildItem $libDir -File | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ""
Write-Host "Main DLL: lib\VL.Text2Speech.Kokoro.dll ($($final.Length) bytes)"
Write-Host ""
Write-Host "Gamma: Dependencies -> Files -> Add Existing -> lib\VL.Text2Speech.Kokoro.dll"
Write-Host "Keep all files in lib\ together. Do NOT add Dependencies -> VL Nugets -> VL.Text2Speech.Kokoro until precompile is proven."
Write-Host "ONNX model: models\kokoro-fp16.onnx  (scripts\download_model.ps1)"
