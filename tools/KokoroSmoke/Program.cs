using KokoroSharp;
using KokoroSharp.Processing;
using VL.Text2Speech.Kokoro;

var root = FindRepoRoot();
var german = args.Any(a => a.Equals("--german", StringComparison.OrdinalIgnoreCase));
var textArgs = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var model = german
    ? Path.Combine(root, "models", "kokoro-de.onnx")
    : Path.Combine(root, "models", "kokoro-fp16.onnx");
if (!File.Exists(model))
    throw new FileNotFoundException(german
        ? "Run scripts\\download_german_pack.ps1 first."
        : "Run scripts\\download_model.ps1 first.", model);

var voices = german
    ? Path.Combine(root, "models", "voices-de")
    : Path.Combine(root, "lib", "voices");
if (!Directory.Exists(voices) && !german)
{
    var buildVoices = Path.Combine(root, "src", "VL.Text2Speech.Kokoro", "bin", "Release", "net8.0-windows", "voices");
    voices = Directory.Exists(buildVoices) ? buildVoices : voices;
}

Console.WriteLine("Model:  " + model);
Console.WriteLine("Voices: " + voices);

KokoroRuntime.ReplaceVoices(voices);
var voiceName = german ? "dm_martin" : "af_heart";
var voice = KokoroRuntime.ResolveVoice(voiceName);
Console.WriteLine("Voice: " + voice.Name + "  count=" + KokoroVoiceManager.Voices.Count);

var text = textArgs.Length > 0
    ? string.Join(" ", textArgs)
    : german
        ? "Guten Tag. Das ist offline Text zu Sprache."
        : "Hello from Kokoro. This is offline text to speech in Gamma.";
var outDir = Path.Combine(root, "artifacts");
Directory.CreateDirectory(outDir);
var wav = Path.Combine(outDir, german ? "hello_kokoro_de.wav" : "hello_kokoro.wav");

Console.WriteLine("Synthesizing…");
var bytes = KokoroRuntime.SynthesizeWav(model, text, voice, KokoroRuntime.FastSpeakConfig(1f, 0.12f));
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
