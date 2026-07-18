using System.Runtime.InteropServices;

namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Locates and stages the native components the Azure Embedded Speech runtime
/// needs to synthesize a Windows Natural Voice fully offline.
///
/// The embedded neural TTS extension is gated: it is not published on NuGet and
/// ships only inside Windows, under the Narrator client's
/// <c>SpeechSynthesizer</c> folder. The extension in turn depends on the
/// UWP-flavored Visual C++ runtimes (<c>*_app.dll</c>) that are delivered as an
/// architecture-specific <c>Microsoft.VCLibs</c> framework package. This helper
/// discovers both locations on the current machine and copies the required
/// files next to the managed SDK so the loader can resolve them.
///
/// Discovery roots are injectable so the logic can be unit tested against fake
/// directory layouts without the real, machine-specific system files.
/// </summary>
internal static class EmbeddedSpeechRuntime
{
    /// <summary>The gated native extension DLLs sourced from the OS.</summary>
    public static readonly IReadOnlyList<string> ExtensionDllNames = new[]
    {
        "Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll",
        "Microsoft.CognitiveServices.Speech.extension.onnxruntime.dll",
        "Microsoft.CognitiveServices.Speech.extension.telemetry.dll",
        "Microsoft.CognitiveServices.Speech.extension.lu.dll",
        "Microsoft.CognitiveServices.Speech.extension.audio.sys.dll",
    };

    /// <summary>The UWP Visual C++ runtimes the extension links against.</summary>
    public static readonly IReadOnlyList<string> AppRuntimeDllNames = new[]
    {
        "msvcp140_app.dll",
        "vcruntime140_app.dll",
        "msvcp140_codecvt_ids_app.dll",
    };

    private const string ExtensionProbe = "Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll";

    private static string DefaultSystemAppsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SystemApps");

    private static string DefaultWindowsAppsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");

    /// <summary>
    /// Return the on-box directory that holds the gated Speech extension DLLs,
    /// or <c>null</c> when it cannot be found. Searches
    /// <c>{systemAppsRoot}\MicrosoftWindows.Client.Core_*\SpeechSynthesizer</c>.
    /// </summary>
    public static string? FindNativeRuntimeDirectory(string? systemAppsRoot = null)
    {
        var root = systemAppsRoot ?? DefaultSystemAppsRoot;
        if (!Directory.Exists(root)) return null;

        foreach (var client in Directory.EnumerateDirectories(root, "MicrosoftWindows.Client.Core_*"))
        {
            var candidate = Path.Combine(client, "SpeechSynthesizer");
            if (File.Exists(Path.Combine(candidate, ExtensionProbe)))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>The publisher family of the UWP Visual C++ runtime framework.</summary>
    private const string VcLibsFamilyName = "Microsoft.VCLibs.140.00_8wekyb3d8bbwe";

    /// <summary>
    /// Return the architecture-appropriate <c>Microsoft.VCLibs</c> directory that
    /// holds the UWP VC++ runtimes, or <c>null</c> when it cannot be found.
    /// Defaults <paramref name="architecture"/> to the current process
    /// architecture, since a mismatched runtime fails to load.
    ///
    /// The VCLibs framework lives under <c>WindowsApps</c>, whose contents a
    /// standard (non-elevated) user is not permitted to enumerate. This method
    /// therefore scans the directory when it can, and otherwise resolves the
    /// install path through the package graph, which needs no directory listing.
    /// </summary>
    public static string? FindAppRuntimeDirectory(
        string? windowsAppsRoot = null,
        Architecture? architecture = null)
    {
        var arch = architecture ?? RuntimeInformation.ProcessArchitecture;
        var archMoniker = ArchitectureMoniker(arch);

        var fromDisk = ScanForVcLibs(windowsAppsRoot ?? DefaultWindowsAppsRoot, archMoniker);
        if (fromDisk is not null) return fromDisk;

        // Only fall back to the package graph for the real system location; an
        // injected test root should stay purely filesystem based.
        return windowsAppsRoot is null ? FindVcLibsViaPackageGraph(arch) : null;
    }

    private static string? ScanForVcLibs(string root, string archMoniker)
    {
        if (!Directory.Exists(root)) return null;
        var pattern = $"Microsoft.VCLibs.140.00_*_{archMoniker}__*";
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, pattern))
            {
                if (File.Exists(Path.Combine(dir, AppRuntimeDllNames[0])))
                {
                    return dir;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // WindowsApps is not listable for standard users; caller falls back.
        }
        return null;
    }

    private static string? FindVcLibsViaPackageGraph(Architecture architecture)
    {
        var wanted = ToProcessorArchitecture(architecture);
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            string? best = null;
            Version? bestVersion = null;
            foreach (var package in manager.FindPackagesForUser(string.Empty, VcLibsFamilyName))
            {
                if (package.Id.Architecture != wanted) continue;

                string path;
                try { path = package.InstalledPath; }
                catch { continue; }

                if (string.IsNullOrEmpty(path) ||
                    !File.Exists(Path.Combine(path, AppRuntimeDllNames[0])))
                {
                    continue;
                }

                var id = package.Id.Version;
                var version = new Version(id.Major, id.Minor, id.Build, id.Revision);
                if (bestVersion is null || version > bestVersion)
                {
                    best = path;
                    bestVersion = version;
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is not NaturalVoiceException)
        {
            // The package graph is unavailable (e.g. no WinRT); treat as not found.
            return null;
        }
    }

    private static Windows.System.ProcessorArchitecture ToProcessorArchitecture(Architecture architecture) =>
        architecture switch
        {
            Architecture.X64 => Windows.System.ProcessorArchitecture.X64,
            Architecture.X86 => Windows.System.ProcessorArchitecture.X86,
            Architecture.Arm64 => Windows.System.ProcessorArchitecture.Arm64,
            _ => Windows.System.ProcessorArchitecture.Unknown,
        };

    /// <summary>
    /// Copy every gated extension DLL and UWP VC++ runtime into
    /// <paramref name="targetDir"/> and return the staged file paths. Throws
    /// <see cref="NaturalVoiceUnavailableException"/> when a required source
    /// directory is missing on this machine.
    /// </summary>
    public static IReadOnlyList<string> Stage(
        string targetDir,
        string? systemAppsRoot = null,
        string? windowsAppsRoot = null,
        Architecture? architecture = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDir);
        Directory.CreateDirectory(targetDir);

        var nativeDir = FindNativeRuntimeDirectory(systemAppsRoot)
            ?? throw new NaturalVoiceUnavailableException(
                "The gated Embedded Speech extension was not found on this machine. " +
                "Install a Windows Natural voice through Narrator so the SpeechSynthesizer runtime is present.");

        var runtimeDir = FindAppRuntimeDirectory(windowsAppsRoot, architecture)
            ?? throw new NaturalVoiceUnavailableException(
                "The architecture-matched Microsoft.VCLibs UWP runtimes were not found on this machine.");

        var staged = new List<string>();
        foreach (var name in ExtensionDllNames)
        {
            staged.Add(CopyRequired(nativeDir, targetDir, name));
        }
        foreach (var name in AppRuntimeDllNames)
        {
            staged.Add(CopyRequired(runtimeDir, targetDir, name));
        }
        return staged;
    }

    private static string CopyRequired(string sourceDir, string targetDir, string name)
    {
        var source = Path.Combine(sourceDir, name);
        if (!File.Exists(source))
        {
            throw new NaturalVoiceUnavailableException(
                $"Required native component '{name}' was not found under '{sourceDir}'.");
        }
        var dest = Path.Combine(targetDir, name);
        // Once the runtime has loaded a staged DLL, Windows locks the image file,
        // so a subsequent Load's File.Copy(overwrite) would throw a sharing
        // violation. Skip the copy when an identical file is already staged.
        if (!IsSameContent(source, dest))
        {
            File.Copy(source, dest, overwrite: true);
        }
        return dest;
    }

    private static bool IsSameContent(string source, string dest)
    {
        var destInfo = new FileInfo(dest);
        if (!destInfo.Exists) return false;
        var sourceInfo = new FileInfo(source);
        return sourceInfo.Length == destInfo.Length &&
               sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc;
    }

    private static string ArchitectureMoniker(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException(
            $"Embedded Speech has no UWP runtime moniker for architecture '{architecture}'."),
    };
}
