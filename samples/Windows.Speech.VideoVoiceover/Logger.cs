using System.Text;

namespace Windows.Speech_VideoVoiceover;

/// <summary>
/// Minimal append-only file logger written next to the executable. Used to
/// capture unhandled exceptions and scheduler diagnostics for this sample.
/// </summary>
internal static class Logger
{
    private static readonly object Gate = new();
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "voiceover.log");

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}";
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* logging must never throw */ }
        }
    }

    public static void Log(string context, Exception ex) =>
        Log($"{context}: {ex.GetType().Name}: {ex.Message}\n{ex}");
}
