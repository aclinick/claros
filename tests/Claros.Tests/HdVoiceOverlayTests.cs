using Claros.Internal;

namespace Claros.Tests;

public class HdVoiceOverlayTests
{
    private const string GatingIni =
        "[Output]\nEnableBuffer=false\n[Pipeline]\nEnableHDVoice=yes\nHDVoiceThreshold=10\n";

    private static string MakeSourcePackage(TempDir dir)
    {
        var src = dir.Sub("package");
        File.WriteAllText(Path.Combine(src, "1033.INI"), GatingIni);
        File.WriteAllText(Path.Combine(src, "MSTTSLocEnUS.ini"), "[Data]\nName=Andrew\n");
        File.WriteAllText(Path.Combine(src, "hd_am_v5_encoder.bin"), "MODELDATA");
        return src;
    }

    [Fact]
    public void Create_ForcesHdThresholdInGatingIni()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, hdThreshold: 0, preferSymlink: false);

        var patched = File.ReadAllText(Path.Combine(overlay, "1033.INI"));
        Assert.Contains("HDVoiceThreshold=0", patched);
        Assert.DoesNotContain("HDVoiceThreshold=10", patched);
        Assert.Contains("EnableHDVoice=yes", patched);
    }

    [Fact]
    public void Create_LeavesNonGatingIniUntouched()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, preferSymlink: false);

        Assert.Equal(
            File.ReadAllText(Path.Combine(src, "MSTTSLocEnUS.ini")),
            File.ReadAllText(Path.Combine(overlay, "MSTTSLocEnUS.ini")));
    }

    [Fact]
    public void Create_MaterializesEveryModelFile()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, preferSymlink: false);

        var bin = Path.Combine(overlay, "hd_am_v5_encoder.bin");
        Assert.True(File.Exists(bin));
        Assert.Equal("MODELDATA", File.ReadAllText(bin));
    }

    [Fact]
    public void Create_HonoursCustomThreshold()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, hdThreshold: 3, preferSymlink: false);

        Assert.Contains("HDVoiceThreshold=3", File.ReadAllText(Path.Combine(overlay, "1033.INI")));
    }

    [Fact]
    public void Create_IsIdempotentAcrossRebuilds()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, preferSymlink: false);
        HdVoiceOverlay.Create(src, overlay, preferSymlink: false);

        Assert.Contains("HDVoiceThreshold=0", File.ReadAllText(Path.Combine(overlay, "1033.INI")));
        Assert.True(File.Exists(Path.Combine(overlay, "hd_am_v5_encoder.bin")));
    }

    [Fact]
    public void Create_WithPreferSymlink_ProducesReadableFilesRegardlessOfPrivilege()
    {
        // On hosts without symbolic-link privilege the overlay falls back to
        // copies, so the file must be present and readable either way.
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var overlay = Path.Combine(dir.Path, "overlay");

        HdVoiceOverlay.Create(src, overlay, preferSymlink: true);

        Assert.Equal("MODELDATA", File.ReadAllText(Path.Combine(overlay, "hd_am_v5_encoder.bin")));
    }

    [Fact]
    public void Create_ThrowsWhenSourceMissing()
    {
        using var dir = TempDir.Create();
        Assert.Throws<DirectoryNotFoundException>(() =>
            HdVoiceOverlay.Create(Path.Combine(dir.Path, "nope"), Path.Combine(dir.Path, "overlay")));
    }

    [Fact]
    public void Create_ThrowsWhenOverlayEqualsSource()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);

        Assert.Throws<ArgumentException>(() => HdVoiceOverlay.Create(src, src, preferSymlink: false));
    }

    [Fact]
    public void Create_ThrowsWhenOverlayIsInsideSource()
    {
        using var dir = TempDir.Create();
        var src = MakeSourcePackage(dir);
        var nested = Path.Combine(src, "overlay");

        Assert.Throws<ArgumentException>(() => HdVoiceOverlay.Create(src, nested, preferSymlink: false));
        // The source package must not have been deleted by the rejected rebuild.
        Assert.True(File.Exists(Path.Combine(src, "hd_am_v5_encoder.bin")));
    }

    [Fact]
    public void IsHdGatingIni_TrueOnlyForIniWithThresholdKey()
    {
        using var dir = TempDir.Create();
        var gating = dir.WriteFile("1033.INI", GatingIni);
        var plain = dir.WriteFile("MSTTSLocEnUS.ini", "[Data]\nName=Andrew\n");
        var notIni = dir.WriteFile("hd_phones.txt", "HDVoiceThreshold=10");

        Assert.True(HdVoiceOverlay.IsHdGatingIni(gating));
        Assert.False(HdVoiceOverlay.IsHdGatingIni(plain));
        Assert.False(HdVoiceOverlay.IsHdGatingIni(notIni));
    }
}
