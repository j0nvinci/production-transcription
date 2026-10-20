using System.IO;
using System.Threading.Channels;
using ProductionTranscription.App.Models;
using Whisper.net;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Persistent Whisper runtime. Rolling partials every ~2 seconds with an up-to-5-second
/// context window; a completed utterance is decoded once at the end.
/// </summary>
public sealed class WhisperTranscription : ITranscriptionSession
{
    private const int Rate = 16000;
    private readonly WhisperFactory _factory;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<SourceConfig, Channel<(float[] Samples, int Rate)>> _queues = new();
    private readonly List<Task> _workers = new();
    private readonly SemaphoreSlim _inferGate = new(1, 1);
    private readonly Action<string, string> _emit;
    public event Action<string>? Diagnostic;
    public event Action<string, string>? PartialRecognized;

    public WhisperTranscription(string modelPath, SourceConfig[] sources, Action<string, string> emit)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Whisper model missing.", modelPath);
        _factory = WhisperFactory.FromPath(modelPath);
        _emit = emit;
        foreach (var source in sources)
        {
            var channel = Channel.CreateBounded<(float[] Samples, int Rate)>(new BoundedChannelOptions(256)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
            _queues[source] = channel;
            _workers.Add(Task.Run(() => WorkAsync(source, channel.Reader, _cancel.Token)));
        }
    }

    public void Submit(SourceConfig source, float[] samples, int sampleRate)
    {
        if (_queues.TryGetValue(source, out var channel))
            channel.Writer.TryWrite((samples, sampleRate));
    }

    private async Task WorkAsync(SourceConfig source,
        ChannelReader<(float[] Samples, int Rate)> reader, CancellationToken ct)
    {
        using var processor = _factory.CreateBuilder().WithLanguage("en").Build();
        var utterance = new List<float>(Rate * 12);
        int silence = 0, voiced = 0, nextPartial = Rate * 2;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            while (reader.TryRead(out var block))
            {
                float[] data = Resample(block.Samples, block.Rate);
                if (data.Length == 0) continue;
                double energy = 0;
                foreach (var value in data) energy += (double)value * value;
                bool active = Math.Sqrt(energy / data.Length) >= Math.Pow(10, Math.Clamp(source.GateDbfs, -80, -20) / 20);
                if (!active && utterance.Count == 0) continue;
                utterance.AddRange(data);
                if (active) { voiced += data.Length; silence = 0; }
                else silence += data.Length;

                bool finished = silence >= Rate * 3 / 4 || utterance.Count >= Rate * 12;
                if (finished)
                {
                    if (voiced >= Rate / 3)
                    {
                        string result = await DecodeAsync(processor, utterance.ToArray(), ct);
                        if (!string.IsNullOrWhiteSpace(result)) _emit(source.Name, result);
                    }
                    PartialRecognized?.Invoke(source.Name, "");
                    utterance.Clear();
                    silence = voiced = 0;
                    nextPartial = Rate * 2;
                }
                else if (utterance.Count >= nextPartial && voiced >= Rate / 3)
                {
                    // Only the most recent five seconds, avoiding unbounded partial inference.
                    float[] clip = utterance.Skip(Math.Max(0, utterance.Count - Rate * 5)).ToArray();
                    string partial = await DecodeAsync(processor, clip, ct);
                    PartialRecognized?.Invoke(source.Name, partial);
                    nextPartial = utterance.Count + Rate * 2;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diagnostic?.Invoke("Whisper [" + source.Name + "]: " + ex.Message); }
    }

    private async Task<string> DecodeAsync(WhisperProcessor processor, float[] data, CancellationToken ct)
    {
        await _inferGate.WaitAsync(ct);
        try
        {
            var words = new List<string>();
            await foreach (var segment in processor.ProcessAsync(data, ct))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text)) words.Add(segment.Text.Trim());
            }
            return string.Join(" ", words).Trim();
        }
        finally { _inferGate.Release(); }
    }

    private static float[] Resample(float[] input, int inputRate)
    {
        if (inputRate <= 0 || input.Length == 0) return Array.Empty<float>();
        if (inputRate == Rate) return input;
        int length = (int)Math.Round(input.Length * (double)Rate / inputRate);
        var output = new float[length];
        for (int i = 0; i < length; i++)
        {
            double position = (double)i * inputRate / Rate;
            int lo = Math.Min((int)position, input.Length - 1);
            int hi = Math.Min(lo + 1, input.Length - 1);
            output[i] = (float)(input[lo] + (input[hi] - input[lo]) * (position - lo));
        }
        return output;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        foreach (var queue in _queues.Values) queue.Writer.TryComplete();
        try { Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(10)); } catch { }
        _factory.Dispose();
        _inferGate.Dispose();
        _cancel.Dispose();
    }
}
