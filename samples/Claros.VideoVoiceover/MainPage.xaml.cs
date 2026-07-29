using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Claros;
using Claros.SpeakSubtitles;
using Claros_VideoVoiceover.Models;
using Claros_VideoVoiceover.ViewModels;
using FileOpenPicker = Microsoft.Windows.Storage.Pickers.FileOpenPicker;

namespace Claros_VideoVoiceover;

/// <summary>
/// The single-window UI: a muted video with a live, on-device TTS voiceover in a
/// user-chosen language, kept in sync with the video's real playback position.
/// </summary>
public sealed partial class MainPage : Page
{
    // Default showcase video. All voiceover languages are discovered from
    // subtitle files sitting next to it.
    private const string DefaultVideoPath =
        @"C:\Users\andre\OneDrive - Elev8 Consulting LLC\npu\apps\zavadental\Zava Dental Final.mp4";

    private readonly MediaPlayer _player;
    private readonly VoiceoverController _controller;

    private SpeechPlatform? _platform;
    private IReadOnlyList<VoiceInfo> _voices = [];
    private string? _videoPath;
    private bool _suppressSelection;
    private string _voiceSignature = string.Empty;

    public MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        _player = new MediaPlayer { IsMuted = true, AutoPlay = false };
        VideoPlayer.SetMediaPlayer(_player);

        _controller = new VoiceoverController(_player, DispatcherQueue);
        _controller.SentenceStarted += OnSentenceStarted;
        _controller.StatusChanged += text => ViewModel.StatusText = text;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _platform = new SpeechPlatform();
        _platform.VoicesChanged += OnVoicesChanged;
        await RefreshVoicesAsync();

        if (File.Exists(DefaultVideoPath))
        {
            await LoadVideoAsync(DefaultVideoPath);
        }
        else
        {
            ViewModel.StatusText = "Default video not found. Use \u201cOpen video\u2026\u201d to pick one.";
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _controller.Dispose();
        if (_platform is not null)
        {
            _platform.VoicesChanged -= OnVoicesChanged;
            _platform.Dispose();
        }
        _player.Dispose();
    }

    private void OnVoicesChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () => await RefreshVoicesAsync());

    private async Task RefreshVoicesAsync()
    {
        try
        {
            _voices = await _platform!.ListVoicesAsync();
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Could not enumerate voices: {ex.Message}";
            _voices = [];
        }

        // The act of loading a voice stages files and makes the OS raise
        // VoicesChanged; ignore those echoes when the actual voice set is unchanged.
        var signature = string.Join("|", _voices.Select(v => v.Id).OrderBy(x => x));
        if (signature == _voiceSignature) return;
        _voiceSignature = signature;

        RebuildLanguages();
        if (_videoPath is not null)
            await PreloadVoicesAsync();
    }

    private async void OpenVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".webm");

        var result = await picker.PickSingleFileAsync();
        if (result is null) return;

        await LoadVideoAsync(result.Path);
    }

    private async Task LoadVideoAsync(string path)
    {
        _videoPath = path;
        await _controller.ResetAsync();

        ViewModel.CurrentSentence = string.Empty;
        ViewModel.ActiveVoice = string.Empty;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);

            // Some source files (e.g. the Zava demo mp4) carry an EMBEDDED subtitle /
            // timed-text track. MediaPlayerElement auto-presents such tracks itself,
            // showing a second theme-coloured caption at the TOP of the frame that clashes with
            // our own controllable white overlay at the bottom. Wrap the source in a
            // MediaPlaybackItem and disable presentation of every timed-metadata track
            // so OUR overlay (governed by the "Show captions" toggle) is the only
            // caption. Tracks can surface asynchronously after load, so disable both
            // the ones already present AND any that appear later.
            var item = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(file));
            void DisableAllTimedTracks()
            {
                var n = item.TimedMetadataTracks.Count;
                for (uint i = 0; i < n; i++)
                {
                    var t = item.TimedMetadataTracks[(int)i];
                    item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
                    Logger.Log($"Disabled timed track [{i}] kind={t.TimedMetadataKind} id={t.Id} label={t.Label} lang={t.Language}");
                }
                Logger.Log($"DisableAllTimedTracks count={n}");
            }
            item.TimedMetadataTracksChanged += (_, _) => DisableAllTimedTracks();
            DisableAllTimedTracks(); // any already discovered

            _player.Source = item;
            _player.IsMuted = true; // the voiceover is the only audio
            ViewModel.VideoName = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Could not open video: {ex.Message}";
            return;
        }

        RebuildLanguages();
        await PreloadVoicesAsync();
    }

    /// <summary>
    /// Load and pre-warm a voice for every offered language up front, so switching
    /// language later is an instant pointer swap (no model load) even mid-playback.
    /// </summary>
    private async Task PreloadVoicesAsync()
    {
        var options = ViewModel.Languages.ToList();
        if (options.Count == 0)
        {
            ViewModel.AreVoicesReady = false;
            ViewModel.StatusText = "No installed Natural voice matches this video's subtitle languages.";
            return;
        }

        ViewModel.AreVoicesReady = false;
        try
        {
            // Progress runs on the UI thread (PreloadAsync resumes here between awaits).
            await _controller.PreloadAsync(options, msg => ViewModel.StatusText = msg);
        }
        catch (Exception ex)
        {
            Logger.Log("PreloadVoicesAsync failed", ex);
            ViewModel.StatusText = $"Couldn't prepare voices: {ex.Message}";
            return;
        }

        // Only the languages that actually parsed + loaded a voice are usable.
        // Prune the ComboBox to those so the user can't pick one that would be silent.
        var ready = new HashSet<string>(_controller.ReadyLanguages, StringComparer.OrdinalIgnoreCase);
        var requested = options.Count;

        _suppressSelection = true;
        for (var i = ViewModel.Languages.Count - 1; i >= 0; i--)
        {
            if (!ready.Contains(ViewModel.Languages[i].Lang))
                ViewModel.Languages.RemoveAt(i);
        }
        // Drop a now-invalid selection so the ComboBox doesn't point at a removed item.
        if (ViewModel.SelectedLanguage is { } sel && !ready.Contains(sel.Lang))
            ViewModel.SelectedLanguage = null;
        ViewModel.HasLanguages = ViewModel.Languages.Count > 0;
        _suppressSelection = false;

        var loaded = ViewModel.Languages.Count;
        ViewModel.AreVoicesReady = loaded > 0;

        if (loaded == 0)
        {
            ViewModel.StatusText = "No voices could be prepared for this video's subtitle languages.";
        }
        else if (loaded < requested)
        {
            ViewModel.StatusText =
                $"Ready. {loaded} of {requested} voice(s) loaded (some failed and were hidden). Pick a language, press Play, and switch anytime while it plays.";
        }
        else
        {
            ViewModel.StatusText =
                $"Ready. {loaded} voice(s) loaded. Pick a language, press Play, and switch anytime while it plays.";
        }
        Logger.Log($"PreloadVoicesAsync requested={requested} loaded={loaded} ready=[{string.Join(",", ready)}]");
    }

    private void RebuildLanguages()
    {
        var previousLang = ViewModel.SelectedLanguage?.Lang;

        _suppressSelection = true;
        ViewModel.Languages.Clear();

        if (_videoPath is not null && _voices.Count > 0)
        {
            var discovered = DiscoverLanguages(_videoPath, _voices)
                .OrderByDescending(o => o.Lang.Equals("en", StringComparison.OrdinalIgnoreCase))
                .ThenBy(o => o.Label, StringComparer.CurrentCulture);
            foreach (var option in discovered)
                ViewModel.Languages.Add(option);
        }

        ViewModel.HasLanguages = ViewModel.Languages.Count > 0;
        Logger.Log($"RebuildLanguages count={ViewModel.Languages.Count} langs=[{string.Join(",", ViewModel.Languages.Select(l => l.Lang))}] prev={previousLang ?? "null"}");

        // Preserve the current language selection across a voice-catalog refresh.
        ViewModel.SelectedLanguage = previousLang is null
            ? null
            : ViewModel.Languages.FirstOrDefault(
                o => o.Lang.Equals(previousLang, StringComparison.OrdinalIgnoreCase));

        _suppressSelection = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Logger.Log($"SelectionChanged suppress={_suppressSelection} selected={ViewModel.SelectedLanguage?.Lang ?? "null"}");
        if (_suppressSelection) return;
        if (ViewModel.SelectedLanguage is not { } option) return;

        ViewModel.CurrentSentence = string.Empty;

        // Instant swap: the voice is already loaded + pre-warmed. The next line
        // renders in the new voice in ~100 ms, so switching feels immediate even
        // while the video is playing.
        if (_controller.SetActiveLanguage(option.Lang))
            ViewModel.ActiveVoice = option.Label;
    }

    private void OnSentenceStarted(string text)
    {
        ViewModel.CurrentSentence = text;
    }

    /// <summary>
    /// Find subtitle files sitting next to the video that follow the
    /// <c>&lt;basename&gt;.&lt;lang&gt;.srt</c> convention (plus the base
    /// <c>&lt;basename&gt;.srt</c>, treated as English) and keep only those whose
    /// locale has a matching installed Natural voice.
    /// </summary>
    private static IEnumerable<LanguageOption> DiscoverLanguages(
        string videoPath, IReadOnlyList<VoiceInfo> voices)
    {
        var dir = Path.GetDirectoryName(videoPath);
        if (dir is null || !Directory.Exists(dir)) yield break;

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var srt in Directory.EnumerateFiles(dir, "*.srt"))
        {
            var core = Path.GetFileName(srt)[..^4]; // drop ".srt"
            if (!core.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) continue;

            var suffix = core[baseName.Length..]; // "" for base, ".fr" for a lang sibling
            string lang;
            if (suffix.Length == 0)
            {
                lang = "en"; // base subtitle => English
            }
            else
            {
                var inferred = LocaleInference.FromFileName(srt);
                // Only accept an exact ".<lang>" suffix (skips e.g. " (2)").
                if (inferred is null ||
                    !suffix.Equals("." + inferred, StringComparison.OrdinalIgnoreCase))
                    continue;
                lang = inferred;
            }

            if (!seen.Add(lang)) continue;

            var voice = VoiceSelection.Pick(voices, nameSubstring: null, lang, out _);
            if (voice is null) continue;

            yield return new LanguageOption(lang, srt, voice);
        }
    }

    /// <summary>Show an overlay only when it has text.</summary>
    public static Visibility NonEmptyToVisible(string value) =>
        string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Show the sentence caption only when captions are enabled AND there is text.
    /// Voiceover playback is independent of this; hiding captions never stops the
    /// TTS scheduler.
    /// </summary>
    public static Visibility CaptionVisible(bool showCaptions, string text) =>
        showCaptions && !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
}
