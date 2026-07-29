namespace Claros;

/// <summary>
/// A word (or punctuation token) reported during live synthesis through
/// <see cref="EmbeddedVoiceSpeaker.SpeakToDefaultOutputAsync"/>. <see cref="Offset"/>
/// is the word's position within the utterance's audio (measured from the start
/// of the utterance), not a wall-clock playback time: the runtime raises these
/// events as audio is produced, which can run ahead of the audible output.
/// </summary>
public readonly record struct SpokenWord(string Text, TimeSpan Offset, TimeSpan Duration);
