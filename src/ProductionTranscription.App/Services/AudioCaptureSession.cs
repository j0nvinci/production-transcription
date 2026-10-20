using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>A single selected device, with independent source meters; no audio recording/storage.</summary>
public sealed class AudioCaptureSession : IDisposable
{
    private AsioOut? _asio;
    private WasapiCapture? _wasapi;
    private readonly ConcurrentDictionary<int, float> _peaks = new();
    private int _channels;
    private bool _running;
    public event Action<string>? CaptureError;

    public void Start(AudioBackend backend, string deviceId, IReadOnlyCollection<SourceConfig> sources)
    {
        if (_running) throw new InvalidOperationException("Capture is already active.");
        if (sources.Count == 0 || sources.All(s => !s.Enabled))
            throw new InvalidOperationException("Enable at least one source.");
        if (sources.Any(s => s.Enabled && s.InputNumber < 1))
            throw new InvalidOperationException("Input numbers must start at 1.");
        _peaks.Clear();
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
                _asio.InitRecordAndPlayback(null, count, 48000);
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
        for (int index = 0; index < data.Length; index++)
            Observe(index % _channels, data[index]);
        e.WrittenToOutputBuffers = false;
    }

    private void OnWasapiAudio(object? sender, WaveInEventArgs e)
    {
        if (_wasapi is null) return;
        var format = _wasapi.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int frameSize = format.BlockAlign;
        for (int offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
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
            }
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
    }
    public void Dispose() => Stop();
}
