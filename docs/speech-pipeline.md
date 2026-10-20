# Speech pipeline — integration foundation

This incremental change exposes per-source, gain-adjusted PCM samples from the capture engine.
`AudioCaptureSession.SourceAudioAvailable` emits mono float blocks tagged with the logical source and actual sample rate.
Each subscriber must return promptly from the audio callback; enqueue into a bounded worker queue
rather than run inference or large allocations on the callback thread.

`SpeechSegmenter` is a lightweight **energy-based boundary detector** (not learned VAD, and not a speech recognizer).
It provides short preroll, a silence endpoint, a 20-second cap, and an utterance-ready event.
RMS thresholds are provisional and must be tested against low-level comms, music bleed, and gain settings.

Next: worker queue, resampling to the chosen model input rate, local CPU model deployment,
independent streaming recognition state, confidence reporting and combined conversation UI.

Nothing in this commit uploads, stores or transcribes audio. Device-agnostic capture remains unchanged.
