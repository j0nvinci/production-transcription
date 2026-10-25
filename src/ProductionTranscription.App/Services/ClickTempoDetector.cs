using System;
using System.Collections.Generic;
using System.Linq;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Low-cost, mono click-track tempo estimator. Processes the same PCM blocks as speech
/// recognition; voice transients can disrupt detection, so confidence is deliberately conservative.
/// All calls for a given instance must be serialized by the caller.
/// </summary>
public sealed class ClickTempoDetector
{
    private readonly Queue<double> _onsets = new();
    private long _samples;
    private int _rate;
    private double _envelope;
    private double _previousEnvelope;
    private double _noiseFloor = 0.002;
    private double _lastOnset = -10;
    private double _lastStable = -10;
    private double _bpm;
    private int _consistent;
    public double Bpm => _bpm;
    public bool Locked => _consistent >= 4 && (_samples / (double)Math.Max(_rate, 1) - _lastStable) < 3;
    public string Status => _bpm <= 0 ? "Detecting" : Locked ? "Locked" : "Click lost / detecting";

    public void Process(float[] samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length == 0) return;
        if (_rate != sampleRate)
        {
            _rate = sampleRate; _samples = 0; _onsets.Clear();
            _envelope = _previousEnvelope = 0; _noiseFloor = 0.002;
            _lastOnset = _lastStable = -10; _bpm = 0; _consistent = 0;
        }
        // 5ms peak envelope windows: inexpensive, independent of buffer size.
        int window = Math.Max(1, sampleRate / 200);
        for (int i = 0; i < samples.Length;)
        {
            int count = Math.Min(window, samples.Length - i);
            double peak = 0;
            for (int j = 0; j < count; j++)
                peak = Math.Max(peak, Math.Abs(samples[i + j]));
            i += count;
            _samples += count;
            double now = _samples / (double)_rate;
            _noiseFloor = 0.995 * _noiseFloor + 0.005 * Math.Min(peak, _noiseFloor * 3 + 0.005);
            _envelope = 0.35 * peak + 0.65 * _envelope;
            bool transient = _envelope > Math.Max(0.015, _noiseFloor * 3.2) &&
                             _envelope > _previousEnvelope * 1.65 &&
                             now - _lastOnset >= 0.22;
            _previousEnvelope = _envelope;
            if (!transient) continue;
            _lastOnset = now;
            _onsets.Enqueue(now);
            while (_onsets.Count > 16) _onsets.Dequeue();
            if (_onsets.Count < 5) continue;
            var times = _onsets.ToArray();
            var intervals = new List<double>();
            for (int k = 1; k < times.Length; k++)
            {
                double delta = times[k] - times[k - 1];
                if (delta >= 60.0 / 220 && delta <= 60.0 / 50) intervals.Add(delta);
            }
            if (intervals.Count < 4) continue;
            intervals.Sort();
            double median = intervals[intervals.Count / 2];
            int matching = intervals.Count(d => Math.Abs(d - median) < Math.Max(0.035, median * 0.075));
            if (matching < 4 || matching < intervals.Count * 0.7) { _consistent = 0; continue; }
            double bpm = 60.0 / median;
            if (bpm < 50 || bpm > 220) continue;
            if (_bpm > 0 && Math.Abs(_bpm - bpm) > Math.Max(3, _bpm * 0.04)) _consistent = 0;
            _bpm = Math.Round(bpm, 1);
            _consistent = Math.Min(16, _consistent + 1);
            if (_consistent >= 4) _lastStable = now;
        }
    }
}
