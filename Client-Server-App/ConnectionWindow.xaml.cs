using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Client_Server_App
{
    /// <summary>
    /// Interaction logic for ConnectionWindow.xaml
    /// </summary>
    public partial class ConnectionWindow : Window
    {
        private ClientTCP clientTcp;
        private ServerTCP serverTCP;
        public ConnectionWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            clientTcp = new ClientTCP();
            clientTcp.Connect("127.0.0.1", 1111);
            clientTcp.SendData();
        }

        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            serverTCP = new ServerTCP(1111);
            Thread serverThread = new Thread(() => serverTCP.Start());
            serverThread.Start();
        }
    }
}
