using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace xDocHunter.Views;

public partial class PdfViewerWindow : Window
{
    private static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "xDocHunter", "WebView2");

    private readonly string _filePath;

    public PdfViewerWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;

        var name = Path.GetFileName(filePath);
        Title = $"{name} — xDocHunter (read-only)";
        FileNameText.Text = name;
        FilePathText.Text = filePath;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder);
            await Web.EnsureCoreWebView2Async(env);

            // Disable drag-and-drop navigation (keeps viewer locked to this one file).
            var settings = Web.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = true;

            Web.CoreWebView2.Navigate(new Uri(_filePath).AbsoluteUri);
            StatusOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusDetail.Text =
                "Could not start the built-in viewer.\n" +
                "Make sure the WebView2 Runtime is installed (it ships with Windows 10/11).\n\n" +
                ex.Message;
        }
    }
}
