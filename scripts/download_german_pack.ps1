#Requires -Version 5.1
# German Kokoro pack: separate ONNX + voices from English.
# Does not mix df_*/dm_* into lib\voices.
param(
    [switch]$SkipEspeak
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$models = Join-Path $root "models"
$voicesDe = Join-Path $models "voices-de"
$libEspeak = Join-Path $root "lib\espeak"
New-Item -ItemType Directory -Force -Path $models, $voicesDe | Out-Null

function Get-File($Url, $Dest, $MinBytes) {
    if ((Test-Path $Dest) -and (Get-Item $Dest).Length -ge $MinBytes) {
        Write-Host "Already present: $Dest ($((Get-Item $Dest).Length) bytes)"
        return
    }
    $tmp = "$Dest.tmp"
    Write-Host "Downloading $Url"
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -L --fail --retry 3 --retry-all-errors -A "Text2Speech_Kokoro82M" -o $tmp $Url
        if ($LASTEXITCODE -ne 0) { throw "curl download failed ($LASTEXITCODE): $Url" }
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    }
    if (-not (Test-Path $tmp) -or (Get-Item $tmp).Length -lt $MinBytes) {
        throw "Download incomplete: $tmp"
    }
    Move-Item -Force $tmp $Dest
    Write-Host "Saved: $Dest ($((Get-Item $Dest).Length) bytes)"
}

$onnxDst = Join-Path $models "kokoro-de.onnx"
$npz = Join-Path $models "voices-martin.npz"

Get-File "https://huggingface.co/Godelaune/Kokoro-82M-ONNX-German-Martin/resolve/main/kokoro-martin.onnx" $onnxDst 100000000
Get-File "https://huggingface.co/Godelaune/Kokoro-82M-ONNX-German-Martin/resolve/main/voices-martin.npz" $npz 1000

# Victoria voicepack (optional). Same German family; matched ONNX is Martin.
$victoriaPt = Join-Path $models "victoria.pt"
try {
    Get-File "https://huggingface.co/kikiri-tts/kikiri-german-victoria/resolve/main/voices/victoria.pt" $victoriaPt 1000
}
catch {
    Write-Host "Victoria .pt skipped: $($_.Exception.Message)"
}

$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { $py = Get-Command py -ErrorAction SilentlyContinue }
if (-not $py) { throw "Python is required to convert German voices / rename ONNX inputs for KokoroSharp." }

Write-Host "Installing numpy + onnx (user) if needed..."
& python -m pip install --user -q numpy onnx
if ($LASTEXITCODE -ne 0) { throw "pip install numpy onnx failed" }

$prep = Join-Path $PSScriptRoot "prepare_german_pack.py"
& python $prep --root $root
if ($LASTEXITCODE -ne 0) { throw "prepare_german_pack.py failed" }

if (-not $SkipEspeak) {
    $dll = Join-Path $libEspeak "libespeak-ng.dll"
    $data = Join-Path $libEspeak "espeak-ng-data"
    if ((Test-Path $dll) -and (Test-Path $data)) {
        Write-Host "espeak-ng already extracted: $libEspeak"
    }
    else {
        $msi = Join-Path $models "espeak-ng.msi"
        Get-File "https://github.com/espeak-ng/espeak-ng/releases/download/1.52.0/espeak-ng.msi" $msi 1000000
        $extract = Join-Path $models "espeak-extract"
        if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
        New-Item -ItemType Directory -Force -Path $extract | Out-Null
        Write-Host "Extracting espeak-ng.msi (administrative, not a system install)..."
        $p = Start-Process -FilePath "msiexec.exe" -ArgumentList @("/a", "`"$msi`"", "/qn", "TARGETDIR=`"$extract`"") -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "msiexec /a failed ($($p.ExitCode))" }

        $foundDll = Get-ChildItem $extract -Recurse -Filter "libespeak-ng.dll" | Select-Object -First 1
        if (-not $foundDll) { throw "libespeak-ng.dll not found after MSI extract under $extract" }
        $foundData = Get-ChildItem $extract -Recurse -Directory -Filter "espeak-ng-data" | Select-Object -First 1
        if (-not $foundData) { throw "espeak-ng-data not found after MSI extract" }

        New-Item -ItemType Directory -Force -Path $libEspeak | Out-Null
        Copy-Item -Force $foundDll.FullName (Join-Path $libEspeak "libespeak-ng.dll")
        $dataDest = Join-Path $libEspeak "espeak-ng-data"
        if (Test-Path $dataDest) { Remove-Item -Recurse -Force $dataDest }
        Copy-Item -Recurse -Force $foundData.FullName $dataDest
        $exe = Get-ChildItem $foundDll.Directory.FullName -Filter "espeak-ng.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($exe) { Copy-Item -Force $exe.FullName (Join-Path $libEspeak "espeak-ng.exe") }
        Write-Host "espeak-ng portable: $libEspeak"
    }
}

if (-not (Test-Path $onnxDst)) { throw "Missing $onnxDst after prepare" }
$npy = @(Get-ChildItem $voicesDe -Filter "*.npy")
if ($npy.Count -lt 1) { throw "No .npy voices in $voicesDe" }

Write-Host ""
Write-Host "German pack ready."
Write-Host "  Model:  $onnxDst"
Write-Host "  Voices: $voicesDe  ($($npy.Count) npy)"
$npy | ForEach-Object { Write-Host "    $($_.Name)" }
Write-Host ""
Write-Host "In Gamma: set Model Path to the .onnx, Voices Path to models\voices-de, Voice to dm_martin, bang Load, then Speak."
