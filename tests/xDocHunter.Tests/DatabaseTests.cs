using xDocHunter.Models;
using xDocHunter.Services;
using Xunit;

namespace xDocHunter.Tests;

/// <summary>
/// All tests use a per-test TestSandbox so they write only to a GUID temp path.
/// No writes go to %LocalAppData%\xDocHunter\ or anywhere outside the sandbox.
/// </summary>
public class DatabaseTests : IDisposable
{
    private readonly TestSandbox _sandbox = new();
    private readonly Database _db;

    public DatabaseTests()
    {
        _db = new Database(_sandbox.DbPath());
    }

    public void Dispose()
    {
        _db.Dispose();
        _sandbox.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileEntry MakeFile(string path, string name, string dir, string ext,
        long size = 100, string content = "", bool isDir = false) => new()
    {
        FullPath    = path,
        Name        = name,
        Directory   = dir,
        Extension   = ext,
        SizeBytes   = size,
        ModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IsDirectory = isDir,
        Content     = content
    };

    private Task Insert(params FileEntry[] entries) =>
        _db.InsertBatchAsync(entries);

    // ── InsertBatch / Count / TotalSize ───────────────────────────────────────

    [Fact]
    public async Task InsertBatch_EmptyInput_LeavesDbEmpty()
    {
        await _db.InsertBatchAsync([]);
        Assert.Equal(0, _db.Count());
        Assert.Equal(0, _db.TotalSize());
    }

    [Fact]
    public async Task InsertBatch_SingleEntry_CountIsOne()
    {
        await Insert(MakeFile(@"C:\a\b.txt", "b.txt", @"C:\a", ".txt", 512));
        Assert.Equal(1, _db.Count());
        Assert.Equal(512, _db.TotalSize());
    }

    [Fact]
    public async Task InsertBatch_MultipleEntries_CountIsCorrect()
    {
        await Insert(
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt", 100),
            MakeFile(@"C:\a\2.pdf", "2.pdf", @"C:\a", ".pdf", 200),
            MakeFile(@"C:\a\3.cs",  "3.cs",  @"C:\a", ".cs",  300)
        );
        Assert.Equal(3, _db.Count());
        Assert.Equal(600, _db.TotalSize());
    }

    [Fact]
    public async Task InsertBatch_DuplicatePath_UpsertsRow()
    {
        await Insert(MakeFile(@"C:\a\b.txt", "b.txt", @"C:\a", ".txt", 100));
        await Insert(MakeFile(@"C:\a\b.txt", "b.txt", @"C:\a", ".txt", 999));
        Assert.Equal(1, _db.Count());
        Assert.Equal(999, _db.TotalSize()); // updated value
    }

    [Fact]
    public async Task TotalSize_ExcludesDirectories()
    {
        await Insert(
            MakeFile(@"C:\a",        "a",     @"C:\",  "",     0, isDir: true),
            MakeFile(@"C:\a\b.txt",  "b.txt", @"C:\a", ".txt", 500)
        );
        // Directory has size 0, but even if it had a size it should be excluded
        Assert.Equal(500, _db.TotalSize());
    }

    // ── Search — no query (browse all) ────────────────────────────────────────

    [Fact]
    public async Task Search_EmptyQuery_ReturnsAllEntries()
    {
        await Insert(
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt"),
            MakeFile(@"C:\a\2.pdf", "2.pdf", @"C:\a", ".pdf")
        );
        var results = _db.Search(null, mode: SearchMode.Filename);
        Assert.Equal(2, results.Count);
    }

    // ── Search — Filename mode (LIKE) ──────────────────────────────────────────

    [Fact]
    public async Task Search_FilenameMode_MatchesByName()
    {
        await Insert(
            MakeFile(@"C:\a\report.txt",  "report.txt",  @"C:\a", ".txt"),
            MakeFile(@"C:\a\invoice.pdf", "invoice.pdf", @"C:\a", ".pdf"),
            MakeFile(@"C:\a\summary.txt", "summary.txt", @"C:\a", ".txt")
        );
        var results = _db.Search("report", mode: SearchMode.Filename);
        Assert.Single(results);
        Assert.Equal("report.txt", results[0].Name);
    }

    [Fact]
    public async Task Search_FilenameMode_MatchesByPath()
    {
        await Insert(
            MakeFile(@"C:\projects\alpha\main.cs", "main.cs", @"C:\projects\alpha", ".cs"),
            MakeFile(@"C:\projects\beta\main.cs",  "main.cs", @"C:\projects\beta",  ".cs")
        );
        var results = _db.Search("alpha", mode: SearchMode.Filename);
        Assert.Single(results);
        Assert.Equal(@"C:\projects\alpha\main.cs", results[0].FullPath);
    }

    [Fact]
    public async Task Search_FilenameMode_MultiTokenAnds()
    {
        // Both tokens must match.
        await Insert(
            MakeFile(@"C:\a\2024_report.pdf", "2024_report.pdf", @"C:\a", ".pdf"),
            MakeFile(@"C:\a\report_only.pdf",  "report_only.pdf",  @"C:\a", ".pdf")
        );
        var results = _db.Search("2024 report", mode: SearchMode.Filename);
        Assert.Single(results);
        Assert.Equal("2024_report.pdf", results[0].Name);
    }

    [Fact]
    public async Task Search_FilenameMode_IsCaseInsensitive()
    {
        await Insert(MakeFile(@"C:\a\README.md", "README.md", @"C:\a", ".md"));
        var results = _db.Search("readme", mode: SearchMode.Filename);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_FilenameMode_NoMatch_ReturnsEmpty()
    {
        await Insert(MakeFile(@"C:\a\hello.txt", "hello.txt", @"C:\a", ".txt"));
        var results = _db.Search("zzznomatch", mode: SearchMode.Filename);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_FilenameMode_DoesNotSearchContent()
    {
        // File content contains the search term but name/path do not.
        await Insert(MakeFile(@"C:\a\notes.txt", "notes.txt", @"C:\a", ".txt",
            content: "this has the secret keyword inside"));
        var results = _db.Search("secret", mode: SearchMode.Filename);
        Assert.Empty(results); // filename mode should NOT find it via content
    }

    // ── Search — Content mode (FTS5) ───────────────────────────────────────────

    [Fact]
    public async Task Search_ContentMode_MatchesByContent()
    {
        await Insert(
            MakeFile(@"C:\a\a.txt", "a.txt", @"C:\a", ".txt", content: "hello world"),
            MakeFile(@"C:\a\b.txt", "b.txt", @"C:\a", ".txt", content: "goodbye moon")
        );
        var results = _db.Search("hello", mode: SearchMode.Content);
        Assert.Single(results);
        Assert.Equal("a.txt", results[0].Name);
    }

    [Fact]
    public async Task Search_ContentMode_MatchesByName()
    {
        // Content mode also searches name via FTS (includeFilename=true by default).
        await Insert(MakeFile(@"C:\a\quarterly_report.txt", "quarterly_report.txt",
            @"C:\a", ".txt", content: "nothing special"));
        var results = _db.Search("quarterly", mode: SearchMode.Content);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_ContentMode_MultiWordQuery_FindsMatch()
    {
        await Insert(MakeFile(@"C:\a\doc.txt", "doc.txt", @"C:\a", ".txt",
            content: "the quick brown fox jumps over the lazy dog"));
        var results = _db.Search("quick fox", mode: SearchMode.Content);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_ContentMode_NoMatch_ReturnsEmpty()
    {
        await Insert(MakeFile(@"C:\a\doc.txt", "doc.txt", @"C:\a", ".txt",
            content: "ordinary text here"));
        var results = _db.Search("zzznomatch", mode: SearchMode.Content);
        Assert.Empty(results);
    }

    // ── Folder filter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_FolderFilter_LimitsToSubtree()
    {
        await Insert(
            MakeFile(@"C:\a\sub\file1.txt",  "file1.txt", @"C:\a\sub",  ".txt"),
            MakeFile(@"C:\a\sub\deep\f2.txt","f2.txt",    @"C:\a\sub\deep", ".txt"),
            MakeFile(@"C:\b\other.txt",       "other.txt", @"C:\b",      ".txt")
        );
        var results = _db.Search(null, folderPath: @"C:\a\sub", mode: SearchMode.Filename);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.StartsWith(@"C:\a\sub", r.Directory));
    }

    [Fact]
    public async Task Search_FolderFilter_ExactMatch_Included()
    {
        await Insert(MakeFile(@"C:\a\file.txt", "file.txt", @"C:\a", ".txt"));
        var results = _db.Search(null, folderPath: @"C:\a", mode: SearchMode.Filename);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_FolderFilter_NoTrailingSlashNormalized()
    {
        await Insert(MakeFile(@"C:\a\file.txt", "file.txt", @"C:\a", ".txt"));
        var results = _db.Search(null, folderPath: @"C:\a\", mode: SearchMode.Filename);
        Assert.Single(results);
    }

    // ── Extension filter ──────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ExtensionFilter_ReturnsOnlyMatchingExtensions()
    {
        await Insert(
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt"),
            MakeFile(@"C:\a\2.pdf", "2.pdf", @"C:\a", ".pdf"),
            MakeFile(@"C:\a\3.cs",  "3.cs",  @"C:\a", ".cs")
        );
        var results = _db.Search(null,
            extensions: [".txt", ".pdf"], mode: SearchMode.Filename);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains(r.Extension, new[] { ".txt", ".pdf" }));
    }

    [Fact]
    public async Task Search_ExtensionFilter_FolderSentinel_IncludesFolders()
    {
        await Insert(
            MakeFile(@"C:\a\dir",    "dir",    @"C:\", "",     0, isDir: true),
            MakeFile(@"C:\a\f.txt",  "f.txt",  @"C:\a", ".txt")
        );
        var results = _db.Search(null,
            extensions: [Database.FolderSentinel], mode: SearchMode.Filename);
        Assert.Single(results);
        Assert.True(results[0].IsDirectory);
    }

    // ── GetDistinctExtensions ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDistinctExtensions_ReturnsCorrectCounts()
    {
        await Insert(
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt"),
            MakeFile(@"C:\a\2.txt", "2.txt", @"C:\a", ".txt"),
            MakeFile(@"C:\a\3.pdf", "3.pdf", @"C:\a", ".pdf")
        );
        var exts = _db.GetDistinctExtensions();
        var dict = exts.ToDictionary(x => x.Extension, x => x.Count);
        Assert.Equal(2, dict[".txt"]);
        Assert.Equal(1, dict[".pdf"]);
    }

    [Fact]
    public async Task GetDistinctExtensions_IncludesFolderSentinel()
    {
        await Insert(
            MakeFile(@"C:\a",       "a",     @"C:\", "", isDir: true),
            MakeFile(@"C:\a\f.txt", "f.txt", @"C:\a", ".txt")
        );
        var exts = _db.GetDistinctExtensions();
        var sentinel = exts.FirstOrDefault(x => x.Extension == Database.FolderSentinel);
        Assert.Equal(1, sentinel.Count);
    }

    [Fact]
    public async Task GetDistinctExtensions_EmptyDb_ReturnsEmptyList()
    {
        Assert.Empty(_db.GetDistinctExtensions());
    }

    // ── ClearAll ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAll_RemovesAllEntries()
    {
        await Insert(
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt"),
            MakeFile(@"C:\a\2.pdf", "2.pdf", @"C:\a", ".pdf")
        );
        _db.ClearAll();
        Assert.Equal(0, _db.Count()); // should return 0 after clear
    }

    // ── Search Limit ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_LimitParameter_CappsResults()
    {
        var entries = Enumerable.Range(1, 20)
            .Select(i => MakeFile($@"C:\a\{i}.txt", $"{i}.txt", @"C:\a", ".txt"))
            .ToArray();
        await Insert(entries);
        var results = _db.Search(null, limit: 5, mode: SearchMode.Filename);
        Assert.Equal(5, results.Count);
    }
}
