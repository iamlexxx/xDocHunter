using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using xDocHunter.Models;
using xDocHunter.Services;
using xDocHunter.Views;

namespace xDocHunter.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly FileScanner _scanner = new();
    private readonly Database _db = new();
    private CancellationTokenSource? _scanCts;
    private string? _loadedNfindexPath;
    private string? _nfindexRootPath;

    [ObservableProperty] private string? _rootPath;
    [ObservableProperty] private string? _indexedRootPath;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusText = "Ready.";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _filesScanned;
    [ObservableProperty] private long _indexedCount;
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private double _scanDurationSeconds;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasNoResults = true;
    [ObservableProperty] private string _themeIcon = ThemeManager.IsDark ? "☀️" : "🌙";
    [ObservableProperty] private bool _showWelcome = true;
    [ObservableProperty] private bool _showMain;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private double _importPercent;
    [ObservableProperty] private string _importStatusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showTree = true;
    [ObservableProperty] private SearchMode _currentSearchMode = ThemeManager.DefaultSearchMode;

    public string SearchModeDisplay => CurrentSearchMode == SearchMode.Content ? "CONTENT" : "FILENAME";
    partial void OnCurrentSearchModeChanged(SearchMode value) => OnPropertyChanged(nameof(SearchModeDisplay));

    public ObservableCollection<FileEntry> Results { get; } = new();
    public ObservableCollection<FolderNode> FolderTree { get; } = new();
    public ObservableCollection<ExtensionFilter> ExtensionFilters { get; } = new();
    public ObservableCollection<RecentIndexFile> RecentFiles { get; } = new();

    public bool HasRecentFiles => RecentFiles.Count > 0;
    public bool IsIndexFromFile => _loadedNfindexPath != null;
    public string SaveButtonLabel => IsIndexFromFile ? "Update Index" : "Save Index";

    [ObservableProperty] private FolderNode? _selectedFolder;
    [ObservableProperty] private string? _filterFolderPath;
    [ObservableProperty] private string _extensionFilterDisplay = string.Empty;

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);
    public bool HasExtensionFilter => ExtensionFilters.Any(e => e.IsSelected);

    public bool HasFolderFilter => !string.IsNullOrEmpty(FilterFolderPath);
    partial void OnFilterFolderPathChanged(string? value) => OnPropertyChanged(nameof(HasFolderFilter));

    public MainViewModel()
    {
        IndexedCount = _db.Count();
        TotalSize = _db.TotalSize();
        BuildFolderTree();
        BuildExtensionFilters();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var r in ThemeManager.RecentFiles)
            RecentFiles.Add(r);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void SetLoadedNfindex(string? filePath, string? rootPath)
    {
        _loadedNfindexPath = filePath;
        _nfindexRootPath = rootPath;
        OnPropertyChanged(nameof(IsIndexFromFile));
        OnPropertyChanged(nameof(SaveButtonLabel));
    }

    public string TotalSizeDisplay => FormatSize(TotalSize);
    public string ScanDurationDisplay => ScanDurationSeconds > 0 ? $"{ScanDurationSeconds:0.0}s" : "—";
    public string RootDisplay => string.IsNullOrWhiteSpace(IndexedRootPath) ? "—" : IndexedRootPath!;

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.##} {units[u]}";
    }

    partial void OnTotalSizeChanged(long value) => OnPropertyChanged(nameof(TotalSizeDisplay));
    partial void OnScanDurationSecondsChanged(double value) => OnPropertyChanged(nameof(ScanDurationDisplay));
    partial void OnIndexedRootPathChanged(string? value) => OnPropertyChanged(nameof(RootDisplay));

    private void TransitionToMain()
    {
        ShowWelcome = false;
        ShowMain = true;
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder to scan" };
        if (dlg.ShowDialog() == true)
            RootPath = dlg.FolderName;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var folderDlg = new OpenFolderDialog { Title = "Choose a folder to scan" };
        if (folderDlg.ShowDialog() != true) return;
        RootPath = folderDlg.FolderName;

        var dialog = new ScanOptionsDialog(CurrentSearchMode) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var filter = dialog.Result;
        CurrentSearchMode = filter.Mode;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        IsBusy = true;
        FilesScanned = 0;
        ScanDurationSeconds = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var modeLabel = filter.Mode == SearchMode.Content ? "content" : "filename";
        StatusText = filter.ScanAll
            ? $"Scanning {RootPath} ({modeLabel} mode)..."
            : $"Scanning {RootPath} ({filter.AllowedExtensions.Count} extensions, {modeLabel} mode)...";

        try
        {
            SetLoadedNfindex(null, null);
            _db.ClearAll();
            var rootPath = RootPath;

            var progress = new Progress<ScanProgress>(p =>
            {
                FilesScanned = p.FilesScanned;
                var action = filter.Mode == SearchMode.Content ? "Scanning & extracting text" : "Scanning";
                StatusText = $"{action}...  {p.CurrentDirectory}";
            });

            await Task.Run(async () =>
            {
                var batch = new List<FileEntry>(capacity: 1000);
                await foreach (var entry in _scanner.ScanAsync(rootPath, filter, progress, _scanCts.Token))
                {
                    batch.Add(entry);
                    if (batch.Count >= 1000)
                    {
                        await _db.InsertBatchAsync(batch, _scanCts.Token);
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                    await _db.InsertBatchAsync(batch, _scanCts.Token);
            });

            sw.Stop();
            ScanDurationSeconds = sw.Elapsed.TotalSeconds;
            IndexedCount = _db.Count();
            TotalSize = _db.TotalSize();
            IndexedRootPath = rootPath;
            BuildFolderTree();
            BuildExtensionFilters();
            TransitionToMain();
            StatusText = $"Done. Indexed {IndexedCount:N0} items in {ScanDurationSeconds:0.0}s.";
            RefreshResults();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _scanCts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        try
        {
            _db.ClearAll();
            Results.Clear();
            FolderTree.Clear();
            ExtensionFilters.Clear();
            ExtensionFilterDisplay = string.Empty;
            FilterFolderPath = null;
            IndexedCount = 0;
            TotalSize = 0;
            ScanDurationSeconds = 0;
            IndexedRootPath = null;
            SetLoadedNfindex(null, null);
            SearchText = string.Empty;
            HasResults = false;
            HasNoResults = true;
            ShowMain = false;
            ShowWelcome = true;
            StatusText = "Index cleared.";
        }
        catch (Exception ex) { StatusText = "Clear failed: " + ex.Message; }
    }

    [RelayCommand]
    private void Refresh()
    {
        SearchText = string.Empty;
        FilterFolderPath = null;
        RefreshResults();
        StatusText = $"Refreshed. {Results.Count:N0} shown  •  {IndexedCount:N0} indexed.";
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ToggleTree() => ShowTree = !ShowTree;

    [RelayCommand]
    private async Task SaveIndexJsonAsync()
    {
        if (IndexedCount == 0) { StatusText = "Nothing to save — index is empty."; return; }

        var dlg = new SaveFileDialog
        {
            Title = "Save index as JSON",
            Filter = "Index JSON (*.nfindex.json)|*.nfindex.json|JSON (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".nfindex.json",
            FileName = (System.IO.Path.GetFileName(IndexedRootPath?.TrimEnd('\\', '/')) ?? "index") + ".nfindex.json"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusText = "Saving JSON...";
        try
        {
            await _db.SaveAsJsonAsync(dlg.FileName, IndexedRootPath, ScanDurationSeconds);
            StatusText = $"Saved {IndexedCount:N0} items to {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { StatusText = "Save failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveIndexSqliteAsync()
    {
        if (IndexedCount == 0) { StatusText = "Nothing to save — index is empty."; return; }

        var dlg = new SaveFileDialog
        {
            Title = "Save index as SQLite (.nfindex)",
            Filter = "Name Finder Index (*.nfindex)|*.nfindex|SQLite (*.db;*.sqlite)|*.db;*.sqlite|All Files (*.*)|*.*",
            DefaultExt = ".nfindex",
            FileName = (System.IO.Path.GetFileName(IndexedRootPath?.TrimEnd('\\', '/')) ?? "index") + ".nfindex"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusText = "Saving SQLite...";
        try
        {
            await _db.SaveAsSqliteAsync(dlg.FileName, IndexedRootPath, ScanDurationSeconds, CurrentSearchMode);
            StatusText = $"Saved {IndexedCount:N0} items to {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { StatusText = "Save failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UpdateIndexAsync()
    {
        if (string.IsNullOrEmpty(_loadedNfindexPath) || string.IsNullOrEmpty(_nfindexRootPath))
        {
            StatusText = "No index loaded to update.";
            return;
        }

        if (!Directory.Exists(_nfindexRootPath))
        {
            StatusText = $"Root path no longer exists: {_nfindexRootPath}";
            return;
        }

        var optsDlg = new ScanOptionsDialog(CurrentSearchMode)
        {
            Owner = Application.Current.MainWindow
        };
        if (optsDlg.ShowDialog() != true || optsDlg.Result is null) return;
        var updateFilter = optsDlg.Result;
        CurrentSearchMode = updateFilter.Mode;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        IsBusy = true;
        FilesScanned = 0;
        ScanDurationSeconds = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rootPath = _nfindexRootPath;
        var loadedPath = _loadedNfindexPath;
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modeLabel = updateFilter.Mode == SearchMode.Content ? "content" : "filename";
        StatusText = $"Updating index for {rootPath}...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                FilesScanned = p.FilesScanned;
                var action = updateFilter.Mode == SearchMode.Content ? "Scanning & extracting text" : "Scanning";
                StatusText = $"{action}...  {p.CurrentDirectory}";
            });
            await Task.Run(async () =>
            {
                var batch = new List<FileEntry>(1000);
                await foreach (var entry in _scanner.ScanAsync(rootPath, updateFilter, progress, _scanCts.Token))
                {
                    scannedPaths.Add(entry.FullPath);
                    batch.Add(entry);
                    if (batch.Count >= 1000)
                    {
                        await _db.InsertBatchAsync(batch, _scanCts.Token);
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                    await _db.InsertBatchAsync(batch, _scanCts.Token);
            });

            await Task.Run(() => _db.DeletePathsNotIn(scannedPaths));

            sw.Stop();
            ScanDurationSeconds = sw.Elapsed.TotalSeconds;
            IndexedCount = _db.Count();
            TotalSize = _db.TotalSize();
            BuildFolderTree();
            BuildExtensionFilters();

            StatusText = "Saving updated index...";
            await _db.SaveAsSqliteAsync(loadedPath, rootPath, ScanDurationSeconds, CurrentSearchMode);

            StatusText = $"Updated. {IndexedCount:N0} items in {ScanDurationSeconds:0.0}s. ({modeLabel} mode)";
            RefreshResults();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Update failed: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    private void FindDuplicates() => StatusText = "Find Duplicates — coming soon.";

    [RelayCommand]
    private void FileStats() => StatusText = "File Stats — coming soon.";

    [RelayCommand]
    private void OpenSettings()
    {
        var dlg = new SettingsDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        ThemeIcon = ThemeManager.IsDark ? "☀️" : "🌙";
        RefreshResults();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        new AboutDialog { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private async Task ImportNfindexAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import .nfindex file",
            Filter = "Name Finder Index (*.nfindex)|*.nfindex|All Files (*.*)|*.*",
            DefaultExt = ".nfindex"
        };
        if (dlg.ShowDialog() != true) return;
        await DoImportAsync(dlg.FileName);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentIndexFile item)
    {
        if (!File.Exists(item.Path))
        {
            StatusText = $"File not found: {item.Path}";
            return;
        }
        await DoImportAsync(item.Path);
    }

    private async Task DoImportAsync(string filePath)
    {
        _scanCts = new CancellationTokenSource();
        IsImporting = true;
        IsBusy = true;
        ImportPercent = 0;
        var fileName = System.IO.Path.GetFileName(filePath);

        var (totalFiles, _) = _db.GetNfindexInfo(filePath);
        ImportStatusText = $"Importing {fileName}  •  0 / {totalFiles:N0}";
        StatusText = ImportStatusText;

        try
        {
            _db.ClearAll();

            var progress = new Progress<ImportProgress>(p =>
            {
                ImportPercent = p.Percent;
                ImportStatusText = $"Importing {fileName}  •  {p.Imported:N0} / {p.Total:N0}";
                StatusText = ImportStatusText;
            });

            var (count, mode) = await _db.ImportNfindexAsync(filePath, progress, _scanCts.Token);
            CurrentSearchMode = mode;
            IndexedCount = _db.Count();
            TotalSize = _db.TotalSize();
            IndexedRootPath = fileName;
            SetLoadedNfindex(filePath, _db.GetNfindexRootPath(filePath));
            BuildFolderTree();
            BuildExtensionFilters();
            ThemeManager.AddRecentFile(filePath);
            RefreshRecentFiles();
            TransitionToMain();
            ImportPercent = 100;
            var modeLabel = mode == SearchMode.Content ? "content" : "filename";
            StatusText = $"Imported {count:N0} items from {fileName} ({modeLabel} mode)";
            RefreshResults();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Import failed: " + ex.Message;
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    private void Search() => RefreshResults();

    [RelayCommand]
    private void OpenFile(FileEntry? entry)
    {
        if (entry is null) return;

        // Open PDFs in the built-in read-only viewer when enabled.
        if (!entry.IsDirectory
            && ThemeManager.UseBuiltInPdfViewer
            && string.Equals(entry.Extension, ".pdf", StringComparison.OrdinalIgnoreCase)
            && File.Exists(entry.FullPath))
        {
            try
            {
                var viewer = new PdfViewerWindow(entry.FullPath)
                {
                    Owner = Application.Current.MainWindow
                };
                viewer.Show();
                return;
            }
            catch (Exception ex)
            {
                StatusText = "Built-in PDF viewer failed, opening externally: " + ex.Message;
                // Fall through to shell execute below.
            }
        }

        try
        {
            var psi = new ProcessStartInfo(entry.FullPath) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex) { StatusText = "Open failed: " + ex.Message; }
    }

    [RelayCommand]
    private void RevealInExplorer(FileEntry? entry)
    {
        if (entry is null) return;
        try
        {
            if (entry.IsDirectory && Directory.Exists(entry.FullPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.FullPath}\"") { UseShellExecute = true });
            else if (File.Exists(entry.FullPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "Reveal failed: " + ex.Message; }
    }

    [RelayCommand]
    private void CopyPath(FileEntry? entry)
    {
        if (entry is null) return;
        Clipboard.SetText(entry.FullPath);
        StatusText = "Path copied: " + entry.FullPath;
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        RefreshResults();
    }

    partial void OnIsScanningChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
    partial void OnRootPathChanged(string? value) => ScanCommand.NotifyCanExecuteChanged();

    private void RefreshResults()
    {
        var selectedExts = ExtensionFilters.Where(e => e.IsSelected).Select(e => e.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = _db.Search(SearchText, FilterFolderPath, selectedExts.Count > 0 ? selectedExts : null, limit: 5000,
            mode: CurrentSearchMode);

        var trimEnabled = ThemeManager.TrimPathEnabled;
        var trimValue = ThemeManager.TrimPathValue;

        Results.Clear();
        foreach (var item in list)
        {
            item.FullPathDisplay = trimEnabled && !string.IsNullOrEmpty(trimValue)
                ? item.FullPath.Replace(trimValue, "", StringComparison.OrdinalIgnoreCase)
                : item.FullPath;
            Results.Add(item);
        }
        HasResults = Results.Count > 0;
        HasNoResults = Results.Count == 0;
        if (!IsScanning)
            StatusText = $"{Results.Count:N0} shown  •  {IndexedCount:N0} indexed";
    }

    [RelayCommand]
    private void SelectFolder(FolderNode? node)
    {
        if (node is null)
        {
            FilterFolderPath = null;
        }
        else
        {
            FilterFolderPath = node.Path;
            node.IsSelected = true;
        }
        RefreshResults();
    }

    [RelayCommand]
    private void ClearFolderFilter()
    {
        FilterFolderPath = null;
        if (SelectedFolder is { } sf) sf.IsSelected = false;
        SelectedFolder = null;
        RefreshResults();
    }

    [RelayCommand]
    private void ApplyExtensionFilter()
    {
        OnPropertyChanged(nameof(HasExtensionFilter));
        var selected = ExtensionFilters.Where(e => e.IsSelected).ToList();
        ExtensionFilterDisplay = selected.Count == 0
            ? string.Empty
            : string.Join(", ", selected.Take(5).Select(e => e.DisplayName)) + (selected.Count > 5 ? $" +{selected.Count - 5}" : "");
        RefreshResults();
    }

    [RelayCommand]
    private void ClearExtensionFilter()
    {
        foreach (var ef in ExtensionFilters) ef.IsSelected = false;
        ExtensionFilterDisplay = string.Empty;
        OnPropertyChanged(nameof(HasExtensionFilter));
        RefreshResults();
    }

    private void BuildExtensionFilters()
    {
        ExtensionFilters.Clear();
        foreach (var (ext, count) in _db.GetDistinctExtensions())
            ExtensionFilters.Add(new ExtensionFilter { Extension = ext, Count = count });
        ExtensionFilterDisplay = string.Empty;
        OnPropertyChanged(nameof(HasExtensionFilter));
    }

    private void BuildFolderTree()
    {
        FolderTree.Clear();
        var aggregates = _db.GetDirectoryAggregates();
        if (aggregates.Count == 0) return;

        var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        var sep = System.IO.Path.DirectorySeparatorChar;

        FolderNode GetOrCreate(string path)
        {
            if (nodes.TryGetValue(path, out var n)) return n;
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path; // drive root like "C:\"
            n = new FolderNode { Path = path, Name = name };
            nodes[path] = n;
            return n;
        }

        foreach (var (dir, fileCount, totalSize) in aggregates)
        {
            var node = GetOrCreate(dir);
            node.FileCount += fileCount;
            node.TotalSize += totalSize;

            // Walk up to the root, linking parents.
            var current = dir;
            while (true)
            {
                var parent = System.IO.Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                var parentNode = GetOrCreate(parent);
                var childNode = nodes[current];
                if (!parentNode.Children.Contains(childNode))
                    parentNode.Children.Add(childNode);
                current = parent;
            }
        }

        // Aggregate counts/sizes from descendants up to ancestors.
        var roots = nodes.Values
            .Where(n => !nodes.Values.Any(p => p.Children.Contains(n)))
            .ToList();

        foreach (var root in roots)
        {
            Aggregate(root);
            SortRecursive(root);
            if (ThemeManager.ExpandTreeOnLoad)
                ExpandAll(root);
            else
                root.IsExpanded = true;
        }

        // If there is exactly one root with one child, auto-expand the child too.
        roots.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        foreach (var r in roots) FolderTree.Add(r);
    }

    private static (int count, long size) Aggregate(FolderNode node)
    {
        int count = node.FileCount;
        long size = node.TotalSize;
        foreach (var child in node.Children)
        {
            var (cCount, cSize) = Aggregate(child);
            count += cCount;
            size += cSize;
        }
        node.FileCount = count;
        node.TotalSize = size;
        return (count, size);
    }

    private static void SortRecursive(FolderNode node)
    {
        var sorted = node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var c in sorted)
        {
            node.Children.Add(c);
            SortRecursive(c);
        }
    }

    private static void ExpandAll(FolderNode node)
    {
        node.IsExpanded = true;
        foreach (var c in node.Children) ExpandAll(c);
    }

    public void Dispose() => _db.Dispose();
}
