using System.Windows;

namespace Client_Server_App;

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
}
