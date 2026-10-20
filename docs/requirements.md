# Requirements v0.5

## MUST
- Proper compiled Windows application, not scripts.
- Global ASIO or WDM/WASAPI capture backend toggle; simultaneous backend use not required.
- At least four independent speech sources.
- Select any exposed physical input number for each logical transcription source, including noncontiguous inputs (e.g. MD→7, FOH→3, Camera Director→9).
- Change labels dynamically; device mappings and settings survive restarts.
- Modern responsive UI from banner to full screen; dark/light mode.
- Offline local CPU-only speech recognition; default Balanced profile, selectable Speed First and Accuracy First.
- Unified chronological transcript with accurate capture timestamps and confidence indications.
- Capture or inference failure must not hang other sources or interface.

## NICE
- HTTP/WebSocket API, browser viewer, optional cloud processing.
- SQLite persistence of messages; initial history lasts only for current app session.
- Clear transcript and per-channel mute.

## Engineering acceptance tests
1. Four channels captured independently from a compatible device.
2. Noncontiguous mappings work without assuming a particular manufacturer.
3. Other channels continue when one input fails.
4. ASIO driver contention is detected and reported.
5. Confidence does not claim uncalibrated accuracy percentages.
6. CPU overload and lost audio must be surfaced, never concealed.
7. No connectivity needed for local speech recognition.
8. Settings are saved even though transcript messages are session-only.

## Implementation phases
0.1 WPF application foundation, source-mapping settings and automated Windows builds.
0.2 Device enumeration, actual ASIO/WASAPI capture, source-level meters.
0.3 Streaming CPU ASR, unified chronological transcript.
0.4 Themes, confidence validation, performance optimization and distribution.
