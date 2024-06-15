using System.Net.Sockets;
using System.Net;
using System.Text;

namespace test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Do you want to start the server or the client? (S/C)");
            string choice = Console.ReadLine().ToUpper();

            if (choice == "S")
            {
                StartServer();
            }
            else if (choice == "C")
            {
                StartClient();
            }
            else
            {
                Console.WriteLine("Invalid choice.");
            }
        }

        private static void StartServer()
        {
            TcpListener server = new TcpListener(IPAddress.Any, 5000);
            server.Start();
            Console.WriteLine("Server started...");

            List<Room> rooms = new List<Room>();

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                Console.WriteLine("Client connected...");

                Thread thread = new Thread(() => HandleClient(client, rooms));
                thread.Start();
            }
        }

        private static void HandleClient(TcpClient client, List<Room> rooms)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string clientMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            if (clientMessage.StartsWith("JOIN"))
            {
                string roomName = clientMessage.Split(' ')[1];
                lock (rooms)
                {
                    Room room = rooms.Find(r => r.Name == roomName);
                    if (room == null)
                    {
                        room = new Room(roomName);
                        rooms.Add(room);
                    }

                    if (room.PlayerCount < 2 || room.PlayerCount == 0)
                    {
                        room.AddPlayer(client, stream);
                        if (room.PlayerCount == 0)
                        {
                            rooms.Remove(room);
                        }
                    }
                    else
                    {
                        byte[] data = Encoding.ASCII.GetBytes("Room is full.");
                        stream.Write(data, 0, data.Length);
                        client.Close();
                    }
                }
            }
        }

        private static void StartClient()
        {
            while (true)
            {
                Console.WriteLine("Enter the room name to join:");
                string roomName = Console.ReadLine();

                TcpClient client = new TcpClient();
                client.Connect("127.0.0.1", 5000);
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
