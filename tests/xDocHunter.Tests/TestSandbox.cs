using System.IO;

namespace xDocHunter.Tests;

/// <summary>
/// IDisposable sandbox for tests. Creates a unique directory under the system temp folder,
/// gives helpers to build file fixtures, and recursively deletes the sandbox on Dispose.
///
/// READ-ONLY GUARANTEE: All file writes by the tests go through this sandbox. Nothing
/// outside <see cref="Root"/> is touched. The path lives under Path.GetTempPath()
/// + a per-instance GUID, so concurrent test runs cannot collide.
/// </summary>
public sealed class TestSandbox : IDisposable
{
    public string Root { get; }

    public TestSandbox()
    {
        Root = Path.Combine(Path.GetTempPath(), "xDocHunter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Creates a file inside the sandbox with the given relative path and text content.</summary>
    public string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Creates a file inside the sandbox with raw bytes (for binary-detection tests).</summary>
    public string WriteBytes(string relativePath, byte[] bytes)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    /// <summary>Creates a subdirectory inside the sandbox.</summary>
    public string MakeDir(string relativePath)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Convenience: a path inside the sandbox suitable for an SQLite database.</summary>
    public string DbPath(string name = "test.db") => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup. Some files may be locked briefly by SQLite/WebView2.
        }
    }
}
