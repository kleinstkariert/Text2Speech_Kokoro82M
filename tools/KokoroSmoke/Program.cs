using KokoroSharp;
using KokoroSharp.Processing;
using VL.Text2Speech.Kokoro;

var root = FindRepoRoot();
var model = Path.Combine(root, "models", "kokoro-fp16.onnx");
if (!File.Exists(model))
    throw new FileNotFoundException("Run scripts\\download_model.ps1 first.", model);

var voices = Path.Combine(root, "lib", "voices");
if (!Directory.Exists(voices))
{
    var buildVoices = Path.Combine(root, "src", "VL.Text2Speech.Kokoro", "bin", "Release", "net8.0-windows", "voices");
    voices = Directory.Exists(buildVoices) ? buildVoices : voices;
}

Console.WriteLine("Model:  " + model);
Console.WriteLine("Voices: " + voices);

KokoroVoiceManager.LoadVoicesFromPath(voices);
var voice = KokoroVoiceManager.GetVoice("af_heart");
Console.WriteLine("Voice count: " + KokoroVoiceManager.Voices.Count);

var text = args.Length > 0 ? string.Join(" ", args) : "Hello from Kokoro. This is offline text to speech in Gamma.";
var outDir = Path.Combine(root, "artifacts");
Directory.CreateDirectory(outDir);
var wav = Path.Combine(outDir, "hello_kokoro.wav");

Console.WriteLine("Synthesizing…");
using var synth = KokoroWavSynthesizer.LoadModel(model);
var bytes = synth.Synthesize(text, voice, new KokoroTTSPipelineConfig { Speed = 1f });
KokoroWavSynthesizer.SaveAudioToFile(bytes, wav);

var info = new FileInfo(wav);
Console.WriteLine($"Wrote {info.Length} bytes -> {wav}");
if (info.Length < 8000)
    throw new InvalidOperationException("WAV is too small; synthesis likely failed.");

Console.WriteLine("OK");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "scripts", "build_deploy.ps1")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
