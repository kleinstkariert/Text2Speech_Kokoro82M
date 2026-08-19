using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace VL.Text2Speech.Kokoro;

/// <summary>
/// Offline Kokoro-82M TTS for Gamma. Load once, then bang Speak.
/// Inference and playback run off the VL update thread.
/// </summary>
[ProcessNode]
public sealed class KokoroTts : IDisposable
{
    KokoroTTS? tts;
    string loadedModelPath = "";
    string error = "";
    string status = "Loading Kokoro-82M…";
    string lastSpoken = "";
    string lastWavPath = "";
    bool isReady;
    bool isLoading;
    bool isSpeaking;
    bool autoLoadStarted;
    bool playbackActive;
    bool prevLoad;
    bool prevSpeak;
    bool prevStop;
    bool prevSaveWav;
    Task? loadTask;
    Task? speakTask;

    public void Update(
        out bool isReady,
        out bool isLoading,
        out bool isSpeaking,
        out int sampleRate,
        out Spread<string> voices,
        out string status,
        out string error,
        out string lastSpoken,
        out string lastWavPath,
        string text = "Hello from Kokoro.",
        string voice = KokoroRuntime.DefaultVoice,
        float speed = KokoroRuntime.DefaultSpeed,
        float newLinePause = KokoroRuntime.DefaultNewLinePause,
        VL.Lib.IO.Path modelPath = default!,
        VL.Lib.IO.Path voicesPath = default!,
        VL.Lib.IO.Path wavPath = default!,
        bool load = false,
        bool speak = false,
        bool saveWav = false,
        bool stop = false,
        bool enabled = true)
    {
        sampleRate = KokoroRuntime.SampleRate;
        voices = ListVoicesSafe();

        if (!enabled)
        {
            this.isSpeaking = false;
            Assign(out isReady, out isLoading, out isSpeaking, out status, out error, out lastSpoken, out lastWavPath);
            return;
        }

        PollLoad();
        PollSpeak();

        var loadBang = load && !prevLoad;
        var speakBang = speak && !prevSpeak;
        var stopBang = stop && !prevStop;
        var saveBang = saveWav && !prevSaveWav;
        prevLoad = load;
        prevSpeak = speak;
        prevStop = stop;
        prevSaveWav = saveWav;

        if (stopBang)
        {
            TryStop();
            playbackActive = false;
            this.status = "Stopped.";
            this.isSpeaking = false;
        }

        if (loadBang && loadTask is not { IsCompleted: false })
        {
            StartLoad(modelPath.ToString(), voicesPath.ToString());
        }
        else if (!autoLoadStarted && tts is null && loadTask is null)
        {
            autoLoadStarted = true;
            StartLoad(modelPath.ToString(), voicesPath.ToString());
        }

        if (saveBang && speakTask is not { IsCompleted: false })
        {
            StartSaveWav(text, voice, speed, newLinePause, wavPath.ToString());
        }
        else if (speakBang && speakTask is not { IsCompleted: false })
        {
            StartSpeak(text, voice, speed, newLinePause);
        }

        this.isLoading = loadTask is { IsCompleted: false };
        this.isSpeaking = speakTask is { IsCompleted: false } || playbackActive;
        if (!this.isSpeaking && this.status == "Speaking…")
            this.status = "Spoken.";

        Assign(out isReady, out isLoading, out isSpeaking, out status, out error, out lastSpoken, out lastWavPath);
    }

    void Assign(
        out bool isReady,
        out bool isLoading,
        out bool isSpeaking,
        out string status,
        out string error,
        out string lastSpoken,
        out string lastWavPath)
    {
        isReady = this.isReady;
        isLoading = this.isLoading;
        isSpeaking = this.isSpeaking;
        status = this.status;
        error = this.error;
        lastSpoken = this.lastSpoken;
        lastWavPath = this.lastWavPath;
    }

    void StartLoad(string requestedPath, string requestedVoices)
    {
        this.isReady = false;
        this.isLoading = true;
        this.error = "";
        this.status = "Loading Kokoro pack…";
        var path = KokoroRuntime.ResolveModelPath(requestedPath);
        var voices = KokoroRuntime.ResolveVoicesPath(requestedVoices);
        loadTask = Task.Run(() => LoadEngine(path, voices));
    }

    void LoadEngine(string path, string voicesDir)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "ONNX model not found. English: scripts\\download_model.ps1. German: scripts\\download_german_pack.ps1. Expected: " + path, path);
        }

        KokoroRuntime.ReplaceVoices(voicesDir);
        var engine = KokoroTTS.LoadModel(path);
        Warmup(engine);
        var previous = tts;
        tts = engine;
        loadedModelPath = path;
        previous?.Dispose();
    }

    static void Warmup(KokoroTTS engine)
    {
        var voice = KokoroRuntime.ResolveVoice(KokoroRuntime.DefaultVoice);
        var tokens = KokoroRuntime.TokenizeForVoice("Hi.", voice);
        var done = new TaskCompletionSource();
        engine.EnqueueJob(KokoroJob.Create(tokens, voice, 1f, _ => done.TrySetResult()));
        if (!done.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("Kokoro warmup inference timed out.");
    }

    void StartSpeak(string text, string voiceName, float speed, float newLinePause)
    {
        if (tts is null || !isReady)
        {
            error = "Model not loaded yet. Wait for Is Ready, or bang Load to retry / switch models.";
            status = error;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Text is empty.";
            status = error;
            return;
        }

        var engine = tts;
        var snapshot = text;
        var voice = voiceName;
        var spd = speed;
        var linePause = newLinePause;
        isSpeaking = true;
        error = "";
        status = "Speaking…";
        speakTask = Task.Run(() => SpeakOnEngine(engine, snapshot, voice, spd, linePause, saveWav: false, wavOut: ""));
    }

    void StartSaveWav(string text, string voiceName, float speed, float newLinePause, string wavOut)
    {
        if (string.IsNullOrWhiteSpace(wavOut))
        {
            error = "Wav Path is empty. Set a .wav file path before Save Wav.";
            status = error;
            return;
        }

        var path = KokoroRuntime.ResolveModelPath(loadedModelPath);
        if (!File.Exists(path) && tts is null)
        {
            error = "Model not loaded yet. Wait for Is Ready, or bang Load to retry / switch models.";
            status = error;
            return;
        }

        var snapshot = text;
        var voice = voiceName;
        var spd = speed;
        var linePause = newLinePause;
        var outPath = wavOut;
        isSpeaking = true;
        error = "";
        status = "Writing WAV…";
        var modelPath = string.IsNullOrEmpty(loadedModelPath) ? path : loadedModelPath;
        speakTask = Task.Run(() => SpeakOnEngine(tts, snapshot, voice, spd, linePause, saveWav: true, wavOut: outPath, modelPathForWav: modelPath));
    }

    void SpeakOnEngine(
        KokoroTTS? engine,
        string text,
        string voiceName,
        float speed,
        float newLinePause,
        bool saveWav,
        string wavOut,
        string? modelPathForWav = null)
    {
        KokoroRuntime.EnsureVoicesLoaded();
        var voice = KokoroRuntime.ResolveVoice(voiceName);
        var config = KokoroRuntime.FastSpeakConfig(speed, newLinePause);

        if (saveWav)
        {
            var synthPath = modelPathForWav ?? loadedModelPath;
            var bytes = KokoroRuntime.SynthesizeWav(synthPath, text, voice, config);
            var dir = Path.GetDirectoryName(wavOut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            KokoroWavSynthesizer.SaveAudioToFile(bytes, wavOut);
            lastWavPath = wavOut;
            lastSpoken = text;
            playbackActive = false;
            return;
        }

        if (engine is null)
            throw new InvalidOperationException("TTS engine is null.");

        playbackActive = true;
        var tokens = KokoroRuntime.TokenizeForVoice(text, voice, config.PreprocessText);
        var handle = engine.Speak_Phonemes(text, tokens, voice, config, fast: true);
        handle.OnSpeechCompleted += _ =>
        {
            lastSpoken = text;
            playbackActive = false;
        };
        handle.OnSpeechCanceled += _ => { playbackActive = false; };
    }

    void PollLoad()
    {
        if (loadTask is not { IsCompleted: true })
            return;

        try
        {
            loadTask.GetAwaiter().GetResult();
            isReady = tts is not null;
            isLoading = false;
            status = isReady ? "Ready. Bang Speak." : "Load finished without engine.";
            error = "";
        }
        catch (Exception ex)
        {
            isReady = false;
            isLoading = false;
            error = Unwrap(ex);
            status = "Load failed.";
        }
        finally
        {
            loadTask = null;
        }
    }

    void PollSpeak()
    {
        if (speakTask is not { IsCompleted: true })
            return;

        try
        {
            speakTask.GetAwaiter().GetResult();
            if (!playbackActive)
            {
                isSpeaking = false;
                status = string.IsNullOrEmpty(lastWavPath) ? "Spoken." : "WAV written.";
            }
            error = "";
        }
        catch (Exception ex)
        {
            playbackActive = false;
            isSpeaking = false;
            error = Unwrap(ex);
            status = "Speak failed.";
        }
        finally
        {
            speakTask = null;
        }
    }

    void TryStop()
    {
        try
        {
            tts?.StopPlayback();
        }
        catch
        {
            // ignore
        }
    }

    static Spread<string> ListVoicesSafe()
    {
        try
        {
            if (KokoroVoiceManager.Voices.Count == 0)
                KokoroRuntime.EnsureVoicesLoaded();
            return Spread.Create(KokoroVoiceManager.Voices.Select(v => v.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch
        {
            return Spread<string>.Empty;
        }
    }

    static string Unwrap(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null)
            inner = inner.InnerException;
        return inner.Message;
    }

    public void Dispose()
    {
        TryStop();
        tts?.Dispose();
        tts = null;
    }
}
