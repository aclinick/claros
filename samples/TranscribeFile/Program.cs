using Windows.Speech;

// Offline speech-to-text with the Windows Live Captions recognition model,
// through the Windows.Speech library. Everything runs on-device; no
// network call is made.
//
//   dotnet run -r win-arm64 -- <path-to-16kHz-mono-wav> [locale]
//
// The locale (default en-US) selects which installed
// MicrosoftWindows.Speech.<locale> recognition pack to use. Install more from
// Settings > Time & language > Speech (or Accessibility > Captions).

var wavPath = args.Length > 0 ? args[0] : null;
var locale = args.Length > 1 ? args[1] : "en-US";

if (wavPath is null)
{
    Console.Error.WriteLine("usage: TranscribeFile <path-to-16kHz-mono-wav> [locale]");
    return 2;
}

using var platform = new SpeechPlatform();

Console.WriteLine("Installed recognition models:");
foreach (var m in platform.ListRecognitionModels())
{
    Console.WriteLine($"  {m.Locale,-6} {m.ModelName}");
}

var model = platform.FindRecognitionModel(locale);
if (model is null)
{
    Console.Error.WriteLine($"No recognition model installed for '{locale}'.");
    return 1;
}

Console.WriteLine($"\nUsing {model.ModelName} ({model.Locale})");
Console.WriteLine($"Transcribing {wavPath} ...\n");

using var transcriber = platform.CreateTranscriber(model);
var result = await transcriber.TranscribeFileAsync(
    wavPath,
    onPartial: text => Console.WriteLine($"  ~ {text}"));

Console.WriteLine("\n===== TRANSCRIPT =====");
foreach (var seg in result.Segments)
{
    Console.WriteLine($"[{seg.Offset:hh\\:mm\\:ss\\.ff}] {seg.Text}");
}
Console.WriteLine("\n" + result.Text);
Console.Out.Flush();

// The embedded recognition engine can fault while its native worker threads are
// torn down at process exit. All results are already captured above, so exit
// promptly to avoid that teardown race.
Environment.Exit(0);
return 0;
