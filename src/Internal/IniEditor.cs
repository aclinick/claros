using System.Text;

namespace Windows.Speech.Internal;

/// <summary>
/// Minimal, section-aware editor for the classic <c>*.INI</c> files that the
/// Windows Natural Voice packages still ship (for example <c>1033.INI</c>,
/// whose <c>[Pipeline]</c> section gates the high-fidelity HD acoustic model).
///
/// The editor rewrites a single key in place while preserving every other
/// line, comment, and blank exactly as written. When the key is absent it is
/// appended to the target section, and when the section is absent the section
/// header and key are appended to the end of the file. Line endings on the
/// edited key follow the document's dominant style.
/// </summary>
internal static class IniEditor
{
    /// <summary>
    /// Return <paramref name="content"/> with <paramref name="key"/> in
    /// <paramref name="section"/> set to <paramref name="value"/>. Section and
    /// key matching is case-insensitive, mirroring the Win32 INI APIs.
    /// </summary>
    public static string SetValue(string content, string section, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split('\n');

        string? currentSection = null;
        int sectionHeaderIndex = -1;
        int lastLineInSection = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim().TrimEnd('\r').Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    // Leaving the target section; stop tracking its extent.
                    break;
                }
                currentSection = trimmed[1..^1].Trim();
                if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionHeaderIndex = i;
                }
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lastLineInSection = i;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            var existingKey = trimmed[..eq].Trim();
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                var cr = lines[i].EndsWith('\r') ? "\r" : string.Empty;
                lines[i] = $"{key}={value}{cr}";
                return string.Join("\n", lines);
            }
        }

        // Key not found. Append into the existing section, or create it.
        if (sectionHeaderIndex >= 0)
        {
            int insertAfter = lastLineInSection >= 0 ? lastLineInSection : sectionHeaderIndex;
            var list = new List<string>(lines);
            list.Insert(insertAfter + 1, $"{key}={value}");
            return string.Join("\n", list);
        }

        var sb = new StringBuilder(content);
        if (content.Length > 0 && !content.EndsWith('\n')) sb.Append(newline);
        sb.Append('[').Append(section).Append(']').Append(newline);
        sb.Append(key).Append('=').Append(value).Append(newline);
        return sb.ToString();
    }
}
