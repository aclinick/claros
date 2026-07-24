namespace Windows.Speech;

/// <summary>
/// An installed on-device speech-recognition model: one of the
/// <c>MicrosoftWindows.Speech.&lt;locale&gt;</c> packs that power Windows Live
/// Captions and Voice Typing. Enumerate these with
/// <see cref="TranscriptionModelCatalog"/> and load one with
/// <see cref="EmbeddedTranscriber.Load"/>.
/// </summary>
/// <param name="Locale">BCP-47 locale of the model, e.g. <c>en-US</c>.</param>
/// <param name="ModelName">
/// The Embedded Speech SDK model identity, e.g.
/// <c>Microsoft Speech Recognizer en-US FP Model V11</c>. Passed to the runtime
/// to select the model.
/// </param>
/// <param name="PackageFamilyName">Package family name of the recognition pack.</param>
/// <param name="PackageFullName">Full package name (includes version and architecture).</param>
/// <param name="InstalledPath">Filesystem path to the installed model files.</param>
public sealed record TranscriptionModelInfo(
    string Locale,
    string ModelName,
    string PackageFamilyName,
    string PackageFullName,
    string InstalledPath);
