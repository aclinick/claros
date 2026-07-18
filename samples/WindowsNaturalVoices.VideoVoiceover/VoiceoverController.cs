using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.UI.Dispatching;
using Windows.Media.Playback;
using WindowsNaturalVoices;
using WindowsNaturalVoices.SpeakSubtitles;
using WindowsNaturalVoices_VideoVoiceover.Models;

namespace WindowsNaturalVoices_VideoVoiceover;

/// <summary>
/// Drives a live, just-in-time voiceover that stays in sync with a muted video.
///
/// The <see cref="MediaPlaybackSession.Position"/> of the video is the master
/// clock, so pausing and seeking the video automatically pause and re-sync the
/// narration. A ~50 ms <see cref="DispatcherQueueTimer"/> polls that clock and,
/// when the playhead reaches the next sentence (minus a small lead to cover
/// first-audio latency), fires <see cref="EmbeddedVoiceSpeaker.SpeakToDefaultOutputAsync"/>
/// for that whole sentence. Only one utterance plays at a time.
///
/// Every offered language is <b>pre-loaded and pre-warmed up front</b> (see
/// <see cref="PreloadAsync"/>): loading a second voice once the native runtime is
/// staged costs ~20 ms, and a throwaway synth per voice pays the one-time
/// ~1.3 s model warm-up so no line is ever delayed by it later. A language switch
/// is then a pure pointer swap (<see cref="SetActiveLanguage"/>) with no loading, so
/// the next line renders in the new voice in ~100 ms, instantly, even mid-playback.
///
/// <see cref="EmbeddedVoiceSpeaker"/> is thread hostile, so every native call
/// (load, pre-warm, speak, dispose) is marshalled onto the one dedicated worker
/// thread <see cref="_worker"/> and serialized.
///
/// An in-flight utterance is always allowed to finish rather than being cancelled.
/// The library cancels playback by calling <c>SpeechSynthesizer.StopSpeakingAsync</c>
/// from the thread that trips the token (i.e. the UI thread), which is a
/// cross-thread call into the thread-hostile native synth and reliably hangs then
/// crashes the runtime. So a switch/pause/seek stops scheduling *new* audio and
/// re-syncs; whatever line is already speaking finishes first and the change lands
/// on the following line.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VoiceoverController : IDisposable
{
    // Start synthesizing this far ahead of a sentence's timestamp so the first
    // audio lands roughly when the video reaches it. With every voice pre-warmed
    // steady-state synth is ~100 ms for ~4 s of audio, so a small lead is plenty.
    private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan GroupGap = TimeSpan.FromSeconds(1.2);

    private readonly MediaPlayer _player;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly SerialWorker _worker = new("VoiceoverSpeaker");

    // Serializes ResetAsync/PreloadAsync so only one reset-or-preload runs at a
    // time (a second video or a VoicesChanged echo can't interleave with an
    // in-flight preload and repopulate the tracks with the wrong subtitles).
    private readonly SemaphoreSlim _gate = new(1, 1);

    // One speaker per voice, loaded once and kept resident for the whole session
    // (each HD model is ~100+ MB; holding a handful on a dev box is fine).
    // Mutated ONLY on the worker thread (see GetOrLoadSpeaker) and disposed on the
    // worker thread, so a created speaker can never escape disposal.
    private readonly Dictionary<string, EmbeddedVoiceSpeaker> _speakersByVoiceId = [];

    // Per-language track (parsed+grouped subtitles + the voice's speaker), rebuilt
    // for the current video. Keyed by language code ("en", "fr", …).
    private readonly Dictionary<string, LangTrack> _tracks =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<CueGroup> _groups = [];
    private EmbeddedVoiceSpeaker? _speaker;
    private Task? _utteranceTask;
    private int _nextIndex;
    private int _speakingIndex = -1;
    // Start time of the sentence currently being spoken, in its OWN track's clock.
    // Used to dedup across a language switch by timestamp rather than by an index
    // that would be meaningless in a differently-grouped translation.
    private TimeSpan _speakingStart = TimeSpan.MinValue;
    private bool _speaking;
    // Bumped whenever a new reset/preload intent starts; an in-flight preload that
    // sees a newer generation abandons its work instead of committing stale tracks.
    private int _generation;
    private bool _seekPending;
    private volatile bool _disposed;

    public VoiceoverController(MediaPlayer player, DispatcherQueue dispatcher)
    {
        _player = player;
        _dispatcher = dispatcher;
        // SeekCompleted is the authoritative signal that the user jumped the
        // playhead. Inferring seeks from position deltas gives false positives
        // whenever the poll timer is briefly starved during synthesis.
        _player.PlaybackSession.SeekCompleted += OnSeekCompleted;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    private void OnSeekCompleted(MediaPlaybackSession sender, object args)
    {
        // Marshal to the UI thread; the actual re-sync happens on the next tick
        // using the settled position.
        _dispatcher.TryEnqueue(() => _seekPending = true);
    }

    /// <summary>Raised (on the UI thread) with the sentence text when it starts speaking.</summary>
    public event Action<string>? SentenceStarted;

    /// <summary>Raised (on the UI thread) for each word as it is synthesized.</summary>
    public event Action<SpokenWord>? WordSpoken;

    /// <summary>Raised (on the UI thread) with human-readable status updates.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>True once a language is active and the scheduler is live.</summary>
    public bool IsActive => _speaker is not null;

    /// <summary>Language codes that are pre-loaded and ready to narrate.</summary>
    public IReadOnlyCollection<string> ReadyLanguages => _tracks.Keys;

    /// <summary>
    /// Parse+group every offered language's subtitles and load+pre-warm its voice,
    /// all off the UI thread. Voices already resident are reused (a re-opened video
    /// never reloads a model). After this returns, <see cref="SetActiveLanguage"/>
    /// is an instant, allocation-free pointer swap.
    ///
    /// Runs under <see cref="_gate"/> so only one reset/preload happens at a time,
    /// and is generation-checked: it builds into local collections and only commits
    /// to <see cref="_tracks"/> if it hasn't been superseded by a newer video /
    /// voices change while it was working.
    /// </summary>
    public async Task PreloadAsync(IReadOnlyList<LanguageOption> options, Action<string>? progress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Signal any in-flight preload that it's been superseded BEFORE we queue
        // behind it on the gate, so it can bail out early instead of committing.
        var myGen = ++_generation;

        await _gate.WaitAsync();
        try
        {
            if (_disposed || myGen != _generation) return;

            _timer.Stop();
            await WaitForUtteranceAsync();
            _tracks.Clear();
            _speaker = null;
            _groups = [];
            _nextIndex = 0;

            // Build into locals; commit only if still current at the end.
            var localTracks = new Dictionary<string, LangTrack>(StringComparer.OrdinalIgnoreCase);
            var count = options.Count;
            for (var i = 0; i < count; i++)
            {
                if (_disposed || myGen != _generation)
                {
                    Logger.Log($"Preload superseded mid-run (gen {myGen} != {_generation}), abandoning");
                    return;
                }

                var opt = options[i];
                progress?.Invoke($"Preparing {opt.Label} ({i + 1}/{count})…");

                IReadOnlyList<CueGroup> groups;
                try
                {
                    var cues = SubtitleParser.Parse(await File.ReadAllTextAsync(opt.SubtitlePath));
                    groups = SentenceGrouper.Group(cues, GroupGap);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Preload parse failed for {opt.Lang}", ex);
                    continue;
                }

                EmbeddedVoiceSpeaker? speaker;
                try
                {
                    // Get-or-load-and-register runs entirely on the worker thread, so
                    // the speaker is placed in _speakersByVoiceId on the same thread
                    // that Dispose uses, so it can never be created-but-untracked.
                    var voice = opt.Voice;
                    speaker = await _worker.RunAsync(() => GetOrLoadSpeaker(voice));
                }
                catch (Exception ex)
                {
                    Logger.Log($"Preload load failed for {opt.Voice.DisplayName}", ex);
                    continue;
                }

                if (speaker is null) continue; // disposed while loading

                localTracks[opt.Lang] = new LangTrack(opt.Lang, opt.Label, opt.Voice, groups, speaker);
                Logger.Log($"Preloaded {opt.Lang} voice={opt.Voice.DisplayName} groups={groups.Count}");
            }

            // Commit atomically only if this run is still the current intent.
            if (_disposed || myGen != _generation)
            {
                Logger.Log($"Preload superseded before commit (gen {myGen} != {_generation}), discarding {localTracks.Count} track(s)");
                return;
            }

            _tracks.Clear();
            foreach (var kv in localTracks) _tracks[kv.Key] = kv.Value;
            Logger.Log($"Preload committed gen={myGen} tracks=[{string.Join(",", _tracks.Keys)}]");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Worker-thread only. Returns the resident speaker for <paramref name="voice"/>,
    /// loading + pre-warming + registering it on first use. Registering here (not on
    /// the UI thread after the await) means a speaker is always tracked in
    /// <see cref="_speakersByVoiceId"/> before this call returns, so Dispose (which
    /// also runs on this worker) can never miss one. If disposal happened while
    /// loading, the freshly-created speaker is torn down here and null returned.
    /// </summary>
    private EmbeddedVoiceSpeaker? GetOrLoadSpeaker(VoiceInfo voice)
    {
        if (_disposed) return null;
        if (_speakersByVoiceId.TryGetValue(voice.Id, out var existing)) return existing;

        var s = EmbeddedVoiceSpeaker.Load(voice, license: null);
        // Pre-warm: pay the one-time ~1.3 s first-synth cost now so the first real
        // line for this voice renders in ~100 ms.
        try { _ = s.SpeakAsync(".").GetAwaiter().GetResult(); }
        catch { /* pre-warm is best-effort */ }

        if (_disposed)
        {
            try { s.Dispose(); } catch (Exception ex) { Logger.Log("Speaker dispose (post-load)", ex); }
            return null;
        }

        _speakersByVoiceId[voice.Id] = s;
        return s;
    }

    /// <summary>
    /// Switch the narration to an already pre-loaded language. Instant: no I/O, no
    /// model load. The next sentence renders in the new voice (~100 ms); any line
    /// currently speaking finishes first, so the change lands on the following line
    /// without pausing the video.
    /// </summary>
    public bool SetActiveLanguage(string lang)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tracks.TryGetValue(lang, out var track))
        {
            Logger.Log($"SetActiveLanguage miss lang={lang} (not preloaded)");
            return false;
        }

        _speaker = track.Speaker;
        _groups = track.Groups;

        var pos = _player.PlaybackSession.Position;
        ResyncForSwitch(pos);
        _timer.Start();
        StatusChanged?.Invoke($"Ready · {_groups.Count} sentences · {track.Voice.DisplayName}");
        Logger.Log($"SetActiveLanguage lang={lang} voice={track.Voice.DisplayName} groups={_groups.Count} nextIndex={_nextIndex} pos={pos} speaking={_speaking} speakingIdx={_speakingIndex} speakingStart={_speakingStart}");
        return true;
    }

    /// <summary>Stop scheduling and drop the current tracks (e.g. before loading a new video).</summary>
    public async Task ResetAsync()
    {
        // Bump the generation before queuing so any in-flight preload bails out.
        _generation++;
        await _gate.WaitAsync();
        try
        {
            _timer.Stop();
            await WaitForUtteranceAsync();
            _tracks.Clear();
            _groups = [];
            _nextIndex = 0;
            _speaker = null; // resident speakers stay loaded for reuse
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_speaker is null || _disposed) return;
        var session = _player.PlaybackSession;
        if (session is null) return;

        var pos = session.Position;

        // Explicit user seek: jump the next-sentence pointer to the seeked
        // position, dropping any sentences already passed. A sentence still
        // speaking is left to finish; its stale index won't advance the pointer
        // because of the guard in StartUtterance's completion.
        if (_seekPending)
        {
            _seekPending = false;
            ResyncToPosition(pos);
            Logger.Log($"Seek -> resync pos={pos} nextIndex={_nextIndex} speaking={_speaking}");
        }

        // Paused/stopped: don't start new audio. Whatever is already speaking
        // finishes on its own (a few seconds at most) and won't restart.
        if (session.PlaybackState != MediaPlaybackState.Playing) return;

        if (_speaking || _nextIndex >= _groups.Count) return;

        var group = _groups[_nextIndex];
        if (pos >= group.Start - Lead)
            StartUtterance(group);
    }

    private void StartUtterance(CueGroup group)
    {
        var speaker = _speaker;
        if (speaker is null) return;

        _speaking = true;
        var indexSpoken = _nextIndex;
        _speakingIndex = indexSpoken;
        _speakingStart = group.Start;

        SentenceStarted?.Invoke(group.Text);
        Logger.Log($"Utterance start idx={indexSpoken} start={group.Start} pos={_player.PlaybackSession.Position} \"{Trim(group.Text)}\"");

        _utteranceTask = _worker.RunAsync(() =>
        {
            try
            {
                // No cancellation token: cancelling triggers a cross-thread native
                // StopSpeakingAsync that crashes the runtime. Utterances finish.
                speaker.SpeakToDefaultOutputAsync(group.Text, OnWord, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Logger.Log($"Utterance done idx={indexSpoken}");
                _dispatcher.TryEnqueue(() =>
                {
                    _speaking = false;
                    // Only advance if this utterance finished and nothing (seek/switch)
                    // moved the playhead onto a different sentence meanwhile.
                    if (indexSpoken == _nextIndex) _nextIndex++;
                });
            }
            catch (Exception ex)
            {
                Logger.Log("Utterance failed", ex);
                _dispatcher.TryEnqueue(() =>
                {
                    _speaking = false;
                    StatusChanged?.Invoke($"Speech error: {ex.Message}");
                });
            }
        });
    }

    private void OnWord(SpokenWord word)
    {
        var handler = WordSpoken;
        if (handler is null) return;
        _dispatcher.TryEnqueue(() => handler(word));
    }

    // Wait for the in-flight utterance (if any) to finish. Never cancels; the
    // native runtime only tears a synth down safely from its own worker thread.
    private async Task WaitForUtteranceAsync()
    {
        var task = _utteranceTask;
        if (task is not null)
        {
            try { await task; } catch { /* already observed inside the task */ }
        }
        _speaking = false;
    }

    private void ResyncToPosition(TimeSpan pos)
    {
        var i = 0;
        while (i < _groups.Count && _groups[i].Start < pos) i++;
        _nextIndex = i;
    }

    /// <summary>
    /// Recompute the next sentence to speak after a <b>language switch</b>. Unlike a
    /// seek, this must never replay the line that is currently playing or one that
    /// has already been spoken; otherwise the new voice re-reads the last sentence.
    ///
    /// Dedup is by <b>timestamp</b>, never by an index carried across tracks: the two
    /// languages may group their cues differently, so an index into the old track is
    /// meaningless in the new one. The next index is the first group in the NEW track
    /// whose Start is strictly after MAX(current position, the currently-speaking
    /// line's Start). Using the speaking line's Start covers the case where an
    /// utterance began up to <see cref="Lead"/> before its own timestamp, so its
    /// Start can still be ahead of the current position. This guarantees no replay
    /// and no skip regardless of how the languages are grouped.
    /// </summary>
    private void ResyncForSwitch(TimeSpan pos)
    {
        var boundary = pos;
        if (_speaking && _speakingStart > boundary) boundary = _speakingStart;

        var i = 0;
        while (i < _groups.Count && _groups[i].Start <= boundary) i++;
        _nextIndex = i;
    }

    private static string Trim(string s) => s.Length <= 40 ? s : s[..40] + "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        try { _player.PlaybackSession.SeekCompleted -= OnSeekCompleted; } catch { }

        var inFlight = _utteranceTask;
        try
        {
            _worker.RunAsync(() =>
            {
                // Let a sentence in progress drain, then dispose every resident
                // speaker on the same thread that created it (thread affinity).
                try { inFlight?.Wait(TimeSpan.FromSeconds(6)); } catch { }
                foreach (var s in _speakersByVoiceId.Values)
                {
                    try { s.Dispose(); } catch (Exception ex) { Logger.Log("Speaker dispose", ex); }
                }
                _speakersByVoiceId.Clear();
                _tracks.Clear();
            }).Wait(TimeSpan.FromSeconds(8));
        }
        catch (Exception ex) { Logger.Log("Dispose failed", ex); }

        _worker.Dispose();
        _gate.Dispose();
    }

    /// <summary>A pre-loaded language: its parsed sentences and the voice that speaks them.</summary>
    private sealed record LangTrack(
        string Lang,
        string Label,
        VoiceInfo Voice,
        IReadOnlyList<CueGroup> Groups,
        EmbeddedVoiceSpeaker Speaker);

    /// <summary>
    /// A single dedicated background thread that runs queued work items in order.
    /// Gives the thread-hostile speaker a stable thread affinity and serializes
    /// every native call.
    /// </summary>
    private sealed class SerialWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public SerialWorker(string name)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Run()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                try { work(); } catch (Exception ex) { Logger.Log("SerialWorker item", ex); }
            }
        }

        public Task<T> RunAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add(() =>
                {
                    try { tcs.SetResult(func()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException)
            {
                tcs.SetCanceled(); // queue completed (disposing)
            }
            return tcs.Task;
        }

        public Task RunAsync(Action action) => RunAsync(() => { action(); return true; });

        public void Dispose() => _queue.CompleteAdding();
    }
}
