# API reference

The full public surface of `Claros`.

## Entry points

- <xref:Claros.VoiceCatalog>: enumerate the installed Windows Natural Voices and watch for changes.
- <xref:Claros.NaturalVoiceSynthesizer>: transparent, license-free pipeline (SAPI frontend plus on-device ONNX acoustic model and vocoder).
- <xref:Claros.EmbeddedSpeechSynthesizer>: highest-fidelity path that reuses the on-device Azure Embedded Speech runtime, including live streaming to the default output.
- <xref:Claros.WaveFile>: write mono 16-bit PCM WAV files.

Browse the namespaces in the table of contents on the left for every type and member.
