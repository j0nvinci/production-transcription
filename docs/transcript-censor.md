# Transcript profanity and slur masking

Live partial results and finalized chat messages are both masked before rendering in
the main transcript feed. This is display-only: audio is not modified.

On first launch, the program creates
`%LocalAppData%\\ProductionTranscription\\censored-terms.txt`.
Edit this UTF-8 text file to add or remove blocked words or multiword phrases
(one per line) and restart the application to reload the terms.
The list is local and is not uploaded.

The filter matches complete words or phrases (case-insensitively) and replaces
matched characters with bullet symbols. This avoids masking inside ordinary words.
It cannot guarantee detection of every slur, inflection, unusual spelling,
phonetic variation, or recognition error. Transcripts can still contain language
not covered by the configured word list.
