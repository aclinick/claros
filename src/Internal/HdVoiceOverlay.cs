namespace Claros.Internal;

/// <summary>
/// Materializes a "force HD" overlay of a Windows Natural Voice package.
///
/// Every HD voice package ships two acoustic tiers and a legacy <c>*.INI</c>
/// that gates them: short utterances (below <c>HDVoiceThreshold</c>, ten words
/// by default) render through a small, low-fidelity device vocoder, while
/// longer ones use the multi-hundred-megabyte HD model. On typical short
/// phrases the low-fidelity path produces an audibly worse, "caricature"
/// rendition. Setting <c>HDVoiceThreshold</c> to zero forces the HD model for
/// every utterance and closes almost all of the gap to Microsoft's cloud
/// voices.
///
/// The package lives under a read-only <c>WindowsApps</c> directory, so the
/// threshold cannot be flipped in place. This overlay creates a writable
/// directory that symbolically links every large model file back to the
/// original package (copying nothing) and writes only a patched copy of each
/// gating INI. When symbolic links cannot be created (for example without
/// Developer Mode) it falls back to copying the file so callers still get a
/// working, forced-HD package.
/// </summary>
internal static class HdVoiceOverlay
{
    internal const string PipelineSection = "Pipeline";
    internal const string ThresholdKey = "HDVoiceThreshold";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> OverlayLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Build (or rebuild) a forced-HD overlay of <paramref name="sourcePackageDir"/>
    /// at <paramref name="overlayDir"/> and return <paramref name="overlayDir"/>.
    /// INI files that gate the HD model are copied with
    /// <see cref="ThresholdKey"/> set to <paramref name="hdThreshold"/>; every
    /// other file is symlinked (or copied when <paramref name="preferSymlink"/>
    /// is false or symlink creation is denied).
    /// </summary>
    public static string Create(
        string sourcePackageDir,
        string overlayDir,
        int hdThreshold = 0,
        bool preferSymlink = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePackageDir);
        ArgumentException.ThrowIfNullOrEmpty(overlayDir);
        if (!Directory.Exists(sourcePackageDir))
        {
            throw new DirectoryNotFoundException($"Voice package directory not found: {sourcePackageDir}");
        }

        var sourceFull = Path.GetFullPath(sourcePackageDir);
        var overlayFull = Path.GetFullPath(overlayDir);
        if (PathsOverlap(sourceFull, overlayFull))
        {
            throw new ArgumentException(
                $"Overlay directory '{overlayFull}' overlaps the source package '{sourceFull}'; " +
                "rebuilding it would delete the source package.", nameof(overlayDir));
        }

        // Serialize rebuilds of the same overlay so concurrent loads don't delete
        // files another load is still creating.
        lock (OverlayLocks.GetOrAdd(overlayFull, static _ => new object()))
        {
            if (Directory.Exists(overlayFull))
            {
                // Symlinks are deleted without touching their targets.
                Directory.Delete(overlayFull, recursive: true);
            }
            Directory.CreateDirectory(overlayFull);

            foreach (var file in Directory.EnumerateFiles(sourceFull, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceFull, file);
                var dest = Path.Combine(overlayFull, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                if (IsHdGatingIni(file))
                {
                    var patched = IniEditor.SetValue(
                        File.ReadAllText(file), PipelineSection, ThresholdKey,
                        hdThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    File.WriteAllText(dest, patched);
                    continue;
                }

                LinkOrCopy(file, dest, preferSymlink);
            }
        }

        return overlayFull;
    }

    /// <summary>
    /// True when two full paths are equal or one contains the other, so that a
    /// recursive delete of one would destroy the other.
    /// </summary>
    private static bool PathsOverlap(string a, string b)
    {
        static string Norm(string p) =>
            p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var na = Norm(a);
        var nb = Norm(b);
        return na.StartsWith(nb, StringComparison.OrdinalIgnoreCase) ||
               nb.StartsWith(na, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="filePath"/> is an <c>*.INI</c> that carries the
    /// <see cref="ThresholdKey"/> gate. Locale-agnostic: the gating INI is named
    /// after the voice's LCID (for example <c>1033.INI</c> for en-US), so the
    /// key, not the file name, is used to identify it.
    /// </summary>
    internal static bool IsHdGatingIni(string filePath)
    {
        if (!filePath.EndsWith(".INI", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return File.ReadAllText(filePath)
                .Contains(ThresholdKey, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void LinkOrCopy(string source, string dest, bool preferSymlink)
    {
        if (preferSymlink)
        {
            try
            {
                File.CreateSymbolicLink(dest, source);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Developer Mode / SeCreateSymbolicLinkPrivilege unavailable; copy instead.
            }
        }

        File.Copy(source, dest, overwrite: true);
    }
}
