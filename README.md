# Text2Speech_Kokoro82M

Offline near-real-time text-to-speech for **vvvv Gamma 7.2**, using [KokoroSharp](https://www.nuget.org/packages/KokoroSharp/) and the open-source **Kokoro-82M** ONNX model.

**Tested with:** vvvv Gamma **7.2** (`C:\Program Files\vvvv\vvvv_gamma_7.2-win-x64`), VL language **2025.7.2**, Windows 10/11 x64.

---

## Dummy install tutorial

Follow this once on a machine that has never seen the project.

### What you need

| Item | Required? | Notes |
|------|-----------|--------|
| Windows 10/11 x64 | Yes | Gamma 7.2 is Win-x64 |
| [vvvv Gamma 7.2](https://visualprogramming.net/) | Yes | Confirmed: `vvvv_gamma_7.2-win-x64` |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Yes | To build `VL.Text2Speech.Kokoro` |
| This repo | Yes | Clone or unzip `Text2Speech_Kokoro82M` |
| Internet, first time only | Once | Downloads `kokoro-fp16.onnx` (~156 MB) |
| GPU | No | CPU path (`KokoroSharp.CPU`) is what we ship |

You do **not** need Azure, Edge, API keys, or Python for English. German pack conversion uses Python once (`scripts\download_german_pack.ps1`).

### 1. Get the repo

```powershell
cd C:\Users\sebas\Nextcloud\_QUADRATURE\01_PROJECTS\01_Current\2026_Schaufler\_Dev
git clone https://github.com/kleinstkariert/Text2Speech_Kokoro82M.git
cd Text2Speech_Kokoro82M
```

If you already have the folder, `cd` into it instead.

### 2. Quit Gamma

Close **all** vvvv Gamma windows. The build copies DLLs into `lib\` and Gamma locks them if it is open.

### 3. Download the model and build the library

```powershell
scripts\install_gamma.ps1
```

This:

1. Downloads `models\kokoro-fp16.onnx` if missing
2. Builds `VL.Text2Speech.Kokoro`
3. Copies the main DLL **and** runtime files into `lib\` (`KokoroSharp.dll`, `MisakiSharp.dll`, `onnxruntime.dll`, `lib\voices\`, …)

Optional check (no Gamma): `scripts\smoke_tts.ps1` writes `artifacts\hello_kokoro.wav`.

### 4. Add the library in Gamma (file reference)

Start Gamma **normally**. Open a new document or `gamma\tests\KokoroTts_Example.vl`.

1. **Dependencies → Files → Add Existing**
2. Select  
   `…\Text2Speech_Kokoro82M\lib\VL.Text2Speech.Kokoro.dll`
3. Leave **every** other file in `lib\` next to that DLL. Do not copy only the main DLL.

Node browser → category **Text2Speech** → `KokoroTts`.

**Do not** use **Dependencies → VL Nugets → VL.Text2Speech.Kokoro**. That install path is broken in this project (same `__AdaptiveImplementations__` issue as VL.NBody.CUDA). It can also break **RandomSpread**. If that happens: quit Gamma, remove the NuGet dependency, restart, confirm RandomSpread, then add the DLL via Files again.

### 5. Use the example patch

Open `gamma\tests\KokoroTts_Example.vl`.

1. Wait until **Is Ready** is true (the model **auto-loads** on start; first load takes several seconds and includes a silent warmup)
2. Edit **Text** if you want
3. Bang **Speak** — audio plays from the default Windows device
4. Optional: bang **Save Wav** to write the path in **Wav Path**
5. Bang **Load** only when you change **Model Path** or **Voices Path** (English ↔ German packs)

Default voice: `af_heart`. Other names are on the **Voices** output (`am_michael`, `bf_emma`, …).

**New Line Pause** (seconds): silence after a `\n`. KokoroSharp default is 0.5 s, which feels long on stage; the node defaults to **0.12**. Set `0` for no extra gap. After a rebuild, create an IO box on that pin if it is not wired yet.

Voice name prefix (first letter) picks language and phonemizer:

| Prefix | Language | Delay |
|--------|----------|--------|
| `a` | American English (`af_heart`, `am_michael`) | Heavier G2P (POS tagger) |
| `b` | British English | Same English G2P |
| `z` | Mandarin Chinese (`zf_*`, `zm_*`) | Faster Chinese G2P — this is why `z*` voices start sooner |
| `e` `f` `h` `i` `p` | Spanish, French, Hindi, Italian, Portuguese | Medium |
| `d` | German (`dm_martin`, `df_victoria`) | espeak-ng; only after the German pack is loaded |

`z*` voices are not “better English”; they are Chinese speakers. English text through a `z` voice is the wrong G2P.

### German pack (separate load, English voices stay)

Do **not** mix `df_*` / `dm_*` into `lib\voices`. English stays the default pack. German is another ONNX + another voices folder. Bang **Load** before you switch language.

```powershell
scripts\download_german_pack.ps1
```

That writes `models\kokoro-de.onnx` (KokoroSharp-compatible Martin ONNX) and `models\voices-de\` (`dm_martin.npy`, `df_victoria.npy`), plus a portable `lib\espeak` for German G2P.

In Gamma, on `KokoroTts`:

1. **Model Path** = `…\models\kokoro-de.onnx`
2. **Voices Path** = `…\models\voices-de`
3. **Voice** = `dm_martin`
4. Bang **Load**, wait for **Is Ready**
5. Bang **Speak**

Back to English: Model Path empty or `kokoro-fp16.onnx`, Voices Path empty, Voice `af_heart`, bang **Load**.

*Verified fact:* Martin ONNX is the matched checkpoint for `dm_martin`. `df_victoria` is in the same voices folder (converted from kikiri `.pt`). It is a different Stage 2 finetune, so it may sound off on the Martin graph.

*Verified fact:* KokoroSharp has no `de` in `Tokenizer`. This wrapper phonemizes `d*` names with espeak-ng (`misaki` EspeakG2P), then `Speak_Phonemes`.

Optional check: `scripts\smoke_tts.ps1 --german` writes `artifacts\hello_kokoro_de.wav`.

### 6. After you change C#

Quit Gamma, then:

```powershell
scripts\build_deploy.ps1
```

Restart Gamma or reload the file dependency.

---

## Why this stack

| Option | Offline | Gamma / C# effort | Notes |
|--------|---------|-------------------|-------|
| **KokoroSharp + Kokoro-82M** | Yes | Low | One NuGet, ONNX file, MisakiSharp phonemizer, `SpeakFast` for first-chunk latency |
| EdgeTTS.DotNet | **No** | Medium | Cloud Microsoft Edge voices; needs network |
| Microsoft Embedded TTS | Yes, after license | High | Limited-access models, extra SDK packages, voice license string |

**Technical suggestion:** DirectML/GPU packages exist if CPU is still too slow on the show machine.

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

More wiring notes: `docs\gamma-integration.md`.

## License

MIT for this wrapper. Kokoro-82M model and official voices: Apache 2.0 (hexgrad). KokoroSharp: MIT (Lyrcaxis).
