namespace Claros.Tests;

/// <summary>
/// Creates a unique temp directory and recursively deletes it on dispose so
/// directory-backed helpers (overlays, native staging) can be tested without
/// leaking artifacts. Deleting the directory removes symlink entries without
/// touching their targets.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    private TempDir(string path) => Path = path;

    public static TempDir Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "wnv_test_dir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }

    /// <summary>Create a file under this directory and return its full path.</summary>
    public string WriteFile(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string Sub(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
