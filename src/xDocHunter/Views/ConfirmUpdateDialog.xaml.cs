using System.Windows;

namespace xDocHunter.Views;

public partial class ConfirmUpdateDialog : Window
{
    public ConfirmUpdateDialog(string filePath, string rootPath)
    {
        InitializeComponent();
        FilePathText.Text = filePath;
        RootPathText.Text = rootPath;
    }

    private void Update_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
