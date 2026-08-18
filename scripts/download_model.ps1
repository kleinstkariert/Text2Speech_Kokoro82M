#Requires -Version 5.1
param(
    [ValidateSet("float16", "float32")]
    [string]$Precision = "float16"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$models = Join-Path $root "models"
New-Item -ItemType Directory -Force -Path $models | Out-Null

$files = @{
    float16 = @{ Name = "kokoro-fp16.onnx"; Url = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx"; MinBytes = 100000000 }
    float32 = @{ Name = "kokoro.onnx"; Url = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx"; MinBytes = 200000000 }
}

$spec = $files[$Precision]
$dest = Join-Path $models $spec.Name

if ((Test-Path $dest) -and (Get-Item $dest).Length -ge $spec.MinBytes) {
    Write-Host "Already present: $dest ($((Get-Item $dest).Length) bytes)"
    exit 0
}

$tmp = "$dest.tmp"
Write-Host "Downloading $($spec.Name) ($Precision) ..."
Write-Host "  $($spec.Url)"

# GitHub release assets need TLS 1.2 and a user-agent; curl.exe handles large files well.
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($curl) {
    & curl.exe -L --fail --retry 3 --retry-all-errors -A "Text2Speech_Kokoro82M" -o $tmp $spec.Url
    if ($LASTEXITCODE -ne 0) { throw "curl download failed ($LASTEXITCODE)" }
}
else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $spec.Url -OutFile $tmp -UseBasicParsing
}

if (-not (Test-Path $tmp) -or (Get-Item $tmp).Length -lt $spec.MinBytes) {
    throw "Download incomplete: $tmp"
}

Move-Item -Force $tmp $dest
Write-Host "Saved: $dest ($((Get-Item $dest).Length) bytes)"
