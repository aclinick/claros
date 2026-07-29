namespace Windows.Speech.Internal;

/// <summary>
/// Shared validation for the loaders that read a voice's model files off disk.
/// </summary>
/// <remarks>
/// Every on-device engine builds paths from <see cref="VoiceInfo.InstalledPath"/>.
/// A voice that is not on-device carries no package identity, so combining its
/// empty path with a model file name would silently probe the process working
/// directory instead of a voice package — which either fails obscurely or, worse,
/// loads an unrelated file that happens to share the name. These loaders therefore
/// reject a non-device voice up front, keeping the tier boundary explicit.
/// </remarks>
internal static class DeviceVoiceGuard
{
    /// <summary>
    /// Throws when <paramref name="voice"/> cannot be loaded from local package
    /// files, either because it belongs to another tier or because it carries no
    /// installed path.
    /// </summary>
    public static void RequireOnDevice(VoiceInfo voice, string paramName)
    {
        ArgumentNullException.ThrowIfNull(voice);

        if (!voice.IsOnDevice)
        {
            throw new ArgumentException(
                $"Voice '{voice.DisplayName}' is a {voice.Source} voice and has no installed " +
                "package to load. This engine synthesizes on-device voices only; construct " +
                "the synthesizer for that tier explicitly.",
                paramName);
        }

        if (string.IsNullOrEmpty(voice.InstalledPath))
        {
            throw new ArgumentException(
                $"Voice '{voice.DisplayName}' is marked on-device but carries no InstalledPath, " +
                "so its model files cannot be located.",
                paramName);
        }
    }
}
