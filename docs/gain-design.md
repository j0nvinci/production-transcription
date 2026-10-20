# Per-source digital input gain

Each logical source owns a GainDb setting, persisted with the source configuration.
Range: -24 dB to +30 dB, default 0 dB (unity).

Conversion to linear amplitude: gain = 10^(dB / 20).

In v0.2, the gain affects each source's **displayed meter** only, using the independently captured raw input peak. It does **not** alter Windows/ASIO driver levels or the audio received by other applications. No transcription pipeline exists yet. When CPU ASR is integrated, apply gain to each logical source's resampled PCM before VAD and inference and use saturation/overload reporting as appropriate.

This approach deliberately keeps gain per logical source instead of per physical input: two sources mapped to the same input may have different gain.

Software gain cannot recover samples clipped at the hardware interface.
