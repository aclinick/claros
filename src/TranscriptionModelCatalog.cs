using Windows.Management.Deployment;
using Windows.Speech.Internal;

namespace Windows.Speech;

/// <summary>
/// Enumerates the on-device speech-recognition models installed on this machine:
/// the <c>MicrosoftWindows.Speech.&lt;locale&gt;</c> packs that Windows downloads
/// for Live Captions and Voice Typing (Settings &gt; Accessibility &gt; Captions,
/// or Settings &gt; Time &amp; language &gt; Speech). Each pack is a streaming
/// conformer-transducer model plus its punctuation, capitalization, and inverse
/// text-normalization pipeline.
///
/// This is the recognition counterpart to <see cref="VoiceCatalog"/>. Results
/// are never cached; each call queries the OS package graph.
/// </summary>
public static class TranscriptionModelCatalog
{
    private const string PackageNamePrefix = "MicrosoftWindows.Speech.";
    // Translation packs share the prefix but are not recognition models.
    private const string TranslationMarker = "Translation";
    private const string RequiredModelFile = "encoder.onnx";

    /// <summary>
    /// Return every installed recognition model, one per locale. When a locale
    /// ships more than one variant (for example a CPU pack and an NPU/<c>qcom</c>
    /// pack), the CPU variant is preferred because it runs on the stock ONNX
    /// Runtime CPU provider without extra hardware providers.
    /// </summary>
    public static IReadOnlyList<TranscriptionModelInfo> ListModels()
    {
        var byLocale = new Dictionary<string, (TranscriptionModelInfo Model, bool IsCpu)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (info, isCpu) in EnumerateInstalledModels())
        {
            if (!byLocale.TryGetValue(info.Locale, out var existing) ||
                (isCpu && !existing.IsCpu))
            {
                byLocale[info.Locale] = (info, isCpu);
            }
        }

        return byLocale.Values
            .Select(v => v.Model)
            .OrderBy(m => m.Locale, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Find the installed recognition model for <paramref name="locale"/>
    /// (matched case-insensitively, e.g. <c>en-US</c>), or <c>null</c> when none
    /// is installed.
    /// </summary>
    public static TranscriptionModelInfo? FindModel(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        return ListModels().FirstOrDefault(
            m => string.Equals(m.Locale, locale, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<(TranscriptionModelInfo Info, bool IsCpu)> EnumerateInstalledModels()
    {
        PackageManager manager;
        try { manager = new PackageManager(); }
        catch { yield break; }

        IEnumerable<Windows.ApplicationModel.Package> packages;
        try { packages = manager.FindPackagesForUser(string.Empty); }
        catch { yield break; }

        foreach (var pkg in packages)
        {
            var name = pkg.Id.Name;
            if (!name.StartsWith(PackageNamePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains(TranslationMarker, StringComparison.OrdinalIgnoreCase)) continue;

            string installedPath;
            try { installedPath = pkg.InstalledPath; }
            catch { continue; }

            if (string.IsNullOrEmpty(installedPath) ||
                !File.Exists(Path.Combine(installedPath, RequiredModelFile)))
            {
                continue;
            }

            var config = RecognitionModelConfig.FromPackage(installedPath);
            if (config is null) continue;

            var info = new TranscriptionModelInfo(
                Locale: config.Locale,
                ModelName: config.Name,
                PackageFamilyName: pkg.Id.FamilyName,
                PackageFullName: pkg.Id.FullName,
                InstalledPath: installedPath);

            var isCpu = !name.Contains(".qcom.", StringComparison.OrdinalIgnoreCase);
            yield return (info, isCpu);
        }
    }
}
