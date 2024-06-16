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

            Client client = new Client(hostname, port);
            client.Start();
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
    }
}
