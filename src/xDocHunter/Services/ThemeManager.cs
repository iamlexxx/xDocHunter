using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using xDocHunter.Models;

namespace xDocHunter.Services;

public record RecentIndexFile(string Path, DateTime LastOpened)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool Exists => File.Exists(Path);
}

public static class ThemeManager
{
    private static readonly string PrefsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "xDocHunter", "prefs.json");

    private static bool _isDark = false;
    private static bool _expandTreeOnLoad = true;
    private static bool _trimPathEnabled;
    private static string _trimPathValue = string.Empty;
    private static bool _useBuiltInPdfViewer = true;
    private static SearchMode _defaultSearchMode = SearchMode.Filename;
    private static List<RecentIndexFile> _recentFiles = new();

    public static bool IsDark => _isDark;
    public static bool ExpandTreeOnLoad => _expandTreeOnLoad;
    public static bool TrimPathEnabled => _trimPathEnabled;
    public static string TrimPathValue => _trimPathValue;
    public static bool UseBuiltInPdfViewer => _useBuiltInPdfViewer;
    public static SearchMode DefaultSearchMode => _defaultSearchMode;
    public static IReadOnlyList<RecentIndexFile> RecentFiles => _recentFiles;

    public static void Initialize()
    {
        LoadPrefs();
        Apply();
    }

    public static void SetDarkMode(bool dark)
    {
        _isDark = dark;
        Apply();
        SavePrefs();
    }

    public static void Toggle()
    {
        SetDarkMode(!_isDark);
    }

    public static void SetExpandTreeOnLoad(bool expand)
    {
        _expandTreeOnLoad = expand;
        SavePrefs();
    }

    public static void SetTrimPath(bool enabled, string value)
    {
        _trimPathEnabled = enabled;
        _trimPathValue = value;
        SavePrefs();
    }

    public static void SetUseBuiltInPdfViewer(bool enabled)
    {
        _useBuiltInPdfViewer = enabled;
        SavePrefs();
    }

    public static void SetDefaultSearchMode(SearchMode mode)
    {
        _defaultSearchMode = mode;
        SavePrefs();
    }

    public static void AddRecentFile(string path)
    {
        _recentFiles.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, new RecentIndexFile(path, DateTime.Now));
        if (_recentFiles.Count > 5)
            _recentFiles.RemoveAt(_recentFiles.Count - 1);
        SavePrefs();
    }

    private static void Apply()
    {
        var uri = _isDark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var newTheme = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (merged.Count > 0)
            merged[0] = newTheme;
        else
            merged.Add(newTheme);

        foreach (Window w in Application.Current.Windows)
        {
            w.Background = (SolidColorBrush)Application.Current.Resources["BackgroundBrush"];
            w.Foreground = (SolidColorBrush)Application.Current.Resources["ForegroundBrush"];
        }
    }

    private static void LoadPrefs()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return;
            var json = File.ReadAllText(PrefsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("theme", out var t))
                _isDark = t.GetString() == "dark";
            if (doc.RootElement.TryGetProperty("expandTreeOnLoad", out var e))
                _expandTreeOnLoad = e.GetBoolean();
            if (doc.RootElement.TryGetProperty("trimPathEnabled", out var tp))
                _trimPathEnabled = tp.GetBoolean();
            if (doc.RootElement.TryGetProperty("trimPathValue", out var tv))
                _trimPathValue = tv.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("useBuiltInPdfViewer", out var pv))
                _useBuiltInPdfViewer = pv.GetBoolean();
            if (doc.RootElement.TryGetProperty("defaultSearchMode", out var dsm))
                _defaultSearchMode = dsm.GetString() == "Content" ? SearchMode.Content : SearchMode.Filename;
            if (doc.RootElement.TryGetProperty("recentFiles", out var rf) && rf.ValueKind == JsonValueKind.Array)
            {
                _recentFiles.Clear();
                foreach (var item in rf.EnumerateArray())
                {
                    var filePath = item.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                    var lastOpened = item.TryGetProperty("lastOpened", out var loEl) && loEl.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;
                    if (!string.IsNullOrEmpty(filePath))
                        _recentFiles.Add(new RecentIndexFile(filePath!, lastOpened));
                }
            }
        }
        catch { }
    }

    private static void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            var json = JsonSerializer.Serialize(new
            {
                theme = _isDark ? "dark" : "light",
                expandTreeOnLoad = _expandTreeOnLoad,
                trimPathEnabled = _trimPathEnabled,
                trimPathValue = _trimPathValue,
                useBuiltInPdfViewer = _useBuiltInPdfViewer,
                defaultSearchMode = _defaultSearchMode == SearchMode.Content ? "Content" : "Filename",
                recentFiles = _recentFiles.Select(r => new { path = r.Path, lastOpened = r.LastOpened })
            });
            File.WriteAllText(PrefsPath, json);
        }
        catch { }
    }
}
