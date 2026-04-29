using System.Windows;
using System.Windows.Input;
using xDocHunter.Models;

namespace xDocHunter.Views;

public partial class ScanOptionsDialog : Window
{
    private readonly List<FileTypePreset> _presets;

    public ScanFilterOptions? Result { get; private set; }

    public ScanOptionsDialog() : this(SearchMode.Filename) { }

    public ScanOptionsDialog(SearchMode initialMode)
    {
        InitializeComponent();
        _presets = FileTypePreset.Defaults();
        PresetsPanel.ItemsSource = _presets;

        if (initialMode == SearchMode.Content) ModeContentRadio.IsChecked = true;
        else ModeFilenameRadio.IsChecked = true;

        UpdateFilterCount();
    }

    private SearchMode CurrentMode =>
        ModeContentRadio.IsChecked == true ? SearchMode.Content : SearchMode.Filename;

    private void Preset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileTypePreset preset)
        {
            preset.IsSelected = !preset.IsSelected;
            PresetsPanel.ItemsSource = null;
            PresetsPanel.ItemsSource = _presets;
            UpdateFilterCount();
        }
    }

    private void ModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        // No-op: all presets remain selectable in both modes.
        // In Content mode, files from non-extractable presets are still indexed (filename only).
    }

    private void UpdateFilterCount()
    {
        int active = _presets.Count(p => p.IsSelected);
        var custom = ParseCustomExtensions();
        int total = active + (custom.Count > 0 ? 1 : 0);
        FilterCountText.Text = total > 0 ? $"{total} filter{(total > 1 ? "s" : "")} active" : "No filters";
        StartScanButton.IsEnabled = total > 0;
    }

    private HashSet<string> ParseCustomExtensions()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = CustomExtensionsBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var raw in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = raw.Trim();
            if (!ext.StartsWith('.')) ext = "." + ext;
            result.Add(ext);
        }
        return result;
    }

    private ScanFilterOptions BuildResult(bool scanAll)
    {
        var opts = new ScanFilterOptions { Mode = CurrentMode };
        if (scanAll) return opts;

        foreach (var preset in _presets.Where(p => p.IsSelected))
            foreach (var ext in preset.Extensions)
                opts.AllowedExtensions.Add(ext);

        foreach (var ext in ParseCustomExtensions())
            opts.AllowedExtensions.Add(ext);

        return opts;
    }

    private void CustomExtensionsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateFilterCount();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void ScanAll_Click(object sender, RoutedEventArgs e)
    {
        Result = BuildResult(scanAll: true);
        DialogResult = true;
    }

    private void StartScan_Click(object sender, RoutedEventArgs e)
    {
        Result = BuildResult(scanAll: false);
        DialogResult = true;
    }
}
