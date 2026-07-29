namespace Windows.Speech;

/// <summary>
/// A voice a caller can synthesize with. On-device voices are discovered through
/// the <c>com.microsoft.voice.model.1</c> AppExtension contract and carry the
/// package identity and installed path the local engines read their models from.
/// </summary>
/// <remarks>
/// The package-identity fields (<see cref="PackageFamilyName"/>,
/// <see cref="PackageFullName"/>, <see cref="InstalledPath"/>) describe an
/// installed Windows voice package and are therefore empty for a voice whose
/// <see cref="Source"/> is <see cref="VoiceSource.Cloud"/>. Check
/// <see cref="IsOnDevice"/> before treating <see cref="InstalledPath"/> as a real
/// directory.
/// </remarks>
/// <param name="Id">Stable identifier for the voice.</param>
/// <param name="DisplayName">Human-readable voice name, e.g. <c>Microsoft Ava</c>.</param>
/// <param name="Locale">BCP-47 locale tag, e.g. <c>en-US</c>.</param>
/// <param name="Gender">Voice gender as reported by the voice metadata.</param>
/// <param name="Age">Voice age as reported by the voice metadata.</param>
/// <param name="Vendor">Voice vendor.</param>
/// <param name="Version">Voice version.</param>
/// <param name="PackageFamilyName">Package family name; empty for a cloud voice.</param>
/// <param name="PackageFullName">Full package name; empty for a cloud voice.</param>
/// <param name="InstalledPath">Filesystem path to the installed voice package; empty for a cloud voice.</param>
public sealed record VoiceInfo(
    string Id,
    string DisplayName,
    string Locale,
    string Gender,
    string Age,
    string Vendor,
    string Version,
    string PackageFamilyName,
    string PackageFullName,
    string InstalledPath)
{
    /// <summary>
    /// Which tier produces this voice's audio. Defaults to
    /// <see cref="VoiceSource.Device"/>, so a voice is only ever treated as hosted
    /// when it was deliberately created that way — there is no path that promotes
    /// a discovered voice to a remote service.
    /// </summary>
    public VoiceSource Source { get; init; } = VoiceSource.Device;

    /// <summary>
    /// <c>true</c> when this voice is synthesized locally from an installed
    /// package, so <see cref="InstalledPath"/> refers to a real directory and the
    /// on-device engines can load it.
    /// </summary>
    public bool IsOnDevice => Source == VoiceSource.Device;

    /// <summary>
    /// Describes a voice served by a hosted engine the caller has explicitly
    /// opted into. The package-identity fields are left empty, and
    /// <see cref="IsOnDevice"/> is <c>false</c>, so the on-device engines reject
    /// it rather than probing a path that does not exist.
    /// </summary>
    public static VoiceInfo Cloud(
        string id,
        string displayName,
        string locale,
        string gender = "Unspecified",
        string age = "Unspecified",
        string vendor = "Microsoft",
        string version = "1.0")
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentException.ThrowIfNullOrEmpty(locale);

        return new VoiceInfo(
            Id: id,
            DisplayName: displayName,
            Locale: locale,
            Gender: gender,
            Age: age,
            Vendor: vendor,
            Version: version,
            PackageFamilyName: "",
            PackageFullName: "",
            InstalledPath: "")
        {
            Source = VoiceSource.Cloud,
        };
    }
}
