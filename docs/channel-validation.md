# Channel mapping validation

After a selected device is enumerated, the application probes its input-channel count automatically while capture is stopped. ASIO drivers may refuse to open if another application owns the device; in that case the UI reports that the count is unknown, and capture-time validation remains authoritative.

Invalid typed input values are shown in red with a hover/focus tooltip, without modal interruptions. The invalid text is not committed to source settings. Capture is blocked while a visible invalid input remains. Settings persistence and device-agnostic mapping are unchanged.

This change does not introduce a transcription engine. The next core milestone remains background speech processing with a CPU model and unified transcript.
