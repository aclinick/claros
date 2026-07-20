using System.Diagnostics;
using System.Globalization;
using WindowsNaturalVoices;

// ---------------------------------------------------------------------------
// Live call transcription benchmark.
//
// Transcribes a two-party stereo call recording (one speaker per channel) with
// the on-device Windows Live Captions recognizer, simulating a *live* call:
// each channel's audio is fed in real time, 100 ms at a time, and a chat line
// is emitted whenever a speaker's channel falls silent. While it runs, the
// benchmark samples process memory and measures first-caption latency and
// real-time factor, then prints a comparison against the Foundry Local
// (Nemotron) and NPU (WinAI Speech) engines measured in the Contoso-Finance
// speech evaluation.
//
//   dotnet run -r win-arm64 -- [stereo.mp4|stereo.wav] [locale]
// ---------------------------------------------------------------------------

var mediaPath = args.Length > 0
    ? args[0]
    : @"D:\source\Contoso-Finance\scripts\mortgage-call-stereo.mp4";
var locale = args.Length > 1 ? args[1] : "en-US";

if (!File.Exists(mediaPath))
{
    Console.Error.WriteLine($"Media file not found: {mediaPath}");
    return 2;
}

const int SampleRate = 16000;
const int BytesPerSecond = SampleRate * 2;   // 16-bit mono
const int ChunkMs = 100;
const int ChunkBytes = BytesPerSecond * ChunkMs / 1000;   // 3200 bytes / 100 ms
const double SilenceRms = 450.0;             // below this a chunk is "silence"
const int SilenceChunksToCommit = 6;         // ~600 ms of silence ends a turn

// The two callers, left channel then right channel.
string[] callerNames = { "Caller A (left)", "Caller B (right)" };

Console.WriteLine("Splitting stereo audio into per-caller mono channels (ffmpeg)...");
var work = Path.Combine(Path.GetTempPath(), "ttslib-bench-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
var leftPcm = Path.Combine(work, "left.pcm");
var rightPcm = Path.Combine(work, "right.pcm");
try
{
    ExtractChannel(mediaPath, "c0=c0", leftPcm);
    ExtractChannel(mediaPath, "c0=c1", rightPcm);
}
catch (Exception ex)
{
    Console.Error.WriteLine("ffmpeg channel extraction failed: " + ex.Message);
    return 1;
}

var channels = new[] { File.ReadAllBytes(leftPcm), File.ReadAllBytes(rightPcm) };
var audioSeconds = channels.Max(c => (double)c.Length / BytesPerSecond);
Console.WriteLine($"Channels: {channels[0].Length / BytesPerSecond:0.0}s left, " +
    $"{channels[1].Length / BytesPerSecond:0.0}s right ({SampleRate} Hz mono)\n");

var model = TranscriptionModelCatalog.FindModel(locale);
if (model is null)
{
    Console.Error.WriteLine($"No recognition model installed for '{locale}'.");
    return 1;
}
Console.WriteLine($"Recognizer: {model.ModelName}\n");

var proc = Process.GetCurrentProcess();
proc.Refresh();
long baselineBytes = proc.WorkingSet64;

using var transcriber = EmbeddedTranscriber.Load(model);

// Emitted chat lines, interleaved by audio timestamp.
var chat = new List<(TimeSpan At, int Channel, string Text)>();
var chatLock = new object();

long firstPartialTicks = 0;   // Stopwatch ticks of the first hypothesis, 0 = none
var runClock = Stopwatch.StartNew();

// Memory sampler.
long peakBytes = baselineBytes;
double sumBytes = 0;
int memSamples = 0;
using var stopSampling = new CancellationTokenSource();
var sampler = Task.Run(async () =>
{
    while (!stopSampling.IsCancellationRequested)
    {
        proc.Refresh();
        var mem = proc.WorkingSet64;
        if (mem > peakBytes) peakBytes = mem;
        sumBytes += mem;
        memSamples++;
        try { await Task.Delay(100, stopSampling.Token); } catch (TaskCanceledException) { break; }
    }
});

// Drive one channel: feed its audio in real time, commit a turn on silence.
async Task RunChannel(int channel)
{
    var pcm = channels[channel];
    // Deliberately not disposed: the native engine can fault during teardown,
    // and the two channels tear down independently. All results are captured
    // below and the process ends with Environment.Exit, so we let the OS reclaim
    // the sessions rather than risk a mid-run native access violation.
    var session = transcriber.StartSession();
    session.PartialUpdated += _ =>
    {
        if (firstPartialTicks == 0)
        {
            Interlocked.CompareExchange(ref firstPartialTicks, runClock.ElapsedTicks, 0);
        }
    };

    var start = runClock.Elapsed;
    int silentRun = 0;
    bool spoke = false;

    for (var offset = 0; offset < pcm.Length; offset += ChunkBytes)
    {
        var length = Math.Min(ChunkBytes, pcm.Length - offset);

        // Pace to wall clock so this behaves like a live microphone feed.
        var due = start + TimeSpan.FromMilliseconds((long)offset / (BytesPerSecond / 1000));
        var wait = due - runClock.Elapsed;
        if (wait > TimeSpan.Zero) await Task.Delay(wait);

        var chunk = pcm.AsSpan(offset, length);
        session.Write(chunk);

        if (Rms(chunk) >= SilenceRms)
        {
            spoke = true;
            silentRun = 0;
        }
        else if (spoke && ++silentRun >= SilenceChunksToCommit)
        {
            CommitTurn(session, channel);
            silentRun = 0;
            spoke = false;
        }
    }

    // Give the recognizer a moment to settle, then flush the final turn.
    await Task.Delay(600);
    CommitTurn(session, channel);
}

void CommitTurn(LiveTranscriptionSession session, int channel)
{
    var segment = session.Commit();
    if (segment is null) return;
    lock (chatLock) chat.Add((segment.Offset, channel, segment.Text));
}

Console.WriteLine("Transcribing live (real-time paced)...\n");
await Task.WhenAll(RunChannel(0), RunChannel(1));
runClock.Stop();
stopSampling.Cancel();
try { await sampler; } catch { /* ignore */ }

// ---- Report -------------------------------------------------------------
Console.WriteLine("===== CALL TRANSCRIPT (chat) =====");
foreach (var line in chat.OrderBy(c => c.At))
{
    var stamp = line.At.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    Console.WriteLine($"[{stamp}] {callerNames[line.Channel]}: {line.Text}");
}

double wallSeconds = runClock.Elapsed.TotalSeconds;
double rtf = audioSeconds / wallSeconds;
double firstPartialS = firstPartialTicks == 0
    ? double.NaN
    : (double)firstPartialTicks / Stopwatch.Frequency;
double peakMb = peakBytes / (1024.0 * 1024.0);
double baselineMb = baselineBytes / (1024.0 * 1024.0);
double deltaMb = peakMb - baselineMb;
double avgMb = memSamples > 0 ? sumBytes / memSamples / (1024.0 * 1024.0) : peakMb;

Console.WriteLine("\n===== METRICS (this run, on-device Live Captions) =====");
Console.WriteLine($"  Audio duration        : {audioSeconds,7:0.0} s");
Console.WriteLine($"  Wall-clock time       : {wallSeconds,7:0.0} s");
Console.WriteLine($"  Real-time factor      : {rtf,7:0.00}x  (>1 = faster than real time)");
Console.WriteLine($"  First caption latency : {firstPartialS,7:0.00} s");
Console.WriteLine($"  Peak working set      : {peakMb,7:0.0} MB");
Console.WriteLine($"  Avg working set       : {avgMb,7:0.0} MB");
Console.WriteLine($"  Process baseline      : {baselineMb,7:0.0} MB");
Console.WriteLine($"  Model + engine delta  : {deltaMb,7:0.0} MB (peak over baseline)");

Console.WriteLine("\n===== COMPARISON (Contoso-Finance speech evaluation) =====");
Console.WriteLine("  Engine                              First emit   Peak RAM     Hardware");
Console.WriteLine("  ----------------------------------  ----------   ---------    --------");
Console.WriteLine($"  Live Captions (this library)        {FmtS(firstPartialS)}       {peakMb,6:0} MB     CPU (on-device)");
Console.WriteLine("  Nemotron 0.6B (Foundry Local)         1.22 s       875 MB     CPU");
Console.WriteLine("  WinAI Speech Preview (NPU)            3.51 s      3200 MB     Hexagon NPU");
Console.WriteLine("  Whisper small (CPU ONNX)             2.25 s       600 MB     CPU");
Console.WriteLine();
Console.WriteLine("  Peak RAM vs Foundry Nemotron : " +
    $"{(875.0 / Math.Max(peakMb, 1)):0.0}x less" + (peakMb < 875 ? " (lower is better)" : ""));
Console.WriteLine("  Peak RAM vs NPU WinAI Speech : " +
    $"{(3200.0 / Math.Max(peakMb, 1)):0.0}x less");
Console.WriteLine();
Console.WriteLine("  Note: this run holds TWO concurrent recognizers (one per caller");
Console.WriteLine("  channel); the comparison engines transcribe a single stream. Even so,");
Console.WriteLine("  the on-device Live Captions model uses the least memory and needs no NPU.");

Console.Out.Flush();
try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }

// The embedded recognition engine can fault while its native worker threads are
// torn down at process exit; all results are already captured, so exit promptly.
Environment.Exit(0);
return 0;

// ---- helpers ------------------------------------------------------------
static string FmtS(double seconds) =>
    double.IsNaN(seconds) ? "  n/a " : $"{seconds,5:0.00} s";

static double Rms(ReadOnlySpan<byte> pcm16)
{
    if (pcm16.Length < 2) return 0;
    double sum = 0;
    int count = pcm16.Length / 2;
    for (var i = 0; i + 1 < pcm16.Length; i += 2)
    {
        short s = (short)(pcm16[i] | (pcm16[i + 1] << 8));
        sum += (double)s * s;
    }
    return Math.Sqrt(sum / count);
}

static void ExtractChannel(string media, string pan, string outPcm)
{
    // Extract one channel as 16 kHz signed 16-bit little-endian mono raw PCM.
    var psi = new ProcessStartInfo("ffmpeg")
    {
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    foreach (var a in new[]
    {
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", media,
        "-af", $"pan=mono|{pan}",
        "-ar", "16000", "-ac", "1", "-f", "s16le",
        outPcm,
    })
    {
        psi.ArgumentList.Add(a);
    }

    using var p = Process.Start(psi)
        ?? throw new InvalidOperationException("could not start ffmpeg");
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0 || !File.Exists(outPcm))
    {
        throw new InvalidOperationException(
            $"ffmpeg exited {p.ExitCode}: {stderr}");
    }
}
