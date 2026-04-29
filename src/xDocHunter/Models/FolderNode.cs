using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace xDocHunter.Models;

public sealed partial class FolderNode : ObservableObject
{
    public required string Path { get; init; }
    public required string Name { get; init; }

    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<FolderNode> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
    public string SizeDisplay => FormatSize(TotalSize);
    public string CountDisplay => FileCount.ToString("N0");

    partial void OnTotalSizeChanged(long value) => OnPropertyChanged(nameof(SizeDisplay));
    partial void OnFileCountChanged(int value) => OnPropertyChanged(nameof(CountDisplay));

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }
}
