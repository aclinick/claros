using System.Text;

namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Resolves the on-device model license string that the Embedded Speech runtime
/// requires from the voice package itself.
///
/// Every Natural Voice model file carries the license as a plaintext notice at
/// the head of the binary (beginning "This model and the software may not be
/// used..." and quoting reference number 2774316). Reading it from the installed
/// package at runtime — rather than hard-coding or redistributing it — keeps the
/// notice attached to the model it licenses and lets any installed voice work
/// without extra configuration.
/// </summary>
internal static class EmbeddedSpeechLicense
{
    private const string StartMarker = "This model and the software";
    private const string EndMarker = "for others to use.";
    private const int PrefixBytesToScan = 16 * 1024;

    /// <summary>
    /// Read the license notice out of a model file under
    /// <paramref name="packageDirectory"/>. Throws
    /// <see cref="NaturalVoiceUnavailableException"/> when no notice is found.
    /// </summary>
    public static string ResolveFromPackage(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageDirectory);
        if (!Directory.Exists(packageDirectory))
        {
            throw new NaturalVoiceUnavailableException(
                $"Voice package directory not found: {packageDirectory}");
        }

        foreach (var bin in EnumerateModelFiles(packageDirectory))
        {
            if (TryReadLicense(bin, out var license))
            {
                return license;
            }
        }

        throw new NaturalVoiceUnavailableException(
            $"No on-device model license notice was found in the voice package at '{packageDirectory}'. " +
            "Pass the license explicitly if the package layout is non-standard.");
    }

    // Prefer the acoustic-model decoder (where the notice reliably sits at the
    // head), then fall back to any other model binary.
    private static IEnumerable<string> EnumerateModelFiles(string packageDirectory)
    {
        var all = Directory.EnumerateFiles(packageDirectory, "*.bin", SearchOption.AllDirectories).ToList();
        return all
            .OrderByDescending(f => Path.GetFileName(f).StartsWith("am_", StringComparison.OrdinalIgnoreCase))
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadLicense(string path, out string license)
    {
        license = string.Empty;
        byte[] buffer;
        int read;
        try
        {
            using var stream = File.OpenRead(path);
            buffer = new byte[PrefixBytesToScan];
            read = stream.Read(buffer, 0, buffer.Length);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(buffer, 0, read);
        var start = text.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0) return false;

        // Keep only the contiguous printable run so trailing binary is excluded.
        var end = start;
        while (end < text.Length && text[end] >= ' ' && text[end] < (char)127)
        {
            end++;
        }
        var run = text[start..end];

        var marker = run.IndexOf(EndMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            // Start marker without the end marker means a truncated or unexpected
            // layout; refuse rather than return an incomplete license.
            return false;
        }
        license = run[..(marker + EndMarker.Length)];
        return true;
    }
}
