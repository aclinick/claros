using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class IpaPhonemeMapTests
{
    [Fact]
    public void Convert_AppendsStressDigitToStressedVowels()
    {
        Assert.Equal("iy1", IpaPhonemeMap.Convert("i", stressed: true));
        Assert.Equal("iy", IpaPhonemeMap.Convert("i", stressed: false));
    }

    [Fact]
    public void Convert_DoesNotStressConsonants()
    {
        // s is a consonant; the stress flag must not add a digit.
        Assert.Equal("s", IpaPhonemeMap.Convert("s", stressed: true));
    }

    [Theory]
    [InlineData("\u00f0", "dh")] // ð
    [InlineData("\u03b8", "th")] // θ
    [InlineData("\u014b", "ng")] // ŋ
    [InlineData("t\u0361\u0283", "ch")] // t͡ʃ affricate with tie bar
    [InlineData("a\u0361\u026a", "ay")] // a͡ɪ diphthong
    public void Convert_MapsKnownIpaSymbols(string ipa, string expected)
    {
        Assert.Equal(expected, IpaPhonemeMap.Convert(ipa, stressed: false));
    }

    [Fact]
    public void Convert_ReturnsNullForUnmappedSymbol()
    {
        // U+0004 is SAPI's inter-word silence marker; it has no ARPABET mapping.
        Assert.Null(IpaPhonemeMap.Convert("\u0004", stressed: false));
        Assert.Null(IpaPhonemeMap.Convert("QQ", stressed: true));
    }

    [Fact]
    public void Vowels_ContainsExpectedNucleiButNotConsonants()
    {
        Assert.Contains("iy", IpaPhonemeMap.Vowels);
        Assert.Contains("er", IpaPhonemeMap.Vowels);
        Assert.DoesNotContain("s", IpaPhonemeMap.Vowels);
        Assert.DoesNotContain("ng", IpaPhonemeMap.Vowels);
    }

    [Fact]
    public void IpaToArpa_MapsBothIpaAndAsciiGToG()
    {
        Assert.Equal("g", IpaPhonemeMap.IpaToArpa["\u0261"]); // ɡ
        Assert.Equal("g", IpaPhonemeMap.IpaToArpa["g"]);       // ASCII g
    }
}
