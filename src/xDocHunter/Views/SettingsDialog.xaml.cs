using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Web.WebView2.Core;
using xDocHunter.Models;
using xDocHunter.Services;

namespace xDocHunter.Views;

public partial class SettingsDialog : Window
{
    private bool _initialized;

    public SettingsDialog()
    {
        InitializeComponent();

        bool webView2Available = IsWebView2Installed();
        PdfViewerToggle.IsEnabled = webView2Available;
        WebView2DownloadLink.Visibility = webView2Available ? Visibility.Collapsed : Visibility.Visible;

        DarkModeToggle.IsChecked = ThemeManager.IsDark;
        ExpandTreeToggle.IsChecked = ThemeManager.ExpandTreeOnLoad;
        PdfViewerToggle.IsChecked = ThemeManager.UseBuiltInPdfViewer;
        PdfMouseControlsToggle.IsChecked = ThemeManager.CustomPdfMouseControls;
        PdfMouseControlsCard.IsEnabled = ThemeManager.UseBuiltInPdfViewer && webView2Available;
        TrimPathToggle.IsChecked = ThemeManager.TrimPathEnabled;
        TrimPathInput.Text = ThemeManager.TrimPathValue;
        TrimPathInput.Visibility = ThemeManager.TrimPathEnabled
            ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeManager.DefaultSearchMode == SearchMode.Content)
            DefaultModeContentRadio.IsChecked = true;
        else
            DefaultModeFilenameRadio.IsChecked = true;
        _initialized = true;
    }

    private void DefaultModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var mode = DefaultModeContentRadio.IsChecked == true ? SearchMode.Content : SearchMode.Filename;
        ThemeManager.SetDefaultSearchMode(mode);
    }

    private void DarkModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ThemeManager.SetDarkMode(DarkModeToggle.IsChecked == true);
    }

    private void ExpandTreeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ThemeManager.SetExpandTreeOnLoad(ExpandTreeToggle.IsChecked == true);
    }

    private void PdfViewerToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var enabled = PdfViewerToggle.IsChecked == true;
        ThemeManager.SetUseBuiltInPdfViewer(enabled);
        PdfMouseControlsCard.IsEnabled = enabled;
    }

    private void PdfMouseControlsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ThemeManager.SetCustomPdfMouseControls(PdfMouseControlsToggle.IsChecked == true);
    }

    private void TrimPathToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var enabled = TrimPathToggle.IsChecked == true;
        TrimPathInput.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ThemeManager.SetTrimPath(enabled, TrimPathInput.Text);
    }

    private void TrimPathInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_initialized) return;
        ThemeManager.SetTrimPath(TrimPathToggle.IsChecked == true, TrimPathInput.Text);
    }

    private static bool IsWebView2Installed()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void WebView2Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
