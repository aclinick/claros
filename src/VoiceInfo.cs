namespace Windows.Speech;

/// <summary>
/// A Windows Natural Voice discovered through the
/// <c>com.microsoft.voice.model.1</c> AppExtension contract.
/// </summary>
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
    string InstalledPath);
