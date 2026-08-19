# Gamma integration — VL.Text2Speech.Kokoro

**Gamma:** 7.2  
**Working path:** DLL file reference. VL NuGet add is **not** the consumption path.

## Setup

```powershell
# Quit Gamma first (unlocks lib\*.dll)
cd Text2Speech_Kokoro82M
scripts\install_gamma.ps1
```

Then in Gamma:

1. **Dependencies → Files → Add Existing** → `lib\VL.Text2Speech.Kokoro.dll`
2. Keep **all** files in `lib\` together (`KokoroSharp.dll`, `Microsoft.ML.OnnxRuntime.dll`, `onnxruntime.dll`, `lib\voices\`)
3. Node browser → category **Text2Speech** → `KokoroTts`
4. Open `gamma\tests\KokoroTts_Example.vl`
5. Wait until **Is Ready**, bang **Speak** (model auto-loads; bang **Load** to switch English/German packs)

Do **not** add **Dependencies → VL Nugets → VL.Text2Speech.Kokoro**. Same `__AdaptiveImplementations__` blocker as VL.NBody.CUDA / VL.DimensionReduction. A broken VL NuGet poisons RandomSpread.

## Nodes

| Node | Role |
|------|------|
| `KokoroTts` | Load ONNX once, speak on bang, optional WAV export |
| `KokoroVoiceList` | Bundled voice names |

`KokoroTts` pins (Enabled is rightmost):

- **Text**, **Voice** (`af_heart` default), **Speed** (0.5–1.3)
- **New Line Pause** — seconds of silence after a line break (library default was 0.5; node default **0.12**). `0` = no extra pause. Periods still use ~0.35 s.
- **Model Path** — empty uses `models\kokoro-fp16.onnx`
- **Voices Path** — empty uses `lib\voices` (English pack). German: `models\voices-de`. Bang **Load** after changing this; packs are not mixed.
- **Wav Path** + **Save Wav** bang — writes 24 kHz WAV (loads a second engine; prefer after Load)
- **Load** bang — optional; used to retry or load a different **Model Path** / **Voices Path**. First load is automatic.
- **Is Ready** / **Is Loading** / **Is Speaking** / **Error** / **Status** / **Voices**

Load and speak run on background threads. First load auto-starts and runs a silent warmup (phonemizer + one ONNX pass) so the first **Speak** is not paying cold-start cost. Bang **Load** to switch models.

## Model

`scripts\download_model.ps1` → `models\kokoro-fp16.onnx` (~156 MB). Not in git (GitHub 100 MB limit). Apache-2.0 Kokoro-82M weights from [KokoroSharpBinaries v2.0.0](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/tag/v2.0.0).

German pack (separate files, same node): `scripts\download_german_pack.ps1` → `models\kokoro-de.onnx` + `models\voices-de`. Source: [Godelaune/Kokoro-82M-ONNX-German-Martin](https://huggingface.co/Godelaune/Kokoro-82M-ONNX-German-Martin) (Apache 2.0, Kikiri Martin). The download script renames ONNX inputs so KokoroSharp can load them.

## Pack (binaries only)

```powershell
scripts\pack_vl_tts.ps1
# → deployment\output\VL.Text2Speech.Kokoro.0.1.0-alpha.nupkg
```

The nupkg ships the DLL + runtime deps + voices. It does **not** ship the ONNX. Installing it via Gamma **VL Nugets** is still the unsolved precompile path.

## Recovery if Gamma breaks

1. Quit Gamma
2. Remove VL.Text2Speech.Kokoro from Dependencies (Nugets and any `.vl` file ref)
3. Restart; confirm RandomSpread in a new empty document
4. Re-add **only** `lib\VL.Text2Speech.Kokoro.dll` via Files
