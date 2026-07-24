// ListVoices — discovery only.
//
// Shows how to enumerate the Windows Natural Voices installed on the machine
// through SpeechPlatform, read each voice's metadata (VoiceInfo), and react to
// install/update/uninstall events without polling. No synthesis happens here.
using Windows.Speech;

using var platform = new SpeechPlatform();

// The platform raises VoicesChanged whenever the OS installs, updates, or
// removes a voice package. A real app would rebuild its voice list here.
platform.VoicesChanged += (_, _) =>
    Console.WriteLine("\n[VoicesChanged] Installed voices changed — call ListVoicesAsync again.");

var voices = await platform.ListVoicesAsync();

if (voices.Count == 0)
{
    Console.WriteLine("No Windows Natural Voice packages installed.");
    Console.WriteLine("Install one from Settings > Time and Language > Speech > Manage voices.");
    return 1;
}

Console.WriteLine($"Found {voices.Count} installed Natural Voice(s):\n");
foreach (var v in voices)
{
    Console.WriteLine($"  {v.DisplayName}");
    Console.WriteLine($"    locale       {v.Locale}");
    Console.WriteLine($"    gender/age   {v.Gender} / {v.Age}");
    Console.WriteLine($"    vendor       {v.Vendor}");
    Console.WriteLine($"    version      {v.Version}");
    Console.WriteLine($"    package      {v.PackageFullName}");
    Console.WriteLine($"    installed at {v.InstalledPath}");
    Console.WriteLine();
}

// The VoicesChanged event only fires on subsequent OS package changes, so stay
// alive to observe them when running interactively.
if (!Console.IsInputRedirected)
{
    Console.WriteLine("Watching for voice changes. Press Enter to exit...");
    Console.ReadLine();
}

return 0;
