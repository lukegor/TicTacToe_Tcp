using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TicTacToe_Tcp
{
    /// <summary>
    /// Klasa reprezentująca klienta
    /// </summary>
    internal sealed class Client
    {
        // nazwa hosta, np. "localhost", "127.0.0.1"
        private readonly string hostname;
        // numer portu, np. 1111
        private readonly int port;

        public Client(string hostname, int port)
        {
            this.hostname = hostname;
            this.port = port;
        }

        /// <summary>
        /// Metoda rozpoczynająca pracę klienta
        /// </summary>
        public void Start()
        {
            while (true)
            {
                JoinRoom();
            }
        }

        /// <summary>
        /// Metoda łącząca klienta do serwera i pokoju
        /// </summary>
        private void JoinRoom()
        {
            // połączenie z serwerem
            TcpClient client = new TcpClient();
            client.Connect(hostname, port);
            NetworkStream stream = client.GetStream();

            // odczyt wiadomości serwera proszącej o nazwę pokoju
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string serverMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            Console.WriteLine(serverMessage);

            string roomName = string.Empty;
            while (string.IsNullOrWhiteSpace(roomName))
            {
                roomName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(roomName))
                {
                    Console.WriteLine("Room name cannot be empty. Please enter a valid room name.");
                }
            }

            // konwersja wiadomości "JOIN {roomName}" na tablicę bajtów
            byte[] data = Encoding.ASCII.GetBytes($"JOIN {roomName}");
            // wysłanie żądania dołączenia do pokoju
            stream.Write(data, 0, data.Length);

            // utworzenie wątku do odbierania wiadomości
            Thread receiveThread = new Thread(() => ReceiveMessages(stream, client));
            // uruchomienie wątku
            receiveThread.Start();

            while (client.Connected)
            {
                string input = Console.ReadLine();
                if (client.Connected)
                {
                    // konwersja wiadomości od użytkownika na tablicę bajtów
                    data = Encoding.ASCII.GetBytes(input);
                    // wysłanie wiadomości do serwera
                    stream.Write(data, 0, data.Length);
                }
            }

            // oczekiwanie na zakończenie wątku odbierającego wiadomości
            receiveThread.Join();
        }

        /// <summary>
        /// Metoda odbierająca wiadomości od serwera
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="client"></param>
        private static void ReceiveMessages(NetworkStream stream, TcpClient client)
        {
            while (client.Connected)
            {
                // bufor do odbierania danych
                byte[] buffer = new byte[1024];
                int bytesRead;
                try
                {
                    // odczyt danych ze strumienia sieciowego
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    // przerwanie pętli w przypadku błędu
                    break;
                }
                // przerwanie pętli, jeśli nie odczytano żadnych danych
                if (bytesRead == 0) break;

                // konwersja odebranych bajtów na ciąg znaków
                string serverMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine(serverMessage);

                // sprawdzenie czy wiadomość zawiera którąś z przewidywanych możliwych informacji
                if (serverMessage.Contains("Room is full"))
                {
                    client.Close();
                    break;
                }

                if (serverMessage.Contains("wins") || serverMessage.Contains("draw"))
                {
                    Console.WriteLine("Game over. Press Enter to reconnect.");
                    client.Close();
                    break;
                }

                if (serverMessage.Contains("Opponent disconnected. You win by walkover."))
                {
                    Console.WriteLine("Game over. Press Enter to reconnect.");
                    client.Close();
                    break;
                }
            }
        }
    }
}
