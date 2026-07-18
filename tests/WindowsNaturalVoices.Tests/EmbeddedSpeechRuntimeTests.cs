using System.Runtime.InteropServices;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class EmbeddedSpeechRuntimeTests
{
    private const string Probe = "Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll";

    private static void MakeNativeLayout(TempDir dir)
    {
        // {systemApps}\MicrosoftWindows.Client.Core_<hash>\SpeechSynthesizer\<extensions>
        var speech = dir.Sub(@"systemapps\MicrosoftWindows.Client.Core_abc123\SpeechSynthesizer");
        foreach (var name in EmbeddedSpeechRuntime.ExtensionDllNames)
        {
            File.WriteAllText(Path.Combine(speech, name), name);
        }
    }

    private static void MakeRuntimeLayout(TempDir dir, string archMoniker)
    {
        var vc = dir.Sub($@"windowsapps\Microsoft.VCLibs.140.00_14.0.1_{archMoniker}__8wekyb3d8bbwe");
        foreach (var name in EmbeddedSpeechRuntime.AppRuntimeDllNames)
        {
            File.WriteAllText(Path.Combine(vc, name), name);
        }
    }

    private static string ArchMoniker() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    [Fact]
    public void FindNativeRuntimeDirectory_LocatesSpeechSynthesizerFolder()
    {
        using var dir = TempDir.Create();
        MakeNativeLayout(dir);

        var found = EmbeddedSpeechRuntime.FindNativeRuntimeDirectory(Path.Combine(dir.Path, "systemapps"));

        Assert.NotNull(found);
        Assert.True(File.Exists(Path.Combine(found!, Probe)));
    }

    [Fact]
    public void FindNativeRuntimeDirectory_ReturnsNullWhenAbsent()
    {
        using var dir = TempDir.Create();
        Assert.Null(EmbeddedSpeechRuntime.FindNativeRuntimeDirectory(Path.Combine(dir.Path, "nope")));
    }

    [Fact]
    public void FindAppRuntimeDirectory_LocatesArchMatchedVcLibs()
    {
        using var dir = TempDir.Create();
        var arch = RuntimeInformation.ProcessArchitecture;
        MakeRuntimeLayout(dir, ArchMoniker());

        var found = EmbeddedSpeechRuntime.FindAppRuntimeDirectory(
            Path.Combine(dir.Path, "windowsapps"), arch);

        Assert.NotNull(found);
        Assert.True(File.Exists(Path.Combine(found!, "msvcp140_app.dll")));
    }

    [Fact]
    public void FindAppRuntimeDirectory_IgnoresMismatchedArchitecture()
    {
        using var dir = TempDir.Create();
        MakeRuntimeLayout(dir, "somethingelse");

        var found = EmbeddedSpeechRuntime.FindAppRuntimeDirectory(
            Path.Combine(dir.Path, "windowsapps"), Architecture.Arm64);

        Assert.Null(found);
    }

    [Fact]
    public void Stage_CopiesAllExtensionsAndRuntimes()
    {
        using var dir = TempDir.Create();
        MakeNativeLayout(dir);
        MakeRuntimeLayout(dir, ArchMoniker());
        var target = dir.Sub("app");

        var staged = EmbeddedSpeechRuntime.Stage(
            target,
            systemAppsRoot: Path.Combine(dir.Path, "systemapps"),
            windowsAppsRoot: Path.Combine(dir.Path, "windowsapps"));

        Assert.Equal(
            EmbeddedSpeechRuntime.ExtensionDllNames.Count + EmbeddedSpeechRuntime.AppRuntimeDllNames.Count,
            staged.Count);
        foreach (var name in EmbeddedSpeechRuntime.ExtensionDllNames)
        {
            Assert.True(File.Exists(Path.Combine(target, name)));
        }
        foreach (var name in EmbeddedSpeechRuntime.AppRuntimeDllNames)
        {
            Assert.True(File.Exists(Path.Combine(target, name)));
        }
    }

    [Fact]
    public void Stage_ThrowsWhenNativeRuntimeMissing()
    {
        using var dir = TempDir.Create();
        MakeRuntimeLayout(dir, ArchMoniker());

        Assert.Throws<NaturalVoiceUnavailableException>(() => EmbeddedSpeechRuntime.Stage(
            dir.Sub("app"),
            systemAppsRoot: Path.Combine(dir.Path, "empty"),
            windowsAppsRoot: Path.Combine(dir.Path, "windowsapps")));
    }

    [Fact]
    public void Stage_ThrowsWhenAppRuntimeMissing()
    {
        using var dir = TempDir.Create();
        MakeNativeLayout(dir);

        Assert.Throws<NaturalVoiceUnavailableException>(() => EmbeddedSpeechRuntime.Stage(
            dir.Sub("app"),
            systemAppsRoot: Path.Combine(dir.Path, "systemapps"),
            windowsAppsRoot: Path.Combine(dir.Path, "empty")));
    }
}
