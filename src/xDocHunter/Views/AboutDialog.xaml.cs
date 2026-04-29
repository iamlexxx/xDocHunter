using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace xDocHunter.Views;

public partial class AboutDialog : Window
{
    // Update check URL — host a JSON file with {"version":"1.x.x"} at this address to enable updates.
    private const string UpdateCheckUrl = "https://raw.githubusercontent.com/iamlexxx/xDocHunter/main/version.json";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Version _current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = $"Version {_current.Major}.{_current.Minor}.{_current.Build}";
        CopyrightText.Text = $"© {DateTime.Now.Year} Lex";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking for updates…";
        UpdateStatusText.Foreground = FindResource("SubtleBrush") as System.Windows.Media.Brush;

        try
        {
            var json = await _http.GetStringAsync(UpdateCheckUrl);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("version", out var vProp)
                && Version.TryParse(vProp.GetString(), out var latest))
            {
                if (latest > _current)
                {
                    UpdateStatusText.Text = $"New version {latest} is available!";
                    UpdateStatusText.Foreground = FindResource("PrimaryBrush") as System.Windows.Media.Brush;
                }
                else
                {
                    UpdateStatusText.Text = "You're up to date.";
                    UpdateStatusText.Foreground = FindResource("SubtleBrush") as System.Windows.Media.Brush;
                }
            }
            else
            {
                UpdateStatusText.Text = "Could not read version info.";
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            UpdateStatusText.Text = "Update info not found. Check back later.";
        }
        catch
        {
            UpdateStatusText.Text = "Unable to check — no connection or server unavailable.";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
