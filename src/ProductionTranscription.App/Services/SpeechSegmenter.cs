using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Speech-boundary detector. Produces audio utterances; does NOT perform speech recognition.
/// Caller must supply chronological mono samples for one source. Each source gets its own instance.
/// </summary>
public sealed class SpeechSegmenter
{
    private readonly List<float> _speech = new();
    private readonly Queue<float> _preroll = new();
    private readonly int _sampleRate;
    private readonly int _maximumSamples;
    private readonly int _prerollSamples;
    private readonly int _endSilenceSamples;
    private readonly int _minimumSpeechSamples;
    private int _silenceSamples;
    private int _activeSpeechSamples;
    private bool _active;
    private const float RmsThreshold = 0.018f;
    public event Action<float[], int>? UtteranceReady;

    public SpeechSegmenter(int sampleRate)
    {
        if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
        _maximumSamples = sampleRate * 20;
        _prerollSamples = sampleRate / 5;
        _endSilenceSamples = sampleRate * 3 / 4;
        _minimumSpeechSamples = sampleRate / 4;
    }

    public void AddSamples(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        double energy = 0;
        foreach (float sample in samples) energy += sample * sample;
        bool voiced = Math.Sqrt(energy / samples.Length) > RmsThreshold;
        if (!_active && voiced)
        {
            _active = true;
            _speech.AddRange(_preroll);
            _preroll.Clear();
        }
        if (_active)
        {
            foreach (float sample in samples) _speech.Add(sample);
            if (voiced) { _silenceSamples = 0; _activeSpeechSamples += samples.Length; }
            else _silenceSamples += samples.Length;
            if (_silenceSamples >= _endSilenceSamples || _speech.Count >= _maximumSamples)
                Flush();
        }
        else
        {
            foreach (float sample in samples)
            {
                _preroll.Enqueue(sample);
                if (_preroll.Count > _prerollSamples) _preroll.Dequeue();
            }
        }
    }

    public void Flush()
    {
        if (_active && _activeSpeechSamples >= _minimumSpeechSamples)
            UtteranceReady?.Invoke(_speech.ToArray(), _sampleRate);
        _speech.Clear();
        _preroll.Clear();
        _active = false;
        _silenceSamples = 0;
        _activeSpeechSamples = 0;
    }
}
