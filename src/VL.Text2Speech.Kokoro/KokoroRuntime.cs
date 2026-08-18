using System.Runtime.InteropServices;

namespace VL.Text2Speech.Kokoro;

/// Shared paths for ONNX, voices, and native ONNX Runtime next to the Gamma file-ref DLL.
static class KokoroRuntime
{
    internal const string DefaultVoice = "af_heart";
    internal const float DefaultSpeed = 1f;
    internal const int SampleRate = 24000;

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
            var lib = AssemblyDirectory;
            var parent = Directory.GetParent(lib);
            return parent?.FullName ?? lib;
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
        var dir = VoicesDirectory;
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                "Kokoro voices folder not found. Run scripts\\build_deploy.ps1 so lib\\voices exists next to VL.Text2Speech.Kokoro.dll. Tried: " + dir);

        KokoroSharp.KokoroVoiceManager.LoadVoicesFromPath(dir);
        if (KokoroSharp.KokoroVoiceManager.Voices.Count == 0)
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
