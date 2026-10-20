using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ProductionTranscription.App.Models;
using Vosk;

namespace ProductionTranscription.App.Services;

/// <summary>One bounded stream per source. Vosk executes on background tasks, never inside capture callbacks.</summary>
public sealed class OfflineTranscription : IDisposable
{
    private sealed class Worker
    {
        public required Channel<(float[] samples, int rate)> Queue;
        public required Task Task;
    }
    private readonly Dictionary<SourceConfig, Worker> _workers = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Model _model;
    public event Action<string>? Diagnostic;
    private long _receivedBlocks;
    public OfflineTranscription(string modelPath, SourceConfig[] sources, Action<string, string> onTranscript)
    {
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        foreach (var source in sources)
        {
            var queue = Channel.CreateBounded<(float[] samples, int rate)>(new BoundedChannelOptions(24)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
            var task = Task.Run(async () =>
            {
                try
                {
                    using var recognizer = new VoskRecognizer(_model, 16000f);
                    while (await queue.Reader.WaitToReadAsync(_stop.Token))
                        while (queue.Reader.TryRead(out var block))
                        {
                            var converted = Resample(block.samples, block.rate, 16000);
                            if (converted.Length == 0) continue;
                            if (recognizer.AcceptWaveform(converted, converted.Length))
                                Publish(recognizer.Result(), source.Name, onTranscript);
                            else if (Interlocked.Increment(ref _receivedBlocks) % 150 == 0)
                                Diagnostic?.Invoke($"Audio is reaching Vosk ({source.Name}); waiting for an utterance boundary.");
                        }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Diagnostic?.Invoke($"Recognition failed on {source.Name}: {ex.Message}"); }
            });
            _workers[source] = new Worker { Queue = queue, Task = task };
        }
    }

    public void Submit(SourceConfig source, float[] samples, int sampleRate)
    {
        if (_workers.TryGetValue(source, out var worker))
            worker.Queue.Writer.TryWrite((samples, sampleRate));
    }

    private static void Publish(string json, string source, Action<string, string> send)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("text", out var value))
        {
            string? words = value.GetString();
            if (!string.IsNullOrWhiteSpace(words)) send(source, words);
        }
    }
    private static float[] Resample(float[] input, int sourceRate, int outputRate)
    {
        if (sourceRate <= 0 || input.Length == 0) return Array.Empty<float>();
        if (sourceRate == outputRate) return input;
        int count = (int)Math.Round((double)input.Length * outputRate / sourceRate);
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            double pos = (double)i * sourceRate / outputRate;
            int left = Math.Min((int)pos, input.Length - 1);
            int right = Math.Min(left + 1, input.Length - 1);
            result[i] = (float)(input[left] + (input[right] - input[left]) * (pos - left));
        }
        return result;
    }
    public void Dispose()
    {
        _stop.Cancel();
        foreach (var worker in _workers.Values) worker.Queue.Writer.TryComplete();
        // Model must remain alive until recognizers finish; cap teardown waits.
        try { Task.WaitAll(_workers.Values.Select(w => w.Task).ToArray(), TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        _model.Dispose();
        _stop.Dispose();
    }
}
