namespace WindowsNaturalVoices;

/// <summary>
/// Base type for every error the library raises for expected, actionable
/// failure modes (a voice is not installed, a voice package is malformed, or
/// synthesis fails at runtime). Catching this type lets callers handle all
/// library-specific failures without also swallowing programming errors such
/// as <see cref="ArgumentNullException"/>.
/// </summary>
public abstract class NaturalVoiceException : Exception
{
    private protected NaturalVoiceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A requested voice, or one or more of the model files it needs, is not
/// present on this machine. Typically means the neural Natural Voice was not
/// installed through Settings &gt; Time &amp; language &gt; Speech.
/// </summary>
public sealed class NaturalVoiceUnavailableException : NaturalVoiceException
{
    /// <summary>Initializes the exception with a message and optional inner exception.</summary>
    /// <param name="message">A description of the missing voice, model file, runtime component, or license.</param>
    /// <param name="innerException">The underlying failure, when one is available.</param>
    public NaturalVoiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A voice package exists on disk but its contents could not be parsed or
/// loaded: the model binary carried no recognizable ONNX payload, the phoneme
/// table was malformed, or the vocoder used operators the library cannot
/// rewrite. Indicates a format the library does not understand rather than a
/// missing install.
/// </summary>
public sealed class VoicePackageFormatException : NaturalVoiceException
{
    /// <summary>Initializes the exception with a message and optional inner exception.</summary>
    /// <param name="message">A description of what could not be parsed or loaded.</param>
    /// <param name="innerException">The underlying parse or load failure, when one is available.</param>
    public VoicePackageFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Synthesis failed while running the acoustic model or vocoder. The
/// <see cref="System.Exception.InnerException"/> carries the underlying ONNX
/// Runtime or SAPI error when one is available.
/// </summary>
public sealed class SpeechSynthesisException : NaturalVoiceException
{
    /// <summary>Initializes the exception with a message and optional inner exception.</summary>
    /// <param name="message">A description of the synthesis failure.</param>
    /// <param name="innerException">The underlying ONNX Runtime, SAPI, or Embedded Speech error, when one is available.</param>
    public SpeechSynthesisException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
