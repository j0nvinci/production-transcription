# Groq Cloud Whisper mode (opt-in)
This engine requires a personal API key from https://console.groq.com/keys.
Select "Groq — cloud Whisper" in Settings, paste the key, select either
Whisper Large V3 Turbo or Whisper Large V3, and press Start.

The API key is protected with Windows DPAPI for the current user in local
application data (not in app settings or version control).

**Privacy:** Only selecting and starting cloud mode uploads audio to Groq.
Each enabled source sends speech segments as in-memory WAV uploads via HTTPS.
Local Vosk/Whisper engines remain offline. No local audio recordings are kept.

Groq's API is batch transcription, not truly streaming speech-to-text:
messages appear after approximately 750 ms of silence or 12 s of speech,
plus network and inference latency. Free accounts have variable per-minute,
per-day, and audio-duration limits. Rate-limit errors are shown in the status.
Avoid using four open continuous channels without checking your limits.
