# Text2Speech_Kokoro82M

Offline near-real-time text-to-speech for **vvvv Gamma 7.2**, using [KokoroSharp](https://www.nuget.org/packages/KokoroSharp/) and the open-source **Kokoro-82M** ONNX model.

**Tested with:** vvvv Gamma **7.2** (`C:\Program Files\vvvv\vvvv_gamma_7.2-win-x64`), VL language **2025.7.2**, Windows 10/11 x64.

---

## Dummy install tutorial (step by step)

Follow this **exactly once** on a fresh PC that has never seen the project. Every step tells you what to click and what to type.

### What you need before you start

| Item | Required? | Where to get it |
|------|-----------|-----------------|
| Windows 10/11 x64 | Yes | — |
| [vvvv Gamma 7.2](https://visualprogramming.net/) | Yes | Download from visualprogramming.net, run installer |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Yes | Download the **SDK** (not Runtime), run installer |
| [Git for Windows](https://git-scm.com/download/win) | Yes | Download, run installer (keep defaults) |
| Internet | Once | Downloads the ONNX model (~156 MB) on first run |
| GPU | No | Runs on CPU only |

You do **not** need Azure, Edge, API keys, or Python for English.

### 1. Clone the repo

1. Press **Win + R**, type `powershell`, press Enter. A blue/black terminal window opens.
2. Pick a folder where you want the project. For example your Desktop:
   ```powershell
   cd ~\Desktop
   ```
3. Clone (= download) the repository:
   ```powershell
   git clone https://github.com/kleinstkariert/Text2Speech_Kokoro82M.git
   ```
   This creates a folder `Text2Speech_Kokoro82M` (~150 MB download).
4. Go into the folder:
   ```powershell
   cd Text2Speech_Kokoro82M
   ```

If you already have the folder from a USB stick or Nextcloud, just `cd` into it instead of cloning.

### 2. Close Gamma

Close **all** vvvv Gamma windows before continuing. The next step rebuilds DLLs, and Gamma locks them while running.

### 3. Download the ONNX model and build

Still in the **same PowerShell window** (you should see `Text2Speech_Kokoro82M` in the prompt), type:

```powershell
.\scripts\install_gamma.ps1
```

> **If you get a red error about "execution policy":** run this once and answer **Y**:
> ```powershell
> Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
> ```
> Then try `.\scripts\install_gamma.ps1` again.

What the script does automatically:
1. Downloads `kokoro-fp16.onnx` (~156 MB) from [KokoroSharpBinaries](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx) into the `models\` folder
2. Builds `VL.Text2Speech.Kokoro.dll`
3. Copies everything into `lib\`

Wait until the script finishes (you see the PowerShell prompt `>` again, no errors in red).

**Optional quick test** (without Gamma): type `.\scripts\smoke_tts.ps1` — it writes `artifacts\hello_kokoro.wav`. Play that file to verify TTS works.

### 4. Open the example in Gamma

1. Start **vvvv Gamma 7.2** normally
2. In Gamma: **File → Open** → navigate to `Text2Speech_Kokoro82M\gamma\tests\KokoroTts_Example.vl` → Open
3. If Gamma asks about dependencies, accept/confirm
4. Wait a few seconds until the **Is Ready** output turns **true** (model auto-loads on start)
5. Bang **Speak** — audio plays from the default Windows audio device
6. Optional: bang **Save Wav** to save the audio to a `.wav` file

Default voice: `af_heart`. Other names appear on the **Voices** output pin (`am_michael`, `bf_emma`, …).

### If the example patch doesn't find the node

If the `KokoroTts` node shows red or is missing:

1. In Gamma: **Dependencies → Files → Add Existing**
2. Navigate to `Text2Speech_Kokoro82M\lib\VL.Text2Speech.Kokoro.dll` → select it
3. Do **not** move or copy only that DLL — everything in `lib\` must stay together

**Do not** use **Dependencies → VL Nugets → VL.Text2Speech.Kokoro**. That install path is broken (same `__AdaptiveImplementations__` issue as VL.NBody.CUDA). It can also break **RandomSpread**. If that happens: quit Gamma, remove the NuGet dependency, restart.

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
