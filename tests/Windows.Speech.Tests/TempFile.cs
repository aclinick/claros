namespace Windows.Speech.Tests;

/// <summary>
/// Creates a unique temp file (optionally with content) and deletes it on
/// dispose so file-backed loaders can be tested without leaking artifacts.
/// </summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; }

    private TempFile(string path) => Path = path;

    public static TempFile Create(string? extension = null)
    {
        var path = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            "wnv_test_" + Guid.NewGuid().ToString("N") + (extension ?? ".tmp"));
        return new TempFile(path);
    }

    public static TempFile WithText(string content, string? extension = null)
    {
        var f = Create(extension);
        File.WriteAllText(f.Path, content);
        return f;
    }

    public static TempFile WithBytes(byte[] content, string? extension = null)
    {
        var f = Create(extension);
        File.WriteAllBytes(f.Path, content);
        return f;
    }

    public void Dispose()
    {
        try { if (File.Exists(Path)) File.Delete(Path); }
        catch { /* best effort cleanup */ }
    }
}
