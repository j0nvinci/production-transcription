# Whisper rolling-window mode (experimental)

Whisper is now loaded **once per capture session** through Whisper.net and its
CPU-only native runtime. There is no longer a need to install whisper-cli.exe
or any tools/ runtime. Place compatible ggml-*.bin model files under models/.
Select the Whisper engine and model from Settings.

Each source accumulates up to 12 seconds of speech and requests an updating
partial transcription approximately every 2 seconds; partials use up to 5 seconds
of recent audio. At 750ms of silence (or 12s utterance limit), the complete
utterance is decoded and replaces its provisional chat bubble.

Processing is CPU-only, bounded, and inference is serialized across sources to
avoid simultaneous high-CPU workloads. Actual partial latency depends on the
CPU and model size; several active channels can lag. Whisper may hallucinate on
background sounds despite the per-source RMS noise gates.

Audio remains in memory and is never uploaded or written to temporary WAV files.
Vosk is unchanged. This experimental mode requires Windows compatible with the
Whisper.net CPU runtime and sufficient AVX support. Test with tiny.en first.
