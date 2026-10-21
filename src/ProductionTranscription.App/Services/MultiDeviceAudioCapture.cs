using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>
/// One ASIO driver, or one shared-mode WASAPI capture per distinct endpoint.
/// Each source retains an independent endpoint/channel mapping.
/// </summary>
public sealed class AudioCaptureSession : IDisposable
{
    private readonly List<SingleDeviceCaptureSession> _sessions = new();
    private readonly Dictionary<SourceConfig, SingleDeviceCaptureSession> _sourceSessions = new();
    private bool _running;
    private bool _stopping;
    public event Action<string>? CaptureError;
    public event Action<SourceConfig, float[], int>? SourceAudioAvailable;

    public void Start(AudioBackend backend, string defaultDeviceId, IReadOnlyCollection<SourceConfig> sources)
    {
        if (_running || _sessions.Count != 0)
            throw new InvalidOperationException("Capture is already active.");
        var enabled = sources.Where(s => s.Enabled).ToArray();
        if (enabled.Length == 0) throw new InvalidOperationException("Enable at least one source.");
        _stopping = false;
        try
        {
            var groups = backend == AudioBackend.Asio
                ? new[] { (DeviceId: defaultDeviceId, Sources: enabled) }
                : enabled.GroupBy(s => string.IsNullOrWhiteSpace(s.WasapiDeviceId)
                        ? defaultDeviceId : s.WasapiDeviceId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (DeviceId: g.Key, Sources: g.ToArray())).ToArray();

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.DeviceId))
                    throw new InvalidOperationException("Select a recording device for every enabled source.");
                var session = new SingleDeviceCaptureSession();
                string label = group.DeviceId;
                session.CaptureError += error =>
                {
                    if (!_stopping) CaptureError?.Invoke(label + ": " + error);
                };
                session.SourceAudioAvailable += (source, samples, rate) =>
                    SourceAudioAvailable?.Invoke(source, samples, rate);
                _sessions.Add(session);
                foreach (var source in group.Sources) _sourceSessions.Add(source, session);
                session.Start(backend, group.DeviceId, group.Sources);
            }
            _running = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public float TakePeak(SourceConfig source) =>
        _sourceSessions.TryGetValue(source, out var session)
            ? session.TakePeak(source.InputNumber) : 0;

    public void Stop()
    {
        _stopping = true;
        _running = false;
        foreach (var session in _sessions)
        {
            try { session.Dispose(); }
            catch (Exception) { /* Continue releasing remaining endpoints. */ }
        }
        _sessions.Clear();
        _sourceSessions.Clear();
    }

    public void Dispose() => Stop();
}
