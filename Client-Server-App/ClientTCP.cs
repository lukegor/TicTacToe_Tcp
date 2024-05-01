using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Client_Server_App
{
    internal class ClientTCP
    {
        private TcpClient tcpClient;

        internal void Connect(string ip, int port)
        {
            tcpClient = new TcpClient(ip, port);
            System.Diagnostics.Debug.WriteLine(tcpClient.Connected);
            if (tcpClient.Connected)
            {
                System.Diagnostics.Debug.WriteLine("Client has connected succesfully");
                Thread clientThread = new Thread(() => ListenFromServer())
                {
                    IsBackground = true
                };
                clientThread.Start();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Client cannot connect");
            }
            //tcpClient.Connect(ip, port);
        }

        internal void SendData()
        {
            NetworkStream stream = tcpClient.GetStream();
            string json = "Message from client\n";
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            stream.Write(buffer, 0, buffer.Length);
            System.Diagnostics.Debug.WriteLine($"Client message: {json}");
        }

        public void ListenFromServer()
        {
            NetworkStream networkStream = tcpClient.GetStream();
            StreamReader streamReader = new StreamReader(networkStream);
            while (true)
            {
                string jsonMessage = streamReader.ReadLine();
                if (jsonMessage == null)
                    break;

                System.Diagnostics.Debug.WriteLine($"Client got message: {jsonMessage}");

                //przetwarzanie wiadomości, deserializacja
            }
        }
    }
}
