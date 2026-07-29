using System.Runtime.Versioning;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Claros.Internal;

namespace Claros;

/// <summary>
/// A live, push-driven transcription session over the on-device Live Captions
/// recognizer. Audio is written as it arrives (16-bit mono PCM); the recognizer
/// streams an ever-growing, fully punctuated hypothesis through
/// <see cref="PartialUpdated"/> and <see cref="CurrentText"/>. The caller decides
/// where one spoken turn ends (for example when its own audio channel falls
/// silent) and calls <see cref="Commit"/> to finalize the text since the previous
/// commit as a discrete <see cref="TranscriptionSegment"/>.
///
/// This deliberately does not rely on the recognizer's built-in end-of-utterance
/// detector: on some devices that native finalizer is unstable, so the session
/// keeps the engine in a continuous streaming state and leaves turn segmentation
/// to the caller, which is also what a two-party "one channel per speaker" call
/// scenario wants.
///
/// Sessions are thread hostile: write audio and read text from one flow.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LiveTranscriptionSession : IDisposable
{
    private readonly PushAudioInputStream _push;
    private readonly AudioConfig _audio;
    private readonly SpeechRecognizer _recognizer;
    private readonly int _bytesPerSecond;
    private readonly object _gate = new();
    private readonly SentenceCommitter _sentences = new();

    private string _current = string.Empty;
    private string _committed = string.Empty;
    private TimeSpan _committedAt = TimeSpan.Zero;
    private long _bytesWritten;
    private bool _disposed;

    /// <summary>
    /// Raised on every in-progress hypothesis update with the full current text
    /// (already punctuated and capitalized). Successive values normally grow;
    /// the trailing words may be revised as more audio arrives.
    /// </summary>
    public event Action<string>? PartialUpdated;

    /// <summary>The full recognized text so far, across the whole session.</summary>
    public string CurrentText
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// How much audio (by duration) has been written into this session, useful as
    /// a timestamp for committed segments.
    /// </summary>
    public TimeSpan AudioPosition
    {
        get { lock (_gate) return TimeSpan.FromSeconds((double)_bytesWritten / _bytesPerSecond); }
    }

    internal LiveTranscriptionSession(EmbeddedSpeechConfig config, int sampleRate)
    {
        _bytesPerSecond = sampleRate * 2; // 16-bit mono
        var format = AudioStreamFormat.GetWaveFormatPCM((uint)sampleRate, 16, 1);
        _push = AudioInputStream.CreatePushStream(format);
        _audio = AudioConfig.FromStreamInput(_push);
        _recognizer = new SpeechRecognizer(config, _audio);
        _recognizer.Recognizing += OnRecognizing;
    }

    private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
    {
        var text = e.Result.Text;
        if (text.Length == 0) return;
        lock (_gate) _current = text;
        PartialUpdated?.Invoke(text);
    }

    internal Task StartAsync() => _recognizer.StartContinuousRecognitionAsync();

    /// <summary>Writes a block of 16-bit mono PCM audio at the session sample rate.</summary>
    public void Write(ReadOnlySpan<byte> pcm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pcm.Length == 0) return;
        var buffer = pcm.ToArray();
        _push.Write(buffer);
        lock (_gate) _bytesWritten += buffer.Length;
    }

    /// <summary>
    /// Finalizes the text recognized since the previous commit as a discrete
    /// segment, spanning from the previous commit to the current
    /// <see cref="AudioPosition"/>, and advances the commit marker. Returns
    /// <c>null</c> when no new text has accumulated. Call this when the speaker's
    /// turn ends (for example on channel silence).
    /// </summary>
    public TranscriptionSegment? Commit()
    {
        lock (_gate)
        {
            var current = _current;
            // The streaming hypothesis grows monotonically, but the engine may
            // revise earlier words (punctuation or capitalization) as more audio
            // arrives. Emit only the text past the common prefix with what was
            // already committed, so a late revision re-emits just the changed
            // tail rather than replaying the whole transcript.
            var delta = TranscriptDelta.Compute(_committed, current);
            var now = TimeSpan.FromSeconds((double)_bytesWritten / _bytesPerSecond);
            var start = _committedAt;
            _committed = current;
            _committedAt = now;
            if (delta.Length == 0) return null;
            return new TranscriptionSegment(delta, start, now - start);
        }
    }

    /// <summary>
    /// Returns the sentences that have fully completed since the previous call,
    /// each as a discrete <see cref="TranscriptionSegment"/> stamped with the
    /// current <see cref="AudioPosition"/>. This gives clean, whole-sentence chat
    /// lines (one bubble per utterance) by withholding the still-in-progress
    /// trailing fragment until it terminates. Poll it as audio streams in; pass
    /// <paramref name="flush"/> = <c>true</c> once at end of audio to also emit
    /// any remaining unterminated tail.
    /// </summary>
    public IReadOnlyList<TranscriptionSegment> CommitSentences(bool flush = false)
    {
        lock (_gate)
        {
            var newSentences = _sentences.Take(_current, flush);
            if (newSentences.Count == 0) return Array.Empty<TranscriptionSegment>();

            var at = TimeSpan.FromSeconds((double)_bytesWritten / _bytesPerSecond);
            var result = new TranscriptionSegment[newSentences.Count];
            for (var i = 0; i < newSentences.Count; i++)
            {
                result[i] = new TranscriptionSegment(newSentences[i], at, TimeSpan.Zero);
            }
            return result;
        }
    }

    /// <summary>
    /// Signals end of audio and disposes native resources. On some devices the
    /// native engine can fault during teardown; that is swallowed here. Prefer
    /// letting the process exit if you have measured or persisted the transcript.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer.Recognizing -= OnRecognizing;
        try { _push.Close(); } catch { /* best effort */ }
        try { _recognizer.Dispose(); } catch { /* native teardown may fault */ }
        try { _audio.Dispose(); } catch { /* best effort */ }
    }
}
