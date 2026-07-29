namespace Claros.Tests;

public class ExceptionsTests
{
    [Fact]
    public void AllLibraryExceptions_DeriveFromNaturalVoiceException()
    {
        Assert.IsAssignableFrom<NaturalVoiceException>(new NaturalVoiceUnavailableException("x"));
        Assert.IsAssignableFrom<NaturalVoiceException>(new VoicePackageFormatException("x"));
        Assert.IsAssignableFrom<NaturalVoiceException>(new SpeechSynthesisException("x"));
        Assert.IsAssignableFrom<NaturalVoiceException>(new SpeechRecognitionException("x"));
    }

    [Fact]
    public void SpeechRecognitionException_IsDistinctFromSynthesis()
    {
        // Recognition failures must not surface as a synthesis exception; callers
        // catching one must not accidentally swallow the other.
        Assert.IsNotType<SpeechSynthesisException>(new SpeechRecognitionException("x"));
        Assert.IsNotType<SpeechRecognitionException>(new SpeechSynthesisException("x"));
    }

    [Fact]
    public void NaturalVoiceException_IsAnException()
    {
        Assert.IsAssignableFrom<Exception>(new VoicePackageFormatException("x"));
    }

    [Fact]
    public void Exceptions_PreserveMessageAndInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new VoicePackageFormatException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
