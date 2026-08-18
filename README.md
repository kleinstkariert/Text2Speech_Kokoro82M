# Text2Speech_Kokoro82M

Offline near-real-time text-to-speech for **vvvv Gamma 7.2**, using [KokoroSharp](https://www.nuget.org/packages/KokoroSharp/) and the open-source **Kokoro-82M** ONNX model.

## Why this stack

| Option | Offline | Gamma / C# effort | Notes |
|--------|---------|-------------------|-------|
| **KokoroSharp + Kokoro-82M** | Yes | Low | One NuGet, ONNX file, MisakiSharp phonemizer, `SpeakFast` for first-chunk latency |
| EdgeTTS.DotNet | **No** | Medium | Cloud Microsoft Edge voices; needs network |
| Microsoft Embedded TTS | Yes, after license | High | Limited-access models, extra SDK packages, voice license string |

**Requirement:** offline near-real-time TTS in Gamma.  
**Verified fact:** KokoroSharp.CPU is plug-and-play on .NET 8, models are public ONNX, voices ship with the package.  
**Technical suggestion:** DirectML/GPU packages exist if CPU is too slow on the show machine.

## Quick start

```powershell
cd Text2Speech_Kokoro82M
scripts\download_model.ps1          # models\kokoro-fp16.onnx (~156 MB)
scripts\install_gamma.ps1           # quit Gamma first
scripts\smoke_tts.ps1               # writes artifacts\hello_kokoro.wav
```

Gamma: **Dependencies → Files →** `lib\VL.Text2Speech.Kokoro.dll`  
Open `gamma\tests\KokoroTts_Example.vl` → bang **Load** → bang **Speak**.

Do **not** add this as a Gamma **VL NuGet** until package precompile works. Same class of failure as VL.NBody.CUDA (`__AdaptiveImplementations__`). Details: `docs\gamma-integration.md`.

## Layout

```
src/VL.Text2Speech.Kokoro/   C# ProcessNodes (ImportAsIs, category Text2Speech)
lib/                         deployed DLL + KokoroSharp + ONNX Runtime + voices
models/                      kokoro-fp16.onnx (gitignored; download script)
gamma/tests/                 example patch (Application wrapper)
scripts/                     build_deploy, pack, install, smoke, download_model
tools/KokoroSmoke/           headless WAV smoke test
deployment/                  VL nupkg layout (binaries; not Gamma-install-ready)
```

## License

MIT for this wrapper. Kokoro-82M model and official voices: Apache 2.0 (hexgrad). KokoroSharp: MIT (Lyrcaxis).
