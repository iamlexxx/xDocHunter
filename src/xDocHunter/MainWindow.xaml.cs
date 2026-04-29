using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xDocHunter.Models;
using xDocHunter.ViewModels;

namespace xDocHunter;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;
    private GridLength _lastTreeWidth = new GridLength(260);

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            if (DataContext is INotifyPropertyChanged npc)
                npc.PropertyChanged += Vm_PropertyChanged;
            ApplyTreeColumnWidth();
        };
        ResultsGrid.LoadingRow += ResultsGrid_LoadingRow;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowTree))
            ApplyTreeColumnWidth();
    }

    private void ApplyTreeColumnWidth()
    {
        if (Vm.ShowTree)
        {
            TreeColumn.Width = _lastTreeWidth.Value > 0 ? _lastTreeWidth : new GridLength(260);
            TreeColumn.MinWidth = 180;
        }
        else
        {
            if (TreeColumn.Width.IsAbsolute && TreeColumn.Width.Value > 0)
                _lastTreeWidth = TreeColumn.Width;
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = GridLength.Auto;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable d) d.Dispose();
    }

    private void ResultsGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu))
        };

        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => { if (e.Row.DataContext is FileEntry f) Vm.OpenFileCommand.Execute(f); };

        var reveal = new MenuItem { Header = "Reveal in Explorer" };
        reveal.Click += (_, _) => { if (e.Row.DataContext is FileEntry f) Vm.RevealInExplorerCommand.Execute(f); };

        var copy = new MenuItem { Header = "Copy full path" };
        copy.Click += (_, _) => { if (e.Row.DataContext is FileEntry f) Vm.CopyPathCommand.Execute(f); };

        menu.Items.Add(open);
        menu.Items.Add(reveal);
        menu.Items.Add(copy);
        e.Row.ContextMenu = menu;
    }

    private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is FileEntry entry)
            Vm.OpenFileCommand.Execute(entry);
    }

    private void SaveIndexButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsIndexFromFile)
        {
            Vm.UpdateIndexCommand.Execute(null);
            return;
        }

        if (sender is Button b && b.ContextMenu is not null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        }
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is not null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.Closed -= FilterMenu_Closed;
            b.ContextMenu.Closed += FilterMenu_Closed;
            b.ContextMenu.IsOpen = true;
        }
    }

    private void FilterMenu_Closed(object sender, RoutedEventArgs e)
    {
        Vm.ApplyExtensionFilterCommand.Execute(null);
    }

    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Models.FolderNode node)
            Vm.SelectFolderCommand.Execute(node);
    }
}
