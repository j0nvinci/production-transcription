using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>A single selected device, with independent source meters; no audio recording/storage.</summary>
public sealed class SingleDeviceCaptureSession : IDisposable
{
    private AsioOut? _asio;
    private WasapiCapture? _wasapi;
    private readonly ConcurrentDictionary<int, float> _peaks = new();
    private int _channels;
    private bool _running;
    public event Action<string>? CaptureError;
    /// <summary>Raised from the capture callback with an independent, gained mono block for each enabled source.</summary>
    public event Action<SourceConfig, float[], int>? SourceAudioAvailable;
    private SourceConfig[] _sources = Array.Empty<SourceConfig>();
    private int _sampleRate;

    public void Start(AudioBackend backend, string deviceId, IReadOnlyCollection<SourceConfig> sources)
    {
        if (_running) throw new InvalidOperationException("Capture is already active.");
        if (sources.Count == 0 || sources.All(s => !s.Enabled))
            throw new InvalidOperationException("Enable at least one source.");
        if (sources.Any(s => s.Enabled && s.InputNumber < 1))
            throw new InvalidOperationException("Input numbers must start at 1.");
        _peaks.Clear();
        _sources = sources.Where(s => s.Enabled).ToArray();
        try
        {
            if (backend == AudioBackend.Asio)
            {
                _asio = new AsioOut(deviceId);
                int count = sources.Where(s => s.Enabled).Max(s => s.InputNumber);
                if (count > _asio.DriverInputChannelCount)
                    throw new InvalidOperationException($"Requested input {count}, but the ASIO device has only {_asio.DriverInputChannelCount} inputs.");
                _channels = count; // ASIO captures channels 1..highest used, while meters only track mapped channels.
                // 48 kHz is the initial capture rate; device-rate negotiation is a follow-up.
                _sampleRate = 48000;
                _asio.InitRecordAndPlayback(null, count, _sampleRate);
                _asio.AudioAvailable += OnAsioAudio;
                _asio.PlaybackStopped += OnAsioStopped;
                _asio.Play();
            }
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoint = enumerator.GetDevice(deviceId);
                _wasapi = new WasapiCapture(endpoint);
                _channels = _wasapi.WaveFormat.Channels;
                _sampleRate = _wasapi.WaveFormat.SampleRate;
                int highest = sources.Where(s => s.Enabled).Max(s => s.InputNumber);
                if (highest > _channels)
                    throw new InvalidOperationException($"Requested input {highest}, but the WASAPI endpoint exposes only {_channels} channels.");
                ValidateWasapiFormat(_wasapi.WaveFormat);
                _wasapi.DataAvailable += OnWasapiAudio;
                _wasapi.RecordingStopped += OnWasapiStopped;
                _wasapi.StartRecording();
            }
            _running = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public float TakePeak(int channelNumber)
    {
        int channel = channelNumber - 1;
        return _peaks.TryRemove(channel, out float peak) ? peak : 0;
    }

    private void Observe(int channel, float amplitude)
    {
        if (channel < 0 || channel >= _channels) return;
        float peak = Math.Clamp(Math.Abs(amplitude), 0f, 1f);
        _peaks.AddOrUpdate(channel, peak, (_, previous) => Math.Max(previous, peak));
    }

    private void OnAsioAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        // NAudio converts the driver's native buffers to interleaved normalized floats.
        float[] data = e.GetAsInterleavedSamples();
        ProcessInterleaved(data, data.Length / _channels);
        e.WrittenToOutputBuffers = false;
    }

    private void OnWasapiAudio(object? sender, WaveInEventArgs e)
    {
        if (_wasapi is null) return;
        var format = _wasapi.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int frameSize = format.BlockAlign;
        int frameCount = e.BytesRecorded / frameSize;
        var blocks = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++) blocks[ch] = new float[frameCount];
        for (int offset = 0, frame = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize, frame++)
        {
            for (int ch = 0; ch < format.Channels; ch++)
            {
                int sampleOffset = offset + ch * bytesPerSample;
                float sample = format.BitsPerSample switch
                {
                    32 when IsFloat(format) => BitConverter.ToSingle(e.Buffer, sampleOffset),
                    16 => BitConverter.ToInt16(e.Buffer, sampleOffset) / 32768f,
                    24 => Sample24(e.Buffer, sampleOffset),
                    32 => BitConverter.ToInt32(e.Buffer, sampleOffset) / 2147483648f,
                    _ => 0
                };
                Observe(ch, sample);
                blocks[ch][frame] = sample;
            }
        }
        PublishBlocks(blocks);
    }
    private void ProcessInterleaved(float[] samples, int frames)
    {
        var blocks = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++) blocks[ch] = new float[frames];
        for (int frame = 0; frame < frames; frame++)
            for (int ch = 0; ch < _channels; ch++)
            {
                float sample = samples[frame * _channels + ch];
                Observe(ch, sample);
                blocks[ch][frame] = sample;
            }
        PublishBlocks(blocks);
    }
    private void PublishBlocks(float[][] channelBlocks)
    {
        var handler = SourceAudioAvailable;
        if (handler is null) return;
        foreach (var source in _sources)
        {
            int channel = source.InputNumber - 1;
            if (channel < 0 || channel >= channelBlocks.Length) continue;
            var raw = channelBlocks[channel];
            var gained = new float[raw.Length];
            float factor = (float)Math.Pow(10, Math.Clamp(source.GainDb, -24, 30) / 20);
            for (int i = 0; i < raw.Length; i++)
                gained[i] = Math.Clamp(raw[i] * factor, -1f, 1f);
            handler(source, gained, _sampleRate);
        }
    }
    private static float Sample24(byte[] b, int i)
    {
        int raw = b[i] | b[i + 1] << 8 | b[i + 2] << 16;
        if ((raw & 0x800000) != 0) raw |= unchecked((int)0xFF000000);
        return raw / 8388608f;
    }
    private static bool IsFloat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        format is WaveFormatExtensible extended &&
        extended.SubFormat == MediaFoundationFloatGuid;

    private static readonly Guid MediaFoundationFloatGuid =
        new("00000003-0000-0010-8000-00AA00389B71");

    private static void ValidateWasapiFormat(WaveFormat format)
    {
        if (format.BitsPerSample is not (16 or 24 or 32))
            throw new NotSupportedException($"Unsupported WASAPI format: {format}");
        if (format.Encoding != WaveFormatEncoding.Pcm && !IsFloat(format) &&
            format is not WaveFormatExtensible)
            throw new NotSupportedException($"Unsupported WASAPI encoding: {format.Encoding}");
    }

    private void OnAsioStopped(object? sender, StoppedEventArgs e) =>
        CaptureError?.Invoke(e.Exception?.Message ?? "ASIO capture stopped unexpectedly.");
    private void OnWasapiStopped(object? sender, StoppedEventArgs e) =>
        CaptureError?.Invoke(e.Exception?.Message ?? "WASAPI capture stopped unexpectedly.");

    public void Stop()
    {
        _running = false;
        if (_asio != null)
        {
            _asio.AudioAvailable -= OnAsioAudio;
            _asio.PlaybackStopped -= OnAsioStopped;
            try { _asio.Stop(); } catch { }
            _asio.Dispose(); _asio = null;
        }
        if (_wasapi != null)
        {
            _wasapi.DataAvailable -= OnWasapiAudio;
            _wasapi.RecordingStopped -= OnWasapiStopped;
            try { _wasapi.StopRecording(); } catch { }
            _wasapi.Dispose(); _wasapi = null;
        }
        _peaks.Clear();
        _sources = Array.Empty<SourceConfig>();
    }
    public void Dispose() => Stop();
}
