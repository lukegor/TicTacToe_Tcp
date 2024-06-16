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
            Console.Write("Enter the port number to listen on: ");
            int port;
            while (!int.TryParse(Console.ReadLine(), out port) || port <= 0)
            {
                Console.WriteLine("Invalid port number. Please enter a valid port number:");
            }

            Server server = new Server(port);
            server.Start();
        }

        private static void StartClient()
        {
            Console.WriteLine("Do you want to use the default hostname (localhost)? (Y/N)");
            string useDefault = Console.ReadLine()?.ToUpper();

            string hostname;
            if (useDefault == "Y")
            {
                hostname = "localhost"; // Default hostname is localhost
            }
            else
            {
                Console.WriteLine("Enter the server hostname or IP address:");
                hostname = Console.ReadLine();
            }

            int port = GetPortNumber();

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

        private static int GetPortNumber()
        {
            Console.WriteLine("Enter the port number to connect:");
            int port;
            while (!int.TryParse(Console.ReadLine(), out port) || port <= 0)
            {
                Console.WriteLine("Invalid port number. Please enter a valid port number:");
            }
            return port;
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
