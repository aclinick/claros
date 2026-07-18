using System.Text;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class EmbeddedSpeechLicenseTests
{
    private const string Notice =
        "This model and the software may not be used except under a written agreement, " +
        "reference number 2774316. You may not share, publish, rent, or lease the model " +
        "or software, or provide the model or software as a standalone solution for others to use.";

    private static void WriteBin(string path, byte[] content) => File.WriteAllBytes(path, content);

    [Fact]
    public void ResolveFromPackage_ReadsNoticeFromModelBin()
    {
        using var dir = TempDir.Create();
        // Notice at the head, followed by binary model bytes.
        var bytes = Encoding.ASCII.GetBytes(Notice).Concat(new byte[] { 0x00, 0x11, 0x22 }).ToArray();
        WriteBin(Path.Combine(dir.Path, "am_v5_decoder.bin"), bytes);

        var license = EmbeddedSpeechLicense.ResolveFromPackage(dir.Path);

        Assert.Equal(Notice, license);
    }

    [Fact]
    public void ResolveFromPackage_StripsTrailingPrintableHashAfterMarker()
    {
        using var dir = TempDir.Create();
        // Real packages append a printable hash after the end marker; it must be cut.
        var bytes = Encoding.ASCII.GetBytes(Notice + "ffffffff661acec2");
        WriteBin(Path.Combine(dir.Path, "am_v5_decoder.bin"), bytes);

        var license = EmbeddedSpeechLicense.ResolveFromPackage(dir.Path);

        Assert.EndsWith("for others to use.", license);
        Assert.DoesNotContain("ffffffff", license);
    }

    [Fact]
    public void ResolveFromPackage_FallsBackToNonAmModelFile()
    {
        using var dir = TempDir.Create();
        WriteBin(Path.Combine(dir.Path, "vocoder.bin"), Encoding.ASCII.GetBytes(Notice));

        var license = EmbeddedSpeechLicense.ResolveFromPackage(dir.Path);

        Assert.Equal(Notice, license);
    }

    [Fact]
    public void ResolveFromPackage_ThrowsWhenNoNoticePresent()
    {
        using var dir = TempDir.Create();
        WriteBin(Path.Combine(dir.Path, "model.bin"), new byte[] { 1, 2, 3, 4, 5 });

        Assert.Throws<NaturalVoiceUnavailableException>(
            () => EmbeddedSpeechLicense.ResolveFromPackage(dir.Path));
    }

    [Fact]
    public void ResolveFromPackage_ThrowsWhenEndMarkerMissing()
    {
        using var dir = TempDir.Create();
        // Start marker present but truncated (no end marker) must be refused.
        var truncated = "This model and the software may not be used except under agreement 2774316.";
        WriteBin(Path.Combine(dir.Path, "am_v5_decoder.bin"), Encoding.ASCII.GetBytes(truncated));

        Assert.Throws<NaturalVoiceUnavailableException>(
            () => EmbeddedSpeechLicense.ResolveFromPackage(dir.Path));
    }

    [Fact]
    public void ResolveFromPackage_ThrowsWhenDirectoryMissing()
    {
        using var dir = TempDir.Create();
        Assert.Throws<NaturalVoiceUnavailableException>(
            () => EmbeddedSpeechLicense.ResolveFromPackage(Path.Combine(dir.Path, "nope")));
    }
}
