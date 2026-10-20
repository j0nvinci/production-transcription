using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Explicit opt-in cloud audio transcription. Per-source silence gate sends completed
/// speech segments only, not continuous silence. No audio files are stored locally.
/// </summary>
public sealed class GroqTranscription : ITranscriptionSession
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<SourceConfig, Channel<(float[] Samples, int Rate)>> _channels = new();
    private readonly List<Task> _tasks = new();
    private readonly SemaphoreSlim _networkLimit = new(2);
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly Action<string, string> _output;

    public event Action<string>? Diagnostic;
    public event Action<string, string>? PartialRecognized;

    public GroqTranscription(string apiKey, string model, SourceConfig[] sources, Action<string, string> output)
    {
        _model = model;
        _output = output;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var source in sources)
        {
            var channel = Channel.CreateBounded<(float[] Samples, int Rate)>(
                new BoundedChannelOptions(128)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            _channels[source] = channel;
            _tasks.Add(Task.Run(() => WorkAsync(source, channel.Reader, _stop.Token)));
        }
    }

    public void Submit(SourceConfig source, float[] samples, int sampleRate)
    {
        if (_channels.TryGetValue(source, out var channel))
            channel.Writer.TryWrite((samples, sampleRate));
    }

    private async Task WorkAsync(SourceConfig source, ChannelReader<(float[] Samples, int Rate)> input, CancellationToken ct)
    {
        var audio = new List<float>();
        int voiced = 0;
        int quiet = 0;
        try
        {
            while (await input.WaitToReadAsync(ct))
            while (input.TryRead(out var block))
            {
                var converted = Resample(block.Samples, block.Rate);
                if (converted.Length == 0) continue;
                double sum = 0;
                foreach (float s in converted) sum += (double)s * s;
                bool active = Math.Sqrt(sum / converted.Length) >=
                    Math.Pow(10, Math.Clamp(source.GateDbfs, -80, -20) / 20.0);
                if (!active && audio.Count == 0) continue;
                audio.AddRange(converted);
                if (active) { voiced += converted.Length; quiet = 0; }
                else quiet += converted.Length;
                if (quiet < 12000 && audio.Count < 16000 * 12) continue;
                var segment = audio.ToArray();
                audio.Clear();
                bool transmit = voiced >= 8000;
                voiced = quiet = 0;
                if (transmit) await UploadAsync(source.Name, segment, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diagnostic?.Invoke("Groq [" + source.Name + "]: " + ex.Message); }
    }

    private async Task UploadAsync(string source, float[] samples, CancellationToken ct)
    {
        await _networkLimit.WaitAsync(ct);
        try
        {
            using var request = new MultipartFormDataContent();
            using var file = new ByteArrayContent(ToWave(samples));
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            request.Add(file, "file", "speech.wav");
            request.Add(new StringContent(_model), "model");
            request.Add(new StringContent("en"), "language");
            using var response = await _http.PostAsync(
                "https://api.groq.com/openai/v1/audio/transcriptions", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Diagnostic?.Invoke("Groq [" + source + "]: " +
                    (response.StatusCode == HttpStatusCode.TooManyRequests ?
                        "Rate limit reached (429); reduce channel activity or use a local engine." :
                        "HTTP " + (int)response.StatusCode));
                return;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.TryGetProperty("text", out var result))
            {
                string transcript = (result.GetString() ?? "").Trim();
                if (transcript.Length > 0) _output(source, transcript);
            }
        }
        finally { _networkLimit.Release(); }
    }

    private static byte[] ToWave(float[] samples)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.ASCII, true);
        int size = samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + size);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(32000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(size);
        foreach (float sample in samples)
            writer.Write((short)Math.Clamp((int)(Math.Clamp(sample, -1f, 1f) * 32767), -32768, 32767));
        return memory.ToArray();
    }

    private static float[] Resample(float[] samples, int rate)
    {
        if (rate <= 0 || samples.Length == 0) return Array.Empty<float>();
        if (rate == 16000) return samples;
        int count = (int)Math.Round(samples.Length * 16000.0 / rate);
        var output = new float[count];
        for (int i = 0; i < count; i++)
        {
            double position = i * rate / 16000.0;
            int left = Math.Min((int)position, samples.Length - 1);
            int right = Math.Min(left + 1, samples.Length - 1);
            output[i] = (float)(samples[left] + (samples[right] - samples[left]) * (position - left));
        }
        return output;
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var channel in _channels.Values) channel.Writer.TryComplete();
        try { Task.WaitAll(_tasks.ToArray(), TimeSpan.FromSeconds(5)); } catch { }
        _http.Dispose();
        _networkLimit.Dispose();
        _stop.Dispose();
    }
}
