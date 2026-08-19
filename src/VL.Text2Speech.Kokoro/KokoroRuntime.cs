using System.Reflection;
using System.Runtime.InteropServices;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace VL.Text2Speech.Kokoro;

/// Shared paths for ONNX, voices, and native ONNX Runtime next to the Gamma file-ref DLL.
static class KokoroRuntime
{
    internal const string DefaultVoice = "af_heart";
    internal const float DefaultSpeed = 1f;
    internal const float DefaultNewLinePause = 0.12f;
    internal const int SampleRate = 24000;
    internal const int FastFirstSegmentTokens = 60;

    internal static KokoroSharp.Processing.KokoroTTSPipelineConfig FastSpeakConfig(float speed, float newLinePause)
    {
        return new KokoroSharp.Processing.KokoroTTSPipelineConfig(
            new KokoroSharp.Processing.DefaultSegmentationConfig { MaxFirstSegmentLength = FastFirstSegmentTokens })
        {
            Speed = Math.Clamp(speed, 0.5f, 2f),
            SecondsOfPauseBetweenProperSegments = new KokoroSharp.Processing.PauseAfterSegmentStrategy(
                CommaPause: 0.1f,
                PeriodPause: 0.35f,
                QuestionMarkPause: 0.35f,
                ExclamationMarkPause: 0.35f,
                NewLinePause: Math.Clamp(newLinePause, 0f, 2f),
                OthersPause: 0.35f),
        };
    }

    static KokoroRuntime()
    {
        var dir = AssemblyDirectory;
        if (!string.IsNullOrEmpty(dir))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
            {
                Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
            }

            TryPreloadNative(Path.Combine(dir, "onnxruntime.dll"));
        }
    }

    internal static string AssemblyDirectory
    {
        get
        {
            var loc = typeof(KokoroRuntime).Assembly.Location;
            return string.IsNullOrWhiteSpace(loc)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(loc) ?? AppContext.BaseDirectory;
        }
    }

    internal static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AssemblyDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "scripts", "build_deploy.ps1")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            var lib = AssemblyDirectory;
            return Directory.GetParent(lib)?.FullName ?? lib;
        }
    }

    internal static string VoicesDirectory
    {
        get
        {
            var nextToDll = Path.Combine(AssemblyDirectory, "voices");
            if (Directory.Exists(nextToDll))
                return nextToDll;
            var inRepo = Path.Combine(RepoRoot, "lib", "voices");
            return Directory.Exists(inRepo) ? inRepo : nextToDll;
        }
    }

    internal static string ResolveVoicesPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (Directory.Exists(requested))
                return Path.GetFullPath(requested);
            // User linked a file inside the voices folder — use its parent directory
            if (File.Exists(requested))
            {
                var parent = Path.GetDirectoryName(requested);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    return Path.GetFullPath(parent);
            }
        }

        return VoicesDirectory;
    }

    internal static string DefaultGermanVoicesDirectory =>
        Path.Combine(RepoRoot, "models", "voices-de");

    internal static string DefaultGermanModelPath =>
        Path.Combine(RepoRoot, "models", "kokoro-de.onnx");

    internal static bool IsGermanVoiceName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length >= 3
        && (name[0] is 'd' or 'D')
        && name[2] == '_';

    internal static string LangCodeForVoice(KokoroVoice voice)
    {
        if (IsGermanVoiceName(voice.Name))
            return "de";
        return voice.GetLangCode();
    }

    internal static int[] TokenizeForVoice(string text, KokoroVoice voice, bool preprocess = true)
    {
        var lang = LangCodeForVoice(voice);
        if (lang == "de")
            return GermanPhonemizer.Tokenize(text);
        return Tokenizer.Tokenize(text.Trim(), lang, preprocess);
    }

    internal static KokoroVoice ResolveVoice(string? voiceName)
    {
        EnsureVoicesLoaded();
        var name = string.IsNullOrWhiteSpace(voiceName) ? DefaultVoice : voiceName;
        var match = KokoroVoiceManager.Voices.FirstOrDefault(v =>
            v.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (match is not null)
            return match;
        if (KokoroVoiceManager.Voices.Count == 0)
            throw new InvalidOperationException("No Kokoro voices loaded.");
        return KokoroVoiceManager.Voices[0];
    }

    internal static byte[] SynthesizeWav(string modelPath, string text, KokoroVoice voice, KokoroTTSPipelineConfig config)
    {
        using var synth = KokoroWavSynthesizer.LoadModel(modelPath);
        var tokens = TokenizeForVoice(text, voice, config.PreprocessText);
        var segments = config.SegmentationFunc(tokens);
        var job = synth.EnqueueJob(KokoroJob.Create(segments, voice, config.Speed, null));
        var bytes = new List<byte>();
        var done = new TaskCompletionSource();
        foreach (var step in job.Steps)
        {
            step.OnStepComplete = samples =>
            {
                var trimmed = KokoroPlayback.PostProcessSamples(samples, out _);
                bytes.AddRange(KokoroPlayback.GetBytes(trimmed));
                if (step.Tokens.Length > 0 && Tokenizer.PunctuationTokens.Contains(step.Tokens[^1]))
                {
                    var sec = config.SecondsOfPauseBetweenProperSegments[Tokenizer.TokenToChar[step.Tokens[^1]]];
                    bytes.AddRange(KokoroPlayback.GetBytes(new float[(int)(sec * SampleRate)]));
                }

                if (step == job.Steps[^1])
                    done.TrySetResult();
            };
        }

        if (job.Steps.Count == 0)
            throw new InvalidOperationException("WAV synthesis produced no segments (empty phonemes).");
        if (!done.Task.Wait(TimeSpan.FromMinutes(5)))
            throw new TimeoutException("WAV synthesis timed out.");
        return bytes.ToArray();
    }

    internal static void ReplaceVoices(string dir)
    {
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                "Voices folder not found: " + dir + ". Empty Voices Path uses lib\\voices (English pack). German: models\\voices-de after scripts\\download_german_pack.ps1.");
        }

        KokoroVoiceManager.Voices.Clear();
        ClearLoadedVoicePaths();
        KokoroVoiceManager.LoadVoicesFromPath(dir);
        if (KokoroVoiceManager.Voices.Count == 0)
            throw new InvalidOperationException("No .npy voices loaded from " + dir);
    }

    static void ClearLoadedVoicePaths()
    {
        var field = typeof(KokoroVoiceManager).GetField("loadedFilePaths", BindingFlags.NonPublic | BindingFlags.Static);
        var value = field?.GetValue(null);
        if (value is HashSet<string> set)
            set.Clear();
        else if (value is System.Collections.IList list)
            list.Clear();
    }

    internal static string ResolveModelPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && File.Exists(requested))
            return Path.GetFullPath(requested);

        string[] candidates =
        [
            Path.Combine(RepoRoot, "models", "kokoro-fp16.onnx"),
            Path.Combine(RepoRoot, "models", "kokoro.onnx"),
            Path.Combine(AssemblyDirectory, "models", "kokoro-fp16.onnx"),
            Path.Combine(AssemblyDirectory, "kokoro-fp16.onnx"),
            Path.Combine(AssemblyDirectory, "kokoro.onnx"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "models", "kokoro-fp16.onnx")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "models", "kokoro-fp16.onnx")),
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return Path.Combine(RepoRoot, "models", "kokoro-fp16.onnx");
    }

    internal static void EnsureVoicesLoaded()
    {
        if (KokoroVoiceManager.Voices.Count > 0)
            return;

        var dir = VoicesDirectory;
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                "Kokoro voices folder not found. Run scripts\\build_deploy.ps1 so lib\\voices exists next to VL.Text2Speech.Kokoro.dll. Tried: " + dir);

        KokoroVoiceManager.LoadVoicesFromPath(dir);
        if (KokoroVoiceManager.Voices.Count == 0)
            throw new InvalidOperationException("No .npy voices loaded from " + dir);
    }

    static void TryPreloadNative(string path)
    {
        if (!File.Exists(path))
            return;
        try
        {
            NativeLibrary.Load(path);
        }
        catch
        {
            // ONNX Runtime may still resolve via PATH.
        }
    }
}
