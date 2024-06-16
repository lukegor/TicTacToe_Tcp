using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace test
{
    internal sealed class Client
    {
        private readonly string hostname;
        private readonly int port;

        public Client(string hostname, int port)
        {
            this.hostname = hostname;
            this.port = port;
        }

        public void Start()
        {
            while (true)
            {
                Console.WriteLine("Enter the room name to join:");
                string roomName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(roomName))
                {
                    Console.WriteLine("Room name cannot be empty. Please enter a valid room name.");
                    continue; // Prompt again for room name
                }

                TcpClient client = new TcpClient();
                client.Connect(hostname, port);
                NetworkStream stream = client.GetStream();

                byte[] data = Encoding.ASCII.GetBytes($"JOIN {roomName}");
                stream.Write(data, 0, data.Length);

                Thread receiveThread = new Thread(() => ReceiveMessages(stream, client));
                receiveThread.Start();

                while (client.Connected)
                {
                    string input = Console.ReadLine();
                    if (client.Connected)
                    {
                        data = Encoding.ASCII.GetBytes(input);
                        stream.Write(data, 0, data.Length);
                    }
                }

                receiveThread.Join();
            }
        }

        private static void ReceiveMessages(NetworkStream stream, TcpClient client)
        {
            while (client.Connected)
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    break;
                }
                if (bytesRead == 0) break;

                string serverMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine(serverMessage);

                if (serverMessage.Contains("Room is full"))
                {
                    client.Close();
                    break;
                }

                if (serverMessage.Contains("wins") || serverMessage.Contains("draw"))
                {
                    Console.WriteLine("Game over. Press Enter to join another room.");
                    client.Close();
                    break;
                }

                if (serverMessage.Contains("Player disconnected. You win by walkover."))
                {
                    Console.WriteLine("Game over. Press Enter to join another room.");
                    client.Close();
                    break;
                }
            }
        }
    }
}
