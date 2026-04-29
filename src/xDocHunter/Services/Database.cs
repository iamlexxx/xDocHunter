using System.IO;
using Microsoft.Data.Sqlite;
using xDocHunter.Models;

namespace xDocHunter.Services;

public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public Database(string? dbPath = null)
    {
        dbPath ??= DefaultDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _conn = new SqliteConnection($"Data Source={dbPath};Cache=Shared;Pooling=True");
        _conn.Open();

        using var pragma = _conn.CreateCommand();
        pragma.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 268435456;
        ";
        pragma.ExecuteNonQuery();

        InitSchema();
        MigrateSchema();
    }

    public static string DefaultDbPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "xDocHunter", "index.db");

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS files (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                full_path    TEXT    NOT NULL UNIQUE,
                name         TEXT    NOT NULL,
                directory    TEXT    NOT NULL,
                extension    TEXT    NOT NULL,
                size_bytes   INTEGER NOT NULL,
                modified     INTEGER NOT NULL,
                is_dir       INTEGER NOT NULL,
                file_content TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS idx_files_name      ON files(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_files_extension ON files(extension);
            CREATE INDEX IF NOT EXISTS idx_files_directory ON files(directory);

            CREATE VIRTUAL TABLE IF NOT EXISTS files_fts
                USING fts5(name, file_content, content='files', content_rowid='id', tokenize='unicode61');

            CREATE TRIGGER IF NOT EXISTS files_ai AFTER INSERT ON files BEGIN
                INSERT INTO files_fts(rowid, name, file_content) VALUES (new.id, new.name, new.file_content);
            END;
            CREATE TRIGGER IF NOT EXISTS files_ad AFTER DELETE ON files BEGIN
                INSERT INTO files_fts(files_fts, rowid, name, file_content) VALUES('delete', old.id, old.name, old.file_content);
            END;
            CREATE TRIGGER IF NOT EXISTS files_au AFTER UPDATE ON files BEGIN
                INSERT INTO files_fts(files_fts, rowid, name, file_content) VALUES('delete', old.id, old.name, old.file_content);
                INSERT INTO files_fts(rowid, name, file_content) VALUES (new.id, new.name, new.file_content);
            END;
        ";
        cmd.ExecuteNonQuery();
    }

    private void MigrateSchema()
    {
        using var checkCmd = _conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('files') WHERE name='file_content'";
        var exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
        if (exists) return;

        using var alterCmd = _conn.CreateCommand();
        alterCmd.CommandText = "ALTER TABLE files ADD COLUMN file_content TEXT NOT NULL DEFAULT ''";
        alterCmd.ExecuteNonQuery();

        RecreateFullTextIndex();

        using var rebuildCmd = _conn.CreateCommand();
        rebuildCmd.CommandText = "INSERT INTO files_fts(files_fts) VALUES('rebuild')";
        rebuildCmd.ExecuteNonQuery();
    }

    private void RecreateFullTextIndex()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            DROP TRIGGER IF EXISTS files_ai;
            DROP TRIGGER IF EXISTS files_ad;
            DROP TRIGGER IF EXISTS files_au;
            DROP TABLE IF EXISTS files_fts;

            CREATE VIRTUAL TABLE files_fts
                USING fts5(name, file_content, content='files', content_rowid='id', tokenize='unicode61');

            CREATE TRIGGER files_ai AFTER INSERT ON files BEGIN
                INSERT INTO files_fts(rowid, name, file_content) VALUES (new.id, new.name, new.file_content);
            END;
            CREATE TRIGGER files_ad AFTER DELETE ON files BEGIN
                INSERT INTO files_fts(files_fts, rowid, name, file_content) VALUES('delete', old.id, old.name, old.file_content);
            END;
            CREATE TRIGGER files_au AFTER UPDATE ON files BEGIN
                INSERT INTO files_fts(files_fts, rowid, name, file_content) VALUES('delete', old.id, old.name, old.file_content);
                INSERT INTO files_fts(rowid, name, file_content) VALUES (new.id, new.name, new.file_content);
            END;
        ";
        cmd.ExecuteNonQuery();
    }

    public void ClearAll()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files;";
        cmd.ExecuteNonQuery();
    }

    public async Task InsertBatchAsync(IEnumerable<FileEntry> entries, CancellationToken ct = default)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO files (full_path, name, directory, extension, size_bytes, modified, is_dir, file_content)
            VALUES ($p, $n, $d, $e, $s, $m, $i, $c)
            ON CONFLICT(full_path) DO UPDATE SET
                name=excluded.name,
                directory=excluded.directory,
                extension=excluded.extension,
                size_bytes=excluded.size_bytes,
                modified=excluded.modified,
                is_dir=excluded.is_dir,
                file_content=excluded.file_content;
        ";
        var pPath    = cmd.Parameters.Add("$p", SqliteType.Text);
        var pName    = cmd.Parameters.Add("$n", SqliteType.Text);
        var pDir     = cmd.Parameters.Add("$d", SqliteType.Text);
        var pExt     = cmd.Parameters.Add("$e", SqliteType.Text);
        var pSize    = cmd.Parameters.Add("$s", SqliteType.Integer);
        var pMod     = cmd.Parameters.Add("$m", SqliteType.Integer);
        var pDirFlag = cmd.Parameters.Add("$i", SqliteType.Integer);
        var pContent = cmd.Parameters.Add("$c", SqliteType.Text);

        foreach (var f in entries)
        {
            ct.ThrowIfCancellationRequested();
            pPath.Value    = f.FullPath;
            pName.Value    = f.Name;
            pDir.Value     = f.Directory;
            pExt.Value     = f.Extension;
            pSize.Value    = f.SizeBytes;
            pMod.Value     = new DateTimeOffset(f.ModifiedUtc == default ? DateTime.UnixEpoch : f.ModifiedUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            pDirFlag.Value = f.IsDirectory ? 1 : 0;
            pContent.Value = f.Content;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        await Task.CompletedTask;
    }

    public List<FileEntry> Search(string? query, string? folderPath = null,
        HashSet<string>? extensions = null, int limit = 5000, bool includeFilename = true,
        SearchMode mode = SearchMode.Content)
    {
        var results = new List<FileEntry>();
        using var cmd = _conn.CreateCommand();

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasFolder = !string.IsNullOrWhiteSpace(folderPath);
        var hasExtFilter = extensions is { Count: > 0 };

        var sb = new System.Text.StringBuilder();

        if (hasQuery && mode == SearchMode.Content)
        {
            // FTS5 MATCH with prefix-match per token.
            var tokens = query!.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var terms = string.Join(" ", tokens.Select(t => $"\"{t.Replace("\"", "")}\"*"));
            var matchStr = includeFilename ? terms : $"{{file_content}}: {terms}";
            sb.Append(@"
                SELECT f.id, f.full_path, f.name, f.directory, f.extension, f.size_bytes, f.modified, f.is_dir
                FROM files f
                JOIN files_fts ON files_fts.rowid = f.id
                WHERE files_fts MATCH $match");
            cmd.Parameters.AddWithValue("$match", matchStr);
        }
        else if (hasQuery && mode == SearchMode.Filename)
        {
            // LIKE-based search on name only — matching full_path would surface files whose
            // parent folder matches the query, which is unexpected in Filename mode.
            var tokens = query!.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            sb.Append("SELECT id, full_path, name, directory, extension, size_bytes, modified, is_dir FROM files WHERE 1=1");
            for (int ti = 0; ti < tokens.Count(); ti++)
            {
                var pn = $"$fn{ti}";
                sb.Append($" AND name LIKE {pn} ESCAPE '|'");
                cmd.Parameters.AddWithValue(pn, "%" + EscapeLike(tokens[ti]) + "%");
            }
        }
        else
        {
            sb.Append(@"
                SELECT id, full_path, name, directory, extension, size_bytes, modified, is_dir
                FROM files WHERE 1=1");
        }

        if (hasFolder)
        {
            var useAlias = hasQuery && mode == SearchMode.Content;
            var col = useAlias ? "f.directory" : "directory";
            sb.Append($" AND ({col} = $fd OR {col} LIKE $fdp ESCAPE '|')");
            var f = folderPath!.TrimEnd('\\', '/');
            cmd.Parameters.AddWithValue("$fd", f);
            cmd.Parameters.AddWithValue("$fdp", EscapeLike(f) + System.IO.Path.DirectorySeparatorChar + "%");
        }
        if (hasExtFilter)
        {
            bool includeFolders = extensions!.Remove(FolderSentinel);
            var extParams = new List<string>();
            int ei = 0;
            foreach (var ext in extensions)
            {
                var p = "$ext" + ei++;
                extParams.Add(p);
                cmd.Parameters.AddWithValue(p, ext);
            }
            var useAlias  = hasQuery && mode == SearchMode.Content;
            var extCol   = useAlias ? "f.extension" : "extension";
            var isDirCol = useAlias ? "f.is_dir"    : "is_dir";

            if (extParams.Count > 0 && includeFolders)
                sb.Append($" AND ({extCol} IN ({string.Join(',', extParams)}) OR {isDirCol} = 1)");
            else if (extParams.Count > 0)
                sb.Append($" AND {extCol} IN ({string.Join(',', extParams)})");
            else if (includeFolders)
                sb.Append($" AND {isDirCol} = 1");
        }

        var orderCol = (hasQuery && mode == SearchMode.Content) ? "f.name" : "name";
        sb.Append($" ORDER BY {orderCol} COLLATE NOCASE LIMIT $lim;");
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$lim", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new FileEntry
            {
                Id          = r.GetInt64(0),
                FullPath    = r.GetString(1),
                Name        = r.GetString(2),
                Directory   = r.GetString(3),
                Extension   = r.GetString(4),
                SizeBytes   = r.GetInt64(5),
                ModifiedUtc = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)).UtcDateTime,
                IsDirectory = r.GetInt32(7) == 1
            });
        }
        return results;
    }

    public long Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files;";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public long TotalSize()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(size_bytes), 0) FROM files WHERE is_dir = 0;";
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value || v is null ? 0L : Convert.ToInt64(v);
    }

    public List<(string Directory, int FileCount, long TotalSize)> GetDirectoryAggregates()
    {
        var list = new List<(string, int, long)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT directory, COUNT(*), COALESCE(SUM(size_bytes), 0)
            FROM files
            WHERE is_dir = 0
            GROUP BY directory;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetInt32(1), r.GetInt64(2)));
        return list;
    }

    public const string FolderSentinel = "<folder>";

    public List<(string Extension, int Count)> GetDistinctExtensions()
    {
        var list = new List<(string, int)>();
        using var cmd = _conn.CreateCommand();

        // Folder count first
        cmd.CommandText = "SELECT COUNT(*) FROM files WHERE is_dir = 1;";
        var folderCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        if (folderCount > 0)
            list.Add((FolderSentinel, folderCount));

        // File extensions
        cmd.CommandText = @"
            SELECT extension, COUNT(*) as cnt
            FROM files
            WHERE is_dir = 0 AND extension != ''
            GROUP BY extension
            ORDER BY cnt DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetInt32(1)));
        return list;
    }

    private static string EscapeLike(string s) =>
        s.Replace("|", "||").Replace("%", "|%").Replace("_", "|_");

    public string? GetNfindexRootPath(string nfindexPath)
    {
        try
        {
            using var src = new SqliteConnection($"Data Source={nfindexPath};Mode=ReadOnly");
            src.Open();
            using var cmd = src.CreateCommand();
            cmd.CommandText = "SELECT value FROM metadata WHERE key='rootPath';";
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    public void DeletePathsNotIn(HashSet<string> knownPaths)
    {
        var toDelete = new List<string>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT full_path FROM files;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = r.GetString(0);
                if (!knownPaths.Contains(p))
                    toDelete.Add(p);
            }
        }

        if (toDelete.Count == 0) return;

        using var tx = _conn.BeginTransaction();
        using var del = _conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM files WHERE full_path = $p;";
        var param = del.Parameters.Add("$p", SqliteType.Text);
        foreach (var path in toDelete)
        {
            param.Value = path;
            del.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public (int totalFiles, long totalSize) GetNfindexInfo(string nfindexPath)
    {
        using var src = new SqliteConnection($"Data Source={nfindexPath};Mode=ReadOnly");
        src.Open();

        int totalFiles = 0;
        long totalSize = 0;

        using var countCmd = src.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM files;";
        totalFiles = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        try
        {
            using var metaCmd = src.CreateCommand();
            metaCmd.CommandText = "SELECT value FROM metadata WHERE key='totalSize';";
            var sizeStr = metaCmd.ExecuteScalar() as string;
            if (long.TryParse(sizeStr, out var s)) totalSize = s;
        }
        catch { /* metadata table absent in some legacy schemas */ }

        return (totalFiles, totalSize);
    }

    public async Task<(int Count, SearchMode Mode)> ImportNfindexAsync(string nfindexPath,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var (expectedTotal, _) = GetNfindexInfo(nfindexPath);

        return await Task.Run(() =>
        {
            using var src = new SqliteConnection($"Data Source={nfindexPath};Mode=ReadOnly");
            src.Open();

            // Detect schema variant
            var hasFileContent = HasColumn(src, "files", "file_content");
            var hasFullPath    = HasColumn(src, "files", "full_path");

            // Try to read search_mode from metadata (xDocHunter format)
            SearchMode detectedMode = SearchMode.Filename;
            string? metaMode = null;
            try
            {
                using var metaCmd = src.CreateCommand();
                metaCmd.CommandText = "SELECT value FROM metadata WHERE key='search_mode';";
                metaMode = metaCmd.ExecuteScalar() as string;
            }
            catch { /* no metadata table */ }

            if (!string.IsNullOrEmpty(metaMode))
                detectedMode = metaMode == "Content" ? SearchMode.Content : SearchMode.Filename;
            else if (hasFileContent)
                detectedMode = SearchMode.Content;

            // Build the read command for the detected schema
            using var readCmd = src.CreateCommand();
            if (hasFullPath)
            {
                // internal schema (name, full_path, directory, extension, size_bytes, modified, is_dir, file_content?)
                readCmd.CommandText = hasFileContent
                    ? "SELECT name, full_path, extension, size_bytes, modified, is_dir, directory, file_content FROM files;"
                    : "SELECT name, full_path, extension, size_bytes, modified, is_dir, directory FROM files;";
            }
            else
            {
                // xDocHunter export schema (name, path, extension, size, modifiedAt, isFolder, parentPath, file_content?)
                readCmd.CommandText = hasFileContent
                    ? "SELECT name, path, extension, size, modifiedAt, isFolder, parentPath, file_content FROM files;"
                    : "SELECT name, path, extension, size, modifiedAt, isFolder, parentPath FROM files;";
            }

            var batch = new List<FileEntry>(1000);
            int total = 0;

            using var reader = readCmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();

                var name = reader.GetString(0);
                var fullPath = reader.GetString(1);
                var ext = reader.IsDBNull(2) ? "" : reader.GetString(2);
                long size = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);

                DateTime mod = default;
                if (hasFullPath)
                {
                    // modified is Unix epoch INTEGER
                    if (!reader.IsDBNull(4))
                        mod = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)).UtcDateTime;
                }
                else
                {
                    // modifiedAt is ISO TEXT
                    var modStr = reader.IsDBNull(4) ? "" : reader.GetString(4);
                    if (DateTime.TryParse(modStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                        mod = parsed.ToUniversalTime();
                }

                var isDir = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                var directory = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var content = hasFileContent && !reader.IsDBNull(7) ? reader.GetString(7) : string.Empty;

                batch.Add(new FileEntry
                {
                    FullPath = fullPath,
                    Name = name,
                    Directory = directory,
                    Extension = ext ?? "",
                    SizeBytes = size,
                    ModifiedUtc = mod,
                    IsDirectory = isDir == 1,
                    Content = content
                });

                if (batch.Count >= 1000)
                {
                    InsertBatchAsync(batch).GetAwaiter().GetResult();
                    total += batch.Count;
                    batch.Clear();
                    progress?.Report(new ImportProgress(total, expectedTotal));
                }
            }

            if (batch.Count > 0)
            {
                InsertBatchAsync(batch).GetAwaiter().GetResult();
                total += batch.Count;
                progress?.Report(new ImportProgress(total, expectedTotal));
            }

            return (total, detectedMode);
        }, ct);
    }

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info($t) WHERE name=$c;";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$c", column);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    public SearchMode? GetNfindexSearchMode(string nfindexPath)
    {
        try
        {
            using var src = new SqliteConnection($"Data Source={nfindexPath};Mode=ReadOnly");
            src.Open();
            using var cmd = src.CreateCommand();
            cmd.CommandText = "SELECT value FROM metadata WHERE key='search_mode';";
            var v = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(v)) return null;
            return v == "Content" ? SearchMode.Content : SearchMode.Filename;
        }
        catch { return null; }
    }

    public async Task SaveAsSqliteAsync(string outPath, string? rootPath,
        double scanDurationSeconds, SearchMode mode = SearchMode.Filename, CancellationToken ct = default)
    {
        var tempPath = outPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        await Task.Run(() =>
        {
            using var dst = new SqliteConnection($"Data Source={tempPath}");
            dst.Open();

            using (var schema = dst.CreateCommand())
            {
                schema.CommandText = @"
                    CREATE TABLE files (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        path TEXT NOT NULL,
                        extension TEXT,
                        size INTEGER DEFAULT 0,
                        modifiedAt TEXT,
                        isFolder INTEGER DEFAULT 0,
                        mimeType TEXT DEFAULT '',
                        depth INTEGER DEFAULT 0,
                        parentPath TEXT DEFAULT '',
                        file_content TEXT DEFAULT ''
                    );
                    CREATE TABLE metadata (
                        key TEXT PRIMARY KEY,
                        value TEXT
                    );
                    CREATE INDEX idx_files_name ON files(name);
                    CREATE INDEX idx_files_extension ON files(extension);
                    CREATE INDEX idx_files_isFolder ON files(isFolder);";
                schema.ExecuteNonQuery();
            }

            using var tx = dst.BeginTransaction();

            using (var meta = dst.CreateCommand())
            {
                meta.Transaction = tx;
                meta.CommandText = "INSERT INTO metadata (key, value) VALUES ($k, $v);";
                var pK = meta.Parameters.Add("$k", SqliteType.Text);
                var pV = meta.Parameters.Add("$v", SqliteType.Text);

                void Put(string k, string v) { pK.Value = k; pV.Value = v; meta.ExecuteNonQuery(); }
                Put("version", "1.0");
                Put("createdAt", DateTime.UtcNow.ToString("o"));
                Put("totalFiles", Count().ToString());
                Put("totalSize", TotalSize().ToString());
                Put("scanDuration", scanDurationSeconds.ToString("0.###"));
                Put("rootPath", rootPath ?? "Unknown");
                Put("search_mode", mode == SearchMode.Content ? "Content" : "Filename");
            }

            using var insert = dst.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
                INSERT INTO files (id, name, path, extension, size, modifiedAt, isFolder, mimeType, depth, parentPath, file_content)
                VALUES ($id, $n, $p, $e, $s, $m, $i, '', $d, $pp, $c);";
            var aId = insert.Parameters.Add("$id", SqliteType.Text);
            var aN  = insert.Parameters.Add("$n",  SqliteType.Text);
            var aP  = insert.Parameters.Add("$p",  SqliteType.Text);
            var aE  = insert.Parameters.Add("$e",  SqliteType.Text);
            var aS  = insert.Parameters.Add("$s",  SqliteType.Integer);
            var aM  = insert.Parameters.Add("$m",  SqliteType.Text);
            var aI  = insert.Parameters.Add("$i",  SqliteType.Integer);
            var aD  = insert.Parameters.Add("$d",  SqliteType.Integer);
            var aPP = insert.Parameters.Add("$pp", SqliteType.Text);
            var aC  = insert.Parameters.Add("$c",  SqliteType.Text);

            using var read = _conn.CreateCommand();
            read.CommandText = @"
                SELECT id, full_path, name, directory, extension, size_bytes, modified, is_dir, file_content
                FROM files;";
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = r.GetString(1);
                aId.Value = r.GetInt64(0).ToString();
                aN.Value  = r.GetString(2);
                aP.Value  = fullPath;
                aE.Value  = r.GetString(4);
                aS.Value  = r.GetInt64(5);
                aM.Value  = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)).UtcDateTime.ToString("o");
                aI.Value  = r.GetInt32(7);
                aD.Value  = fullPath.Count(c => c == '\\' || c == '/');
                aPP.Value = r.GetString(3);
                aC.Value  = r.IsDBNull(8) ? string.Empty : r.GetString(8);
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }, ct);

        SqliteConnection.ClearAllPools();
        File.Move(tempPath, outPath, overwrite: true);
    }

    public async Task SaveAsJsonAsync(string outPath, string? rootPath,
        double scanDurationSeconds, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var fs = File.Create(outPath);
            using var w = new System.Text.Json.Utf8JsonWriter(fs,
                new System.Text.Json.JsonWriterOptions { Indented = true });

            w.WriteStartObject();
            w.WriteString("version", "1.0");
            w.WriteString("createdAt", DateTime.UtcNow.ToString("o"));

            w.WritePropertyName("stats");
            w.WriteStartObject();
            w.WriteNumber("totalFiles", Count());
            w.WriteNumber("totalSize", TotalSize());
            w.WriteString("scanDuration", scanDurationSeconds.ToString("0.###"));
            w.WriteString("rootPath", rootPath ?? "Unknown");
            w.WriteEndObject();

            w.WritePropertyName("files");
            w.WriteStartArray();

            using var read = _conn.CreateCommand();
            read.CommandText = @"
                SELECT id, full_path, name, directory, extension, size_bytes, modified, is_dir
                FROM files;";
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                w.WriteStartObject();
                w.WriteString("id", r.GetInt64(0).ToString());
                w.WriteString("name", r.GetString(2));
                w.WriteString("path", r.GetString(1));
                w.WriteString("extension", r.GetString(4));
                w.WriteNumber("size", r.GetInt64(5));
                w.WriteString("modifiedAt",
                    DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)).UtcDateTime.ToString("o"));
                w.WriteNumber("isFolder", r.GetInt32(7));
                w.WriteString("parentPath", r.GetString(3));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.Flush();
        }, ct);
    }

    public void Dispose() => _conn.Dispose();
}
