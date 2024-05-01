using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Client_Server_App
{
    internal class ServerTCP
    {
        private int port;
        private TcpListener listener;
        private TcpClient client;

        public ServerTCP(int port)
        {
            this.port = port;
            listener = new TcpListener(IPAddress.Any, port);
        }

        internal void Start()
        {
            this.listener.Start();
            System.Diagnostics.Debug.WriteLine($"Server has started on port {port}");
            Thread serverThread = new Thread(() => ListenForClients());
            serverThread.Start();
        }

        public void ListenForClients()
        {
            while (true)
            {
                var client = listener.AcceptTcpClient();
                this.client = client;
                Thread serverThread = new Thread(() => ListenFromSpecificClient(this.client))
                {
                    IsBackground = true
                };
                serverThread.Start();
            }
        }

        public void ListenFromSpecificClient(TcpClient client)
        {
            NetworkStream networkStream = client.GetStream();
            StreamReader streamReader = new StreamReader(networkStream);
            while(true)
            {
                string jsonMessage = streamReader.ReadLine();
                if (jsonMessage == null)
                    break;

                System.Diagnostics.Debug.WriteLine($"Server got message: {jsonMessage}");
                Broadcast();
                //przetwarzanie wiadomości, deserializacja
            }
        }

        public void Broadcast()
        {
            string data = "Message from server\n";
            byte[] buffer = Encoding.UTF8.GetBytes(data);
            try
            {
                NetworkStream stream = client.GetStream();
                if (stream.CanWrite)
                {
                    stream.Write(buffer, 0, buffer.Length);
                    System.Diagnostics.Debug.WriteLine($"Server sent data: {data}");
                }
            }
            catch (Exception e)
            {

            }
        }
    }
}
