using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>Experimental offline whisper.cpp CLI adapter: CPU inference on completed audio segments.</summary>
public sealed class WhisperTranscription : ITranscriptionSession
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<SourceConfig, Channel<(float[] Samples, int Rate)>> _queues = new();
    private readonly List<Task> _tasks = new();
    private readonly SemaphoreSlim _cpu = new(1);
    private readonly string _cli;
    private readonly string _model;
    private readonly Action<string, string> _emit;
    public event Action<string>? Diagnostic;
    public event Action<string, string>? PartialRecognized;

    public WhisperTranscription(string cli, string model, SourceConfig[] sources, Action<string, string> emit)
    {
        if (!File.Exists(cli)) throw new FileNotFoundException("Install official whisper.cpp whisper-cli.exe and DLLs in tools.", cli);
        if (!File.Exists(model)) throw new FileNotFoundException("Missing GGML model.", model);
        _cli = cli; _model = model; _emit = emit;
        foreach (var source in sources)
        {
            var queue = Channel.CreateBounded<(float[] Samples, int Rate)>(new BoundedChannelOptions(128)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
            });
            _queues[source] = queue;
            _tasks.Add(Task.Run(() => ProcessSourceAsync(source, queue.Reader, _stop.Token)));
        }
    }
    public void Submit(SourceConfig source, float[] samples, int sampleRate)
    {
        if (_queues.TryGetValue(source, out var queue)) queue.Writer.TryWrite((samples, sampleRate));
    }
    private async Task ProcessSourceAsync(SourceConfig source, ChannelReader<(float[] Samples, int Rate)> reader, CancellationToken ct)
    {
        var buffer = new List<float>();
        int speech = 0, silence = 0;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            while (reader.TryRead(out var block))
            {
                float[] audio = Resample(block.Samples, block.Rate);
                if (audio.Length == 0) continue;
                double sum = 0;
                foreach (float sample in audio) sum += (double)sample * sample;
                bool active = Math.Sqrt(sum / audio.Length) >= Math.Pow(10, Math.Clamp(source.GateDbfs, -80, -20) / 20.0);
                if (!active && buffer.Count == 0) continue;
                buffer.AddRange(audio);
                if (active) { speech += audio.Length; silence = 0; }
                else silence += audio.Length;
                if (silence < 12000 && buffer.Count < 16000 * 12) continue;
                var utterance = buffer.ToArray();
                buffer.Clear();
                bool useful = speech >= 16000 / 3;
                speech = silence = 0;
                if (useful) await RecognizeAsync(source.Name, utterance, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diagnostic?.Invoke("Whisper [" + source.Name + "]: " + ex.Message); }
    }

    private async Task RecognizeAsync(string source, float[] audio, CancellationToken ct)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ProductionTranscription", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string wav = Path.Combine(folder, "input.wav");
        string output = Path.Combine(folder, "transcript");
        try
        {
            using (var stream = File.Create(wav))
            using (var writer = new BinaryWriter(stream))
            {
                int bytes = audio.Length * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                foreach (float sample in audio)
                    writer.Write((short)Math.Clamp((int)(Math.Clamp(sample, -1f, 1f) * 32767), -32768, 32767));
            }
            await _cpu.WaitAsync(ct);
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo(_cli)
                { UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in new[] { "-m", _model, "-f", wav, "-l", "en", "-t", "2", "-ng", "-otxt", "-of", output, "-np" })
                    process.StartInfo.ArgumentList.Add(arg);
                if (!process.Start()) throw new InvalidOperationException("Unable to start whisper-cli.");
                using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("whisper-cli exited with code " + process.ExitCode);
                if (File.Exists(output + ".txt"))
                {
                    string text = (await File.ReadAllTextAsync(output + ".txt", ct)).Trim();
                    if (!string.IsNullOrWhiteSpace(text)) _emit(source, text);
                }
            }
            finally { _cpu.Release(); }
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    private static float[] Resample(float[] audio, int rate)
    {
        if (rate <= 0 || audio.Length == 0) return Array.Empty<float>();
        if (rate == 16000) return audio;
        int length = (int)Math.Round(audio.Length * 16000.0 / rate);
        var result = new float[length];
        for (int n = 0; n < length; n++)
        {
            double position = n * rate / 16000.0;
            int left = Math.Min((int)position, audio.Length - 1);
            int right = Math.Min(left + 1, audio.Length - 1);
            result[n] = (float)(audio[left] + (audio[right] - audio[left]) * (position - left));
        }
        return result;
    }
    public void Dispose()
    {
        _stop.Cancel();
        foreach (var queue in _queues.Values) queue.Writer.TryComplete();
        try { Task.WaitAll(_tasks.ToArray(), TimeSpan.FromSeconds(3)); } catch { }
        _stop.Dispose();
        // The CLI process is cancelled with its recognition task; bounded shutdown is best effort.
    }
}
