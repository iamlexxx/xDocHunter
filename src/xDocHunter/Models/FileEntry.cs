namespace xDocHunter.Models;

public sealed class FileEntry
{
    public long Id { get; set; }
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required string Extension { get; init; }
    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public bool IsDirectory { get; init; }
    public string Content { get; init; } = string.Empty;

    public string FullPathDisplay { get; set; } = string.Empty;
    public string TypeDisplay => IsDirectory ? "Folder" : Extension;
    public string SizeDisplay => IsDirectory ? "" : FormatSize(SizeBytes);
    public string ModifiedDisplay => ModifiedUtc == default ? "" : ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}

public sealed record ScanProgress(int FilesScanned, string CurrentDirectory);
public sealed record ImportProgress(int Imported, int Total)
{
    public double Percent => Total > 0 ? (double)Imported / Total * 100 : 0;
};

public sealed class ExtensionFilter
{
    public required string Extension { get; init; }
    public int Count { get; init; }
    public bool IsSelected { get; set; }

    public bool IsFolder => Extension == Services.Database.FolderSentinel;
    public string DisplayName => IsFolder ? "Folders" : Extension;
    public string Display => $"{DisplayName}  ({Count:N0})";
}
