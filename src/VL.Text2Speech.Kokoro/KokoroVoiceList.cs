using KokoroSharp;

namespace VL.Text2Speech.Kokoro;

/// <summary>Lists bundled Kokoro-82M voice names (requires lib\voices from build_deploy).</summary>
[ProcessNode]
public sealed class KokoroVoiceList
{
    public void Update(out Spread<string> voices, out int count, out string error)
    {
        try
        {
            if (KokoroVoiceManager.Voices.Count == 0)
                KokoroRuntime.EnsureVoicesLoaded();
            var names = KokoroVoiceManager.Voices
                .Select(v => v.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            voices = Spread.Create(names);
            count = names.Length;
            error = "";
        }
        catch (Exception ex)
        {
            voices = Spread<string>.Empty;
            count = 0;
            error = ex.Message;
        }
    }
}
