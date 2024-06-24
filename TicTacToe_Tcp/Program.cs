using System.Net.Sockets;
using System.Net;
using System.Text;

namespace TicTacToe_Tcp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Do you want to start the server or the client? (S/C)");
            string choice = Console.ReadLine().ToUpper();

            if (choice == "S")
            {
                // Uruchomienie serwera
                StartServer();
            }
            else if (choice == "C")
            {
                // Uruchomienie klienta
                StartClient();
            }
            else
            {
                Console.WriteLine("Invalid choice.");
            }
        }

        /// <summary>
        /// Metoda uruchamiająca serwer
        /// </summary>
        private static void StartServer()
        {
            Console.Write("Enter the port number to listen on: ");
            int port;
            while (!int.TryParse(Console.ReadLine(), out port) || port <= 0)
            {
                Console.WriteLine("Invalid port number. Please enter a valid port number:");
            }

            // utworzenie instancji serwera
            Server server = new Server(port);
            // rozpoczęcie nasłuchiwania przez server
            server.Start();
        }

        /// <summary>
        /// Metoda uruchamiająca klienta
        /// </summary>
        private static void StartClient()
        {
            Console.WriteLine("Do you want to use the default hostname (localhost)? (Y/N)");
            string useDefault = Console.ReadLine()?.ToUpper();

            string hostname;
            if (useDefault == "Y")
            {
                hostname = "localhost"; // domyślny hostname to localhost
            }
            else
            {
                Console.WriteLine("Enter the server hostname or IP address:");
                hostname = Console.ReadLine();
            }

            int port = GetPortNumber();

            // Utworzenie instancji klienta i rozpoczęcie jego działania (próba dołączenia do pokoju)
            Client client = new Client(hostname, port);
            // Rozpoczęcie połączenia przez klienta
            client.Start();
        }

        /// <summary>
        /// Metoda pobierająca numer portu od użytkownika i validująca go
        /// </summary>
        /// <returns></returns>
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
