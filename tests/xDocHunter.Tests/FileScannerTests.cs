using System.IO;
using xDocHunter.Models;
using xDocHunter.Services;
using Xunit;

namespace xDocHunter.Tests;

public class FileScannerTests : IDisposable
{
    private readonly TestSandbox _sandbox = new();
    private readonly FileScanner _scanner = new();

    public void Dispose() => _sandbox.Dispose();

    private async Task<List<FileEntry>> ScanAll(string root, ScanFilterOptions? filter = null)
    {
        var results = new List<FileEntry>();
        await foreach (var entry in _scanner.ScanAsync(root, filter))
            results.Add(entry);
        return results;
    }

    // ── Basic discovery ───────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_SingleFile_YieldsOneEntry()
    {
        _sandbox.WriteFile("hello.txt", "hello");
        var entries = await ScanAll(_sandbox.Root);
        Assert.Single(entries);
        Assert.Equal("hello.txt", entries[0].Name);
        Assert.False(entries[0].IsDirectory);
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_YieldsNoEntries()
    {
        var entries = await ScanAll(_sandbox.Root);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task ScanAsync_Subdirectory_YieldsDirectoryEntry()
    {
        _sandbox.MakeDir("subdir");
        var entries = await ScanAll(_sandbox.Root);
        var dir = entries.SingleOrDefault(e => e.IsDirectory);
        Assert.NotNull(dir);
        Assert.Equal("subdir", dir!.Name);
    }

    [Fact]
    public async Task ScanAsync_NestedFiles_DiscoveredRecursively()
    {
        _sandbox.WriteFile("a.txt", "a");
        _sandbox.WriteFile(@"sub\b.txt", "b");
        _sandbox.WriteFile(@"sub\deep\c.txt", "c");

        var entries = await ScanAll(_sandbox.Root);
        var files = entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.Name == "a.txt");
        Assert.Contains(files, f => f.Name == "b.txt");
        Assert.Contains(files, f => f.Name == "c.txt");
    }

    [Fact]
    public async Task ScanAsync_DirectoryNotFound_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in _scanner.ScanAsync(@"C:\ThisDoesNotExistEver12345"))
            { }
        });
    }

    // ── FileEntry properties ──────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_FileEntry_HasCorrectMetadata()
    {
        _sandbox.WriteFile("data.txt", "hello world");

        var entries = await ScanAll(_sandbox.Root);
        var entry = entries.Single(e => !e.IsDirectory);

        Assert.Equal("data.txt", entry.Name);
        Assert.Equal(".txt", entry.Extension);
        Assert.Equal(_sandbox.Root, entry.Directory);
        Assert.True(entry.SizeBytes > 0);
        Assert.NotEqual(default, entry.ModifiedUtc);
    }

    [Fact]
    public async Task ScanAsync_DirectoryEntry_HasCorrectMetadata()
    {
        _sandbox.MakeDir("mydir");

        var entries = await ScanAll(_sandbox.Root);
        var dir = entries.Single(e => e.IsDirectory);

        Assert.Equal("mydir", dir.Name);
        Assert.Equal(string.Empty, dir.Extension);
        Assert.Equal(_sandbox.Root, dir.Directory);
        Assert.True(dir.IsDirectory);
    }

    // ── Filename mode: no content extracted ───────────────────────────────────

    [Fact]
    public async Task ScanAsync_FilenameMode_ContentIsEmpty()
    {
        _sandbox.WriteFile("readme.txt", "This is important content that should not be indexed.");
        var filter = new ScanFilterOptions { Mode = SearchMode.Filename };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal(string.Empty, file.Content);
    }

    [Fact]
    public async Task ScanAsync_DefaultMode_IsFilenameMode_ContentIsEmpty()
    {
        _sandbox.WriteFile("readme.txt", "Some text content.");
        // No filter passed → default is Filename mode
        var entries = await ScanAll(_sandbox.Root, filter: null);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal(string.Empty, file.Content);
    }

    [Fact]
    public async Task ScanAsync_FilenameMode_UnsupportedExtension_ContentIsEmpty()
    {
        _sandbox.WriteFile("image.jpg", "fake image data");
        var filter = new ScanFilterOptions { Mode = SearchMode.Filename };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal(string.Empty, file.Content);
    }

    // ── Content mode: text is extracted ──────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ContentMode_PlainTextFile_ContentExtracted()
    {
        _sandbox.WriteFile("notes.txt", "The quick brown fox.");
        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal("The quick brown fox.", file.Content);
    }

    [Fact]
    public async Task ScanAsync_ContentMode_CSharpFile_ContentExtracted()
    {
        _sandbox.WriteFile("Program.cs", "Console.WriteLine(\"Hello\");");
        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal("Console.WriteLine(\"Hello\");", file.Content);
    }

    [Fact]
    public async Task ScanAsync_ContentMode_UnsupportedExtension_ContentIsEmpty()
    {
        // .jpg is not supported by TextExtractor even in content mode
        _sandbox.WriteFile("photo.jpg", "not a real image");
        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal(string.Empty, file.Content);
    }

    [Fact]
    public async Task ScanAsync_ContentMode_BinaryFile_ContentIsEmpty()
    {
        // A .txt file that contains null bytes is treated as binary
        _sandbox.WriteBytes("binary.txt", new byte[] { 0x48, 0x00, 0x65, 0x6C, 0x6C, 0x6F });
        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var file = entries.Single(e => !e.IsDirectory);
        Assert.Equal(string.Empty, file.Content);
    }

    [Fact]
    public async Task ScanAsync_ContentMode_DirectoryEntry_HasNoContent()
    {
        _sandbox.MakeDir("somedir");
        _sandbox.WriteFile(@"somedir\file.txt", "text");
        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var dir = entries.First(e => e.IsDirectory);
        Assert.Equal(string.Empty, dir.Content);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_CancelledToken_StopsEnumeration()
    {
        // Create many files to ensure cancellation can be exercised
        for (int i = 0; i < 50; i++)
            _sandbox.WriteFile($"file{i}.txt", "content");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        var results = new List<FileEntry>();
        await foreach (var entry in _scanner.ScanAsync(_sandbox.Root, ct: cts.Token))
            results.Add(entry);

        // Should have collected 0 or very few entries (depends on scheduling)
        Assert.True(results.Count < 50);
    }

    // ── Multiple extensions ───────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_MixedExtensions_AllDiscoveredInContentMode()
    {
        _sandbox.WriteFile("a.txt", "text content");
        _sandbox.WriteFile("b.md",  "# Markdown");
        _sandbox.WriteFile("c.jpg", "not an image");

        var filter = new ScanFilterOptions { Mode = SearchMode.Content };
        var entries = await ScanAll(_sandbox.Root, filter);
        var files = entries.Where(e => !e.IsDirectory).ToList();

        Assert.Equal(3, files.Count);

        var txt = files.Single(f => f.Name == "a.txt");
        var md  = files.Single(f => f.Name == "b.md");
        var jpg = files.Single(f => f.Name == "c.jpg");

        Assert.Equal("text content", txt.Content);
        Assert.Equal("# Markdown",  md.Content);
        Assert.Equal(string.Empty,   jpg.Content); // not supported
    }
}
