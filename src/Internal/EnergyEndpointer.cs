namespace WindowsNaturalVoices.Internal;

/// <summary>A state transition reported by <see cref="EnergyEndpointer"/>.</summary>
internal enum VadTransition
{
    /// <summary>No change this frame.</summary>
    None,

    /// <summary>Silence to speech: an utterance began.</summary>
    SpeechStarted,

    /// <summary>Speech to silence: the utterance ended.</summary>
    SpeechEnded,
}

/// <summary>
/// Pure energy endpointer: the debouncing state machine behind the energy VAD.
/// Fed one frame's RMS at a time, it accumulates continuous above-threshold time
/// to declare speech started (after <c>StartHangover</c>) and continuous
/// below-threshold time to declare it ended (after <c>EndHangover</c>). It holds
/// no audio and no timing, so it is fully unit-testable.
/// </summary>
internal sealed class EnergyEndpointer
{
    private readonly double _startThreshold;
    private readonly double _endThreshold;
    private readonly TimeSpan _startHangover;
    private readonly TimeSpan _endHangover;

    private bool _inSpeech;
    private TimeSpan _aboveFor; // continuous above-threshold time while in silence
    private TimeSpan _belowFor; // continuous below-threshold time while in speech

    public EnergyEndpointer(VoiceActivityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _startThreshold = options.StartThreshold;
        _endThreshold = options.EndThreshold;
        _startHangover = options.StartHangover;
        _endHangover = options.EndHangover;
    }

    /// <summary>Whether the endpointer currently considers audio to be speech.</summary>
    public bool IsSpeaking => _inSpeech;

    /// <summary>
    /// Feeds one frame's <paramref name="rms"/> covering <paramref name="frameDuration"/>
    /// and returns the resulting transition (at most one per frame).
    /// </summary>
    public VadTransition Process(double rms, TimeSpan frameDuration)
    {
        if (!_inSpeech)
        {
            if (rms >= _startThreshold)
            {
                _aboveFor += frameDuration;
                if (_aboveFor >= _startHangover)
                {
                    _inSpeech = true;
                    _belowFor = TimeSpan.Zero;
                    return VadTransition.SpeechStarted;
                }
            }
            else
            {
                _aboveFor = TimeSpan.Zero;
            }
            return VadTransition.None;
        }

        if (rms < _endThreshold)
        {
            _belowFor += frameDuration;
            if (_belowFor >= _endHangover)
            {
                _inSpeech = false;
                _aboveFor = TimeSpan.Zero;
                return VadTransition.SpeechEnded;
            }
        }
        else
        {
            _belowFor = TimeSpan.Zero;
        }
        return VadTransition.None;
    }

    /// <summary>Returns to the initial silence state, clearing all accumulators.</summary>
    public void Reset()
    {
        _inSpeech = false;
        _aboveFor = TimeSpan.Zero;
        _belowFor = TimeSpan.Zero;
    }
}
