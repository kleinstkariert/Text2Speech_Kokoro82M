using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace VL.Text2Speech.Kokoro;

/// <summary>
/// Kokoro-82M TTS — synthesis only, no audio device.
/// Outputs resampled float[] for VL.Audio (Buffer → BufferPlayer → AudioOut).
/// Set Target Sample Rate to match your ASIO driver (e.g. 44100 or 48000).
/// Use Duration output with a Stopwatch/MonoFlop to stop BufferPlayer after one play.
/// Voice listing is in the separate KokoroVoiceList node.
/// </summary>
[ProcessNode]
public sealed class KokoroTtsSynth : IDisposable
{
    KokoroEngine? engine;
    string loadedModelPath = "";
    string error = "";
    string status = "Idle.";
    string lastSpoken = "";
    bool isReady;
    bool isLoading;
    bool isSynthesizing;
    bool autoLoadStarted;
    bool prevLoad;
    bool prevSynth;

    Task? loadTask;
    Task? synthTask;

    readonly object samplesLock = new();
    float[] pendingSamples = [];
    float[] outputSamples = [];
    float outputDuration;
    bool newSamplesReady;

    public void Update(
        out bool isReady,
        out bool isLoading,
        out bool isSynthesizing,
        out bool newSamplesAvailable,
        out int sampleCount,
        out float duration,
        out Spread<float> samples,
        out string status,
        out string error,
        out string lastSpoken,
        string text = "Hello from Kokoro.",
        string voice = KokoroRuntime.DefaultVoice,
        float speed = KokoroRuntime.DefaultSpeed,
        float newLinePause = KokoroRuntime.DefaultNewLinePause,
        int targetSampleRate = 48000,
        VL.Lib.IO.Path modelPath = default!,
        VL.Lib.IO.Path voicesPath = default!,
        bool load = false,
        bool synthesize = false,
        bool enabled = true)
    {
        if (!enabled)
        {
            this.isSynthesizing = false;
            newSamplesAvailable = false;
            sampleCount = 0;
            duration = 0f;
            samples = Spread<float>.Empty;
            Assign(out isReady, out isLoading, out isSynthesizing, out status, out error, out lastSpoken);
            return;
        }

        PollLoad();
        PollSynth();

        var loadBang = load && !prevLoad;
        var synthBang = synthesize && !prevSynth;
        prevLoad = load;
        prevSynth = synthesize;

        var modelStr = modelPath?.ToString() ?? "";
        var voicesStr = voicesPath?.ToString() ?? "";
        var targetRate = Math.Clamp(targetSampleRate, 8000, 192000);

        if (loadBang && loadTask is not { IsCompleted: false })
        {
            StartLoad(modelStr, voicesStr);
        }
        else if (!autoLoadStarted && engine is null && loadTask is null)
        {
            autoLoadStarted = true;
            StartLoad(modelStr, voicesStr);
        }

        if (synthBang && synthTask is not { IsCompleted: false })
        {
            StartSynth(text, voice, speed, newLinePause, targetRate);
        }

        this.isLoading = loadTask is { IsCompleted: false };
        this.isSynthesizing = synthTask is { IsCompleted: false };

        lock (samplesLock)
        {
            newSamplesAvailable = newSamplesReady;
            if (newSamplesReady)
            {
                outputSamples = pendingSamples;
                pendingSamples = [];
                newSamplesReady = false;
            }
        }

        sampleCount = outputSamples.Length;
        duration = outputDuration;
        samples = outputSamples.Length > 0
            ? Spread.Create(outputSamples)
            : Spread<float>.Empty;

        Assign(out isReady, out isLoading, out isSynthesizing, out status, out error, out lastSpoken);
    }

    void Assign(
        out bool isReady,
        out bool isLoading,
        out bool isSynthesizing,
        out string status,
        out string error,
        out string lastSpoken)
    {
        isReady = this.isReady;
        isLoading = this.isLoading;
        isSynthesizing = this.isSynthesizing;
        status = this.status;
        error = this.error;
        lastSpoken = this.lastSpoken;
    }

    void StartLoad(string requestedPath, string requestedVoices)
    {
        this.isReady = false;
        this.isLoading = true;
        this.error = "";
        this.status = "Loading Kokoro pack…";
        var path = KokoroRuntime.ResolveModelPath(requestedPath);
        var voicesDir = KokoroRuntime.ResolveVoicesPath(requestedVoices);
        loadTask = Task.Run(() => LoadEngine(path, voicesDir));
    }

    void LoadEngine(string path, string voicesDir)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "ONNX model not found. English: scripts\\download_model.ps1. German: scripts\\download_german_pack.ps1. Expected: " + path, path);
        }

        KokoroRuntime.ReplaceVoices(voicesDir);
        var newEngine = new KokoroEngine(path);

        var warmupVoice = KokoroRuntime.ResolveVoice(KokoroRuntime.DefaultVoice);
        var tokens = KokoroRuntime.TokenizeForVoice("Hi.", warmupVoice);
        var done = new TaskCompletionSource();
        newEngine.EnqueueJob(KokoroJob.Create(tokens, warmupVoice, 1f, _ => done.TrySetResult()));
        if (!done.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("Kokoro warmup inference timed out.");

        var previous = engine;
        engine = newEngine;
        loadedModelPath = path;
        previous?.Dispose();
    }

    void StartSynth(string text, string voiceName, float speed, float newLinePause, int targetRate)
    {
        if (engine is null || !isReady)
        {
            error = "Model not loaded yet. Wait for Is Ready, or bang Load.";
            status = error;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Text is empty.";
            status = error;
            return;
        }

        var eng = engine;
        isSynthesizing = true;
        error = "";
        status = "Synthesizing…";
        synthTask = Task.Run(() => SynthesizeOnEngine(eng, text, voiceName, speed, newLinePause, targetRate));
    }

    void SynthesizeOnEngine(KokoroEngine eng, string text, string voiceName, float speed, float newLinePause, int targetRate)
    {
        KokoroRuntime.EnsureVoicesLoaded();
        var voice = KokoroRuntime.ResolveVoice(voiceName);
        var config = KokoroRuntime.FastSpeakConfig(speed, newLinePause);
        var tokens = KokoroRuntime.TokenizeForVoice(text, voice, config.PreprocessText);
        var segments = config.SegmentationFunc(tokens);

        var allSamples = new List<float>();
        var done = new TaskCompletionSource();

        var job = eng.EnqueueJob(KokoroJob.Create(segments, voice, config.Speed, null));
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var isLast = i == job.Steps.Count - 1;
            step.OnStepComplete = rawSamples =>
            {
                var trimmed = KokoroPlayback.PostProcessSamples(rawSamples, out _);
                allSamples.AddRange(trimmed);

                if (step.Tokens.Length > 0 && Tokenizer.PunctuationTokens.Contains(step.Tokens[^1]))
                {
                    var sec = config.SecondsOfPauseBetweenProperSegments[Tokenizer.TokenToChar[step.Tokens[^1]]];
                    var silenceCount = (int)(sec * KokoroRuntime.SampleRate);
                    if (silenceCount > 0)
                        allSamples.AddRange(new float[silenceCount]);
                }

                if (isLast)
                    done.TrySetResult();
            };
        }

        if (job.Steps.Count == 0)
            throw new InvalidOperationException("Synthesis produced no segments (empty phonemes).");

        if (!done.Task.Wait(TimeSpan.FromMinutes(5)))
            throw new TimeoutException("Synthesis timed out.");

        var native = allSamples.ToArray();
        var resampled = Resample(native, KokoroRuntime.SampleRate, targetRate);
        var dur = (float)native.Length / KokoroRuntime.SampleRate;

        lock (samplesLock)
        {
            pendingSamples = resampled;
            outputDuration = dur;
            newSamplesReady = true;
        }
        lastSpoken = text;
        status = "Synthesized.";
    }

    /// <summary>
    /// Linear-interpolation resample from srcRate to dstRate.
    /// Keeps BufferPlayer Speed at 1.0 regardless of ASIO sample rate.
    /// </summary>
    static float[] Resample(float[] src, int srcRate, int dstRate)
    {
        if (srcRate == dstRate)
            return src;

        double ratio = (double)srcRate / dstRate;
        int outLen = (int)(src.Length / ratio);
        var dst = new float[outLen];

        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i * ratio;
            int idx = (int)srcPos;
            double frac = srcPos - idx;

            if (idx + 1 < src.Length)
                dst[i] = (float)(src[idx] * (1.0 - frac) + src[idx + 1] * frac);
            else if (idx < src.Length)
                dst[i] = src[idx];
        }

        return dst;
    }

    void PollLoad()
    {
        if (loadTask is not { IsCompleted: true })
            return;

        try
        {
            loadTask.GetAwaiter().GetResult();
            isReady = engine is not null;
            isLoading = false;
            status = isReady ? "Ready. Bang Synthesize." : "Load finished without engine.";
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

    void PollSynth()
    {
        if (synthTask is not { IsCompleted: true })
            return;

        try
        {
            synthTask.GetAwaiter().GetResult();
            isSynthesizing = false;
            error = "";
        }
        catch (Exception ex)
        {
            isSynthesizing = false;
            error = Unwrap(ex);
            status = "Synthesis failed.";
        }
        finally
        {
            synthTask = null;
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
        engine?.Dispose();
        engine = null;
    }
}
