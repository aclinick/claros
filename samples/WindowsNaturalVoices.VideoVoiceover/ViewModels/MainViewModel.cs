using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsNaturalVoices_VideoVoiceover.Models;

namespace WindowsNaturalVoices_VideoVoiceover.ViewModels;

/// <summary>
/// Bindable state for the main window: the discovered languages, the current
/// selection, and the on-screen narration captions.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Languages offered for voiceover (subtitles that have a matching installed voice).</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string VideoName { get; set; } = "No video loaded";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pick a video and a language to begin.";

    [ObservableProperty]
    public partial string CurrentSentence { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLanguages { get; set; }

    /// <summary>True once every offered language's voice is pre-loaded and pre-warmed.</summary>
    [ObservableProperty]
    public partial bool AreVoicesReady { get; set; }

    /// <summary>Label of the voice currently narrating, e.g. "French (France) · Microsoft Remy".</summary>
    [ObservableProperty]
    public partial string ActiveVoice { get; set; } = string.Empty;

    /// <summary>
    /// Whether the on-screen sentence caption is shown. Default on. This only hides
    /// the caption band/text; the voiceover keeps playing either way (caption
    /// visibility is never wired into the TTS scheduler).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowCaptions { get; set; } = true;
}
