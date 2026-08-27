using System.Windows;
using System.Windows.Controls;

namespace TicTacToe;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectionWindow connectionWindow = new();
        connectionWindow.Show();
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.ThemeMode = ThemeSelector.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
