using System.IO;
using Microsoft.Data.Sqlite;
using xDocHunter.Models;
using xDocHunter.Services;
using Xunit;

namespace xDocHunter.Tests;

/// <summary>
/// Verifies full round-trip: scan → save as .nfindex → import back, plus
/// legacy schema imports (xFinder and xCrawler-internal).
///
/// All file I/O is confined to TestSandbox. No writes go to
/// %LocalAppData%\xDocHunter\ or anywhere on the user's system.
/// </summary>
public class RoundTripTests : IDisposable
{
    private readonly TestSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private Database NewDb(string name = "test.db") =>
        new(_sandbox.DbPath(name));

    // ── xDocHunter round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndImport_FilenameMode_PreservesModeAndCount()
    {
        using var db = NewDb("source.db");
        await db.InsertBatchAsync([
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt", 100),
            MakeFile(@"C:\a\2.pdf", "2.pdf", @"C:\a", ".pdf", 200)
        ]);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 1.0, SearchMode.Filename);

        using var db2 = NewDb("import.db");
        var (count, mode) = await db2.ImportNfindexAsync(outPath);

        Assert.Equal(2, count);
        Assert.Equal(SearchMode.Filename, mode);
        Assert.Equal(2, db2.Count());
    }

    [Fact]
    public async Task SaveAndImport_ContentMode_PreservesModeAndContent()
    {
        using var db = NewDb("source.db");
        await db.InsertBatchAsync([
            MakeFile(@"C:\a\doc.txt", "doc.txt", @"C:\a", ".txt", 100,
                content: "searchable text inside")
        ]);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 1.0, SearchMode.Content);

        using var db2 = NewDb("import.db");
        var (count, mode) = await db2.ImportNfindexAsync(outPath);

        Assert.Equal(1, count);
        Assert.Equal(SearchMode.Content, mode);

        // After import, content search should work
        var results = db2.Search("searchable", mode: SearchMode.Content);
        Assert.Single(results);
        Assert.Equal("doc.txt", results[0].Name);
    }

    [Fact]
    public async Task SaveAndImport_MetadataSearchMode_IsReadBack()
    {
        using var db = NewDb("source.db");
        await db.InsertBatchAsync([MakeFile(@"C:\a\f.txt", "f.txt", @"C:\a", ".txt")]);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 0.5, SearchMode.Content);

        // Verify the raw metadata in the exported file
        var mode = db.GetNfindexSearchMode(outPath);
        Assert.Equal(SearchMode.Content, mode);
    }

    [Fact]
    public async Task SaveAndImport_FileSizes_Preserved()
    {
        using var db = NewDb("source.db");
        await db.InsertBatchAsync([
            MakeFile(@"C:\a\big.bin", "big.bin", @"C:\a", ".bin", 1_000_000)
        ]);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 1.0, SearchMode.Filename);

        using var db2 = NewDb("import.db");
        await db2.ImportNfindexAsync(outPath);

        Assert.Equal(1_000_000, db2.TotalSize());
    }

    // ── Legacy xFinder schema import ─────────────────────────────────────────

    [Fact]
    public async Task ImportNfindex_XFinderSchema_DetectsFilenameMode()
    {
        // Build a minimal xFinder-style .nfindex using the 'path' column name.
        var nfindexPath = _sandbox.DbPath("xfinder.nfindex");
        BuildXFinderSchema(nfindexPath, new[]
        {
            ("notes.txt", @"C:\docs\notes.txt", ".txt", 512L, @"C:\docs"),
            ("report.pdf", @"C:\docs\report.pdf", ".pdf", 2048L, @"C:\docs")
        });

        using var db = NewDb("import.db");
        var (count, mode) = await db.ImportNfindexAsync(nfindexPath);

        Assert.Equal(2, count);
        Assert.Equal(SearchMode.Filename, mode);
        Assert.Equal(2, db.Count());
    }

    [Fact]
    public async Task ImportNfindex_XFinderSchema_FilenameSearchWorks()
    {
        var nfindexPath = _sandbox.DbPath("xfinder.nfindex");
        BuildXFinderSchema(nfindexPath, new[]
        {
            ("quarterly_report.pdf", @"C:\docs\quarterly_report.pdf", ".pdf", 100L, @"C:\docs"),
            ("invoice.pdf",          @"C:\docs\invoice.pdf",          ".pdf", 100L, @"C:\docs")
        });

        using var db = NewDb("import.db");
        await db.ImportNfindexAsync(nfindexPath);

        var results = db.Search("quarterly", mode: SearchMode.Filename);
        Assert.Single(results);
        Assert.Equal("quarterly_report.pdf", results[0].Name);
    }

    // ── Legacy xCrawler internal schema import ────────────────────────────────

    [Fact]
    public async Task ImportNfindex_XCrawlerInternalSchema_DetectsContentMode()
    {
        // xCrawler internal DBs use full_path and file_content but no metadata table.
        var nfindexPath = _sandbox.DbPath("xcrawler.nfindex");
        BuildXCrawlerInternalSchema(nfindexPath, new[]
        {
            ("notes.txt", @"C:\docs\notes.txt", ".txt", 100L, @"C:\docs", "important notes here")
        });

        using var db = NewDb("import.db");
        var (count, mode) = await db.ImportNfindexAsync(nfindexPath);

        Assert.Equal(1, count);
        Assert.Equal(SearchMode.Content, mode);
    }

    [Fact]
    public async Task ImportNfindex_XCrawlerInternalSchema_ContentSearchWorks()
    {
        var nfindexPath = _sandbox.DbPath("xcrawler.nfindex");
        BuildXCrawlerInternalSchema(nfindexPath, new[]
        {
            ("contract.txt", @"C:\docs\contract.txt", ".txt", 100L, @"C:\docs", "the indemnification clause applies"),
            ("readme.txt",   @"C:\docs\readme.txt",   ".txt", 100L, @"C:\docs", "installation guide")
        });

        using var db = NewDb("import.db");
        await db.ImportNfindexAsync(nfindexPath);

        var results = db.Search("indemnification", mode: SearchMode.Content);
        Assert.Single(results);
        Assert.Equal("contract.txt", results[0].Name);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportNfindex_CancelledToken_ThrowsOrReturnsPartial()
    {
        using var db = NewDb("source.db");
        var entries = Enumerable.Range(1, 100)
            .Select(i => MakeFile($@"C:\a\{i}.txt", $"{i}.txt", @"C:\a", ".txt"))
            .ToArray();
        await db.InsertBatchAsync(entries);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 1.0, SearchMode.Filename);

        using var db2 = NewDb("import.db");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await db2.ImportNfindexAsync(outPath, ct: cts.Token));
    }

    // ── GetNfindexInfo ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNfindexInfo_ReturnsCorrectFileCount()
    {
        using var db = NewDb("source.db");
        await db.InsertBatchAsync([
            MakeFile(@"C:\a\1.txt", "1.txt", @"C:\a", ".txt", 100),
            MakeFile(@"C:\a\2.txt", "2.txt", @"C:\a", ".txt", 200),
            MakeFile(@"C:\a\3.txt", "3.txt", @"C:\a", ".txt", 300)
        ]);

        var outPath = _sandbox.DbPath("export.nfindex");
        await db.SaveAsSqliteAsync(outPath, @"C:\a", 1.0, SearchMode.Filename);

        var (totalFiles, totalSize) = db.GetNfindexInfo(outPath);
        Assert.Equal(3, totalFiles);
        Assert.Equal(600, totalSize);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FileEntry MakeFile(string path, string name, string dir, string ext,
        long size = 100, string content = "") => new()
    {
        FullPath    = path,
        Name        = name,
        Directory   = dir,
        Extension   = ext,
        SizeBytes   = size,
        ModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IsDirectory = false,
        Content     = content
    };

    /// <summary>
    /// Builds a minimal xFinder-style .nfindex file:
    /// - 'files' table uses 'path' (not 'full_path'), 'size', 'modifiedAt' (ISO text), 'isFolder', 'parentPath'
    /// - No metadata table, no file_content column
    /// </summary>
    private static void BuildXFinderSchema(string dbPath,
        IEnumerable<(string name, string path, string ext, long size, string parentPath)> rows)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE files (
                id        TEXT PRIMARY KEY,
                name      TEXT NOT NULL,
                path      TEXT NOT NULL,
                extension TEXT,
                size      INTEGER DEFAULT 0,
                modifiedAt TEXT,
                isFolder  INTEGER DEFAULT 0,
                parentPath TEXT DEFAULT ''
            );
            CREATE TABLE metadata (
                key TEXT PRIMARY KEY,
                value TEXT
            );
            INSERT INTO metadata (key, value) VALUES ('totalFiles', '0');
            INSERT INTO metadata (key, value) VALUES ('totalSize',  '0');
        ";
        cmd.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.CommandText = "INSERT INTO files (id, name, path, extension, size, modifiedAt, isFolder, parentPath) VALUES ($id, $n, $p, $e, $s, $m, 0, $pp);";
        int i = 1;
        foreach (var (name, path, ext, size, parent) in rows)
        {
            ins.Parameters.Clear();
            ins.Parameters.AddWithValue("$id",  i++.ToString());
            ins.Parameters.AddWithValue("$n",   name);
            ins.Parameters.AddWithValue("$p",   path);
            ins.Parameters.AddWithValue("$e",   ext);
            ins.Parameters.AddWithValue("$s",   size);
            ins.Parameters.AddWithValue("$m",   "2026-01-01T00:00:00Z");
            ins.Parameters.AddWithValue("$pp",  parent);
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Builds a minimal xCrawler-internal-style .nfindex:
    /// - Uses full_path, file_content columns (xCrawler's in-memory schema)
    /// - Has no metadata table (or metadata without search_mode)
    /// </summary>
    private static void BuildXCrawlerInternalSchema(string dbPath,
        IEnumerable<(string name, string fullPath, string ext, long size, string dir, string content)> rows)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE files (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                full_path    TEXT NOT NULL,
                name         TEXT NOT NULL,
                directory    TEXT NOT NULL,
                extension    TEXT NOT NULL,
                size_bytes   INTEGER NOT NULL,
                modified     INTEGER NOT NULL,
                is_dir       INTEGER NOT NULL,
                file_content TEXT NOT NULL DEFAULT ''
            );
        ";
        cmd.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO files (full_path, name, directory, extension, size_bytes, modified, is_dir, file_content)
                            VALUES ($p, $n, $d, $e, $s, $m, 0, $c);";
        foreach (var (name, fullPath, ext, size, dir, content) in rows)
        {
            ins.Parameters.Clear();
            ins.Parameters.AddWithValue("$p", fullPath);
            ins.Parameters.AddWithValue("$n", name);
            ins.Parameters.AddWithValue("$d", dir);
            ins.Parameters.AddWithValue("$e", ext);
            ins.Parameters.AddWithValue("$s", size);
            ins.Parameters.AddWithValue("$m", 1735689600L); // 2026-01-01 UTC
            ins.Parameters.AddWithValue("$c", content);
            ins.ExecuteNonQuery();
        }
    }
}
