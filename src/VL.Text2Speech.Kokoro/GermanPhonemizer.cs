using System.Text;
using KokoroSharp.Processing;
using MisakiSharp;

namespace VL.Text2Speech.Kokoro;

/// German G2P for Kokoro: MisakiSharp EspeakG2P + live espeak-ng.
/// Tokenizer.Tokenize(..., "de") returns empty — German is not one of KokoroSharp's nine langs.
static class GermanPhonemizer
{
    static readonly object Gate = new();
    static EspeakG2P? g2p;
    static string? initError;

    internal static int[] Tokenize(string text)
    {
        var phonemes = Phonemize(text);
        if (string.IsNullOrWhiteSpace(phonemes))
        {
            throw new InvalidOperationException(
                "German phonemizer produced no tokens. Check espeak-ng (scripts\\download_german_pack.ps1) and that Voices Path points at df_* / dm_* names.");
        }

        return Tokenizer.TokenizePhonemes(phonemes.Where(Tokenizer.Vocab.ContainsKey).ToArray());
    }

    internal static string Phonemize(string text)
    {
        var engine = GetG2P();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (i > 0)
                sb.Append('\n');
            if (line.Length == 0)
                continue;
            var ps = engine.Phonemize(line);
            foreach (var c in ps)
            {
                if (Tokenizer.Vocab.ContainsKey(c))
                    sb.Append(c);
            }
        }

        return sb.ToString();
    }

    static EspeakG2P GetG2P()
    {
        lock (Gate)
        {
            if (g2p is not null)
                return g2p;
            if (initError is not null)
                throw new InvalidOperationException(initError);

            try
            {
                g2p = Create();
                return g2p;
            }
            catch (Exception ex)
            {
                initError = "German G2P needs espeak-ng. Run scripts\\download_german_pack.ps1 (extracts a portable copy into lib\\espeak). Detail: " + ex.Message;
                throw new InvalidOperationException(initError, ex);
            }
        }
    }

    static EspeakG2P Create()
    {
        var (dll, data) = FindEspeak();
        if (dll is not null && data is not null)
            return new EspeakG2P(EspeakG2P.LibraryProvider("de", dll, data));

        var exe = FindEspeakExe();
        if (exe is not null)
        {
            var dataDir = Path.GetDirectoryName(exe);
            return new EspeakG2P(EspeakG2P.ProcessProvider("de", exe, dataDir));
        }

        throw new FileNotFoundException(
            "espeak-ng not found. Expected lib\\espeak\\libespeak-ng.dll next to VL.Text2Speech.Kokoro.dll, or an installed eSpeak NG.");
    }

    static (string? dll, string? data) FindEspeak()
    {
        foreach (var root in EspeakRoots())
        {
            var dll = Path.Combine(root, "libespeak-ng.dll");
            if (!File.Exists(dll))
                dll = Path.Combine(root, "lib", "libespeak-ng.dll");
            if (!File.Exists(dll))
                continue;

            var data = root;
            if (!Directory.Exists(Path.Combine(data, "espeak-ng-data")))
            {
                var nested = Path.Combine(root, "espeak-ng-data");
                if (Directory.Exists(nested))
                    data = root;
                else
                    continue;
            }

            return (dll, data);
        }

        return (null, null);
    }

    static string? FindEspeakExe()
    {
        foreach (var root in EspeakRoots())
        {
            var exe = Path.Combine(root, "espeak-ng.exe");
            if (File.Exists(exe))
                return exe;
        }

        return null;
    }

    static IEnumerable<string> EspeakRoots()
    {
        var lib = KokoroRuntime.AssemblyDirectory;
        yield return Path.Combine(lib, "espeak");
        yield return Path.Combine(KokoroRuntime.RepoRoot, "lib", "espeak");
        yield return Path.Combine(lib, "espeak-ng");
        yield return @"C:\Program Files\eSpeak NG";
        yield return @"C:\Program Files (x86)\eSpeak NG";
    }

    /// Test hook: force re-init after a pack download in the same process.
    internal static void Reset()
    {
        lock (Gate)
        {
            g2p = null;
            initError = null;
        }
    }
}
