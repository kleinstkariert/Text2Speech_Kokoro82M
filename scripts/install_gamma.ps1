#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "lib\VL.Text2Speech.Kokoro.dll"

& (Join-Path $root "scripts\download_model.ps1")
& (Join-Path $root "scripts\build_deploy.ps1")

if (-not (Test-Path $dll)) { throw "Build output missing: $dll" }

Write-Host ""
Write-Host "DLL built:"
Write-Host "  $dll"
Write-Host ""
Write-Host "IMPORTANT: Do NOT add Dependencies -> Nugets -> VL.Text2Speech.Kokoro"
Write-Host "Gamma VL NuGet install is the same unsolved precompile issue as VL.NBody.CUDA / VL.DimensionReduction"
Write-Host "(__AdaptiveImplementations__ glue missing). A broken VL NuGet poisons RandomSpread."
Write-Host ""
Write-Host "Use this instead in Gamma:"
Write-Host "  1. Quit Gamma completely"
Write-Host "  2. Remove VL.Text2Speech.Kokoro from Dependencies -> Nugets (if present)"
Write-Host "  3. Start Gamma normally"
Write-Host "  4. Dependencies -> Files -> Add Existing ->"
Write-Host "     $dll"
Write-Host "     (KokoroSharp.dll, Microsoft.ML.OnnxRuntime.dll, onnxruntime.dll, and lib\voices must stay beside it)"
Write-Host "  5. Node browser -> category Text2Speech -> KokoroTts"
Write-Host "  6. Open gamma\tests\KokoroTts_Example.vl"
Write-Host "  7. Wait for Is Ready (auto-load). Bang Speak. Bang Load only to switch models."
Write-Host ""
Write-Host "Model: $($root)\models\kokoro-fp16.onnx"
Write-Host "If RandomSpread broke: remove the VL NuGet dependency and restart Gamma."
