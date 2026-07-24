using Windows.Speech.Internal;

namespace Windows.Speech.Tests;

public class IniEditorTests
{
    [Fact]
    public void SetValue_ReplacesExistingKeyInSection()
    {
        var ini = "[Pipeline]\nEnableHDVoice=yes\nHDVoiceThreshold=10\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("HDVoiceThreshold=0", result);
        Assert.DoesNotContain("HDVoiceThreshold=10", result);
        Assert.Contains("EnableHDVoice=yes", result);
    }

    [Fact]
    public void SetValue_MatchesKeyAndSectionCaseInsensitively()
    {
        var ini = "[pipeline]\nhdvoicethreshold=10\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("HDVoiceThreshold=0", result);
        Assert.DoesNotContain("=10", result);
    }

    [Fact]
    public void SetValue_PreservesCrlfLineEndings()
    {
        var ini = "[Pipeline]\r\nHDVoiceThreshold=10\r\nEnableBuffer=false\r\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("HDVoiceThreshold=0\r\n", result);
        Assert.Contains("EnableBuffer=false\r\n", result);
    }

    [Fact]
    public void SetValue_OnlyEditsMatchingSection()
    {
        var ini = "[Output]\nHDVoiceThreshold=99\n[Pipeline]\nHDVoiceThreshold=10\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("[Output]\nHDVoiceThreshold=99", result);
        Assert.Contains("[Pipeline]\nHDVoiceThreshold=0", result);
    }

    [Fact]
    public void SetValue_AppendsKeyWhenSectionExistsWithoutKey()
    {
        var ini = "[Pipeline]\nEnableHDVoice=yes\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("HDVoiceThreshold=0", result);
        Assert.Contains("EnableHDVoice=yes", result);
    }

    [Fact]
    public void SetValue_CreatesSectionWhenAbsent()
    {
        var ini = "[Output]\nEnableBuffer=false\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        Assert.Contains("[Pipeline]", result);
        Assert.Contains("HDVoiceThreshold=0", result);
        Assert.Contains("[Output]", result);
    }

    [Fact]
    public void SetValue_DoesNotMatchKeyInDifferentSection()
    {
        var ini = "[Pipeline]\nEnableHDVoice=yes\n[Other]\nHDVoiceThreshold=10\n";

        var result = IniEditor.SetValue(ini, "Pipeline", "HDVoiceThreshold", "0");

        // Pipeline gets the key appended; the Other section's value is untouched.
        Assert.Contains("[Other]\nHDVoiceThreshold=10", result);
        Assert.Contains("[Pipeline]\nEnableHDVoice=yes\nHDVoiceThreshold=0", result);
    }

    [Fact]
    public void SetValue_ThrowsOnNullContent() =>
        Assert.Throws<ArgumentNullException>(() => IniEditor.SetValue(null!, "s", "k", "v"));

    [Fact]
    public void SetValue_ThrowsOnEmptySectionOrKey()
    {
        Assert.Throws<ArgumentException>(() => IniEditor.SetValue("x", "", "k", "v"));
        Assert.Throws<ArgumentException>(() => IniEditor.SetValue("x", "s", "", "v"));
    }
}
