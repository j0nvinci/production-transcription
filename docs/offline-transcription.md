# v0.3 local transcription preview

Download the English Vosk small model from https://alphacephei.com/vosk/models
(`vosk-model-small-en-us-0.15`), extract the **model directory** into
`models/vosk-model-small-en-us-0.15/` next to ProductionTranscription.exe.
The download is a one-time manual operation; audio is not uploaded.

Start capture after the model is installed. Recognized finalized utterances appear
chronologically in the live transcript pane, tagged with their source names.

**Limitations of the preview:** the Vosk small model favors low CPU usage over
accuracy. Linear block-wise resampling is provisional, particularly for unusual
sample rates. Capture workers use bounded queues and can drop old audio if CPU
is overloaded. No persistence or partial-text display yet. Confidence scoring
and model/profile selection are not implemented. Full performance tuning and
native-code validation on ASIO hardware remain necessary.
