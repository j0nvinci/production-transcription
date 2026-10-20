# Production Transcription

A Windows desktop application for independent, real-time transcription of multiple production-communications audio inputs.

**Status:** Foundation build only. Audio capture and speech recognition are **not implemented**; ASIO/WASAPI device enumeration is implemented in this release.

## Core decisions
- Generic audio-device architecture; not tied to Dante or DVS
- Globally selected ASIO **or** WDM/WASAPI mode
- Noncontiguous physical input mapping to logical named sources, at least four
- CPU-only local, offline recognition required; optional cloud integration later
- Default transcription profile: **Balanced** (adjustable in Settings)
- Unified chronological feed, responsive full-screen/banner UI, dark/light themes
- Session-based messages; configuration persisted across restarts

## Build
Requires .NET 10 SDK on Windows.

```powershell
dotnet build src/ProductionTranscription.App/ProductionTranscription.App.csproj -c Release
dotnet run --project src/ProductionTranscription.App
```

GitHub Actions produces a self-contained Windows x64 build artifact.

See [requirements](docs/requirements.md).

Device dropdowns are populated from installed ASIO drivers or active WASAPI capture endpoints. Physical channel mappings remain configurable separately.
