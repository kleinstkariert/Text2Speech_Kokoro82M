# Kokoro-82M ONNX models

Weights are **not** committed (GitHub 100 MB file limit). Download with:

```powershell
scripts\download_model.ps1
```

Default for Gamma / CPU near-real-time: `models\kokoro-fp16.onnx` (~156 MB, Kokoro-82M v1.0 float16).

Optional full precision:

```powershell
scripts\download_model.ps1 -Precision float32
```

Source: [KokoroSharpBinaries v2.0.0](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/tag/v2.0.0)  
Original model: [hexgrad/Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) (Apache 2.0)

German (separate pack):

```powershell
scripts\download_german_pack.ps1
```

Writes `models\kokoro-de.onnx` and `models\voices-de\`. Source: [Godelaune/Kokoro-82M-ONNX-German-Martin](https://huggingface.co/Godelaune/Kokoro-82M-ONNX-German-Martin) / [kikiri-tts/kikiri-german-martin](https://huggingface.co/kikiri-tts/kikiri-german-martin) (Apache 2.0). Do not mix those `.npy` files into `lib\voices`.
