# Experimental offline Whisper support

Use official CPU-compatible Windows whisper.cpp binaries from
https://github.com/ggml-org/whisper.cpp/releases .
Put whisper-cli.exe **and accompanying DLLs** in tools/ beside the application.
Download a compatible whisper.cpp ggml-tiny.en.bin or ggml-base.en.bin model and
place the file in models/ beside the application. In Settings, select the
Whisper engine, refresh models, choose a file, and start capture.

Whisper recognition uses the existing independent per-source PCM streams and
noise gates. Segments end after ~0.75 s silence or 12 s maximum. Each segment
is temporarily written as 16 kHz mono WAV, passed to local whisper-cli in
CPU mode, then deleted. CPU execution is serialized to avoid competing
Whisper processes across multiple channels.

IMPORTANT: this is a prototype CLI adapter, not true streaming Whisper.
There are no partial words; messages appear only after segmentation plus
Whisper processing. Process creation and reloading models for each segment
may be slow. Vosk remains the more efficient live option. Later improve
with a persistent Whisper runtime, proper resampling, preroll and backlog
reporting. No audio is sent to cloud services.
