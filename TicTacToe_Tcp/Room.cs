using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TicTacToe_Tcp
{
    /// <summary>
    /// Klasa reprezentująca pokój gry (room) "kółko i krzyżyk"
    /// </summary>
    public sealed class Room
    {
        // nazwa pokoju
        public string Name { get; private set; }
        // gracze (klienci)
        private TcpClient[] players = new TcpClient[2];
        // strumienie do komunikacji z graczami
        private NetworkStream[] streams = new NetworkStream[2];
        // liczba graczy aktualnie w pokoju
        private int playerCount = 0;
        // aktualny stan planszy do gry w kółko i krzyżyk
        private char[,] board = new char[3, 3];
        // indeks gracza, który ma teraz ruch (0 lub 1)
        private int currentPlayer = 0;
        // flaga wskazująca "czy gra się skończyła"
        public bool gameEnded = false;

        // getter liczby graczy
        public int PlayerCount => playerCount;

        public Room(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Metoda dodająca gracza do pokoju, jeśli pokój jest pełny rozpoczyna grę
        /// </summary>
        /// <param name="client"></param>
        /// <param name="stream"></param>
        public void AddPlayer(TcpClient client, NetworkStream stream)
        {
            if (playerCount < 2)
            {
                players[playerCount] = client;
                streams[playerCount] = stream;
                playerCount++;

                // jeśli pokój jest pełny, rozpoczyna się gra
                if (playerCount == 2)
                {
                    StartGame();
                }
            }
        }

        /// <summary>
        /// Metoda rozpoczynająca grę
        /// </summary>
        private void StartGame()
        {
            gameEnded = false;

            // utworzenie nowego wątku dla gry
            Thread gameThread = new Thread(() => GameLoop());
            // rozpoczęcie wątku gry
            gameThread.Start();
        }

        /// <summary>
        /// Pętla gry
        /// </summary>
        private void GameLoop()
        {
            // inicjalizacja planszy
            InitializeBoard();

            while (!gameEnded)
            {
                // powiadomienie obecnego gracza o jego turze
                SendMessage("Your turn", streams[currentPlayer]);
                // powiadomienie drugiego gracza o turze obecnego gracza
                SendMessage("Opponent's turn", streams[1 - currentPlayer]);
                bool validMove = false;

                while (!validMove)
                {
                    // bufor do przechowywania danych odebranych od gracza
                    byte[] buffer = new byte[1024];
                    int bytesRead;
                    try
                    {
                        // oddczytanie danych od obecnego gracza
                        bytesRead = streams[currentPlayer].Read(buffer, 0, buffer.Length);
                    }
                    catch
                    {
                        NotifyWalkover(currentPlayer);
                        gameEnded = true;
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        NotifyWalkover(currentPlayer);
                        gameEnded = true;
                        break;
                    }

                    // konwersja odebranych bajtów na ciąg znaków, reprezentujący ruch gracza
                    string move = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                    // przetwarzanie ruchu gracza
                    validMove = ProcessMove(move, currentPlayer);
                    if (!validMove)
                    {
                        SendMessage("Invalid move. Try again.", streams[currentPlayer]);
                    }
                    else
                    {
                        // wyświetlenie planszy
                        DisplayBoard();

                        // sprawdzenie warunków kończących grę, jak zwycięstwo i remis
                        if (CheckWin())
                        {
                            SendMessage($"Player {currentPlayer + 1} wins!");
                            gameEnded = true;
                            break;
                        }

                        if (CheckDraw())
                        {
                            SendMessage("It's a draw!");
                            gameEnded = true;
                            break;
                        }

                        currentPlayer = 1 - currentPlayer;
                    }
                }
                if (CheckWin() || CheckDraw())
                {
                    gameEnded = true;
                    break;
                }
            }

            // zamknięcie połączeń po zakończeniu gry
            CloseConnections();
            playerCount = 0;
            gameEnded = false;
        }

        #region GameLogic/GameMaintenance
        /// <summary>
        /// Metoda inicjalizująca planszę
        /// </summary>
        private void InitializeBoard()
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    board[i, j] = ' ';
        }

        /// <summary>
        /// Metoda przetwarzająca ruch gracza
        /// </summary>
        /// <param name="move"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool ProcessMove(string move, int player)
        {
            int row, col;
            try
            {
                string[] parts = move.Split(',');
                row = int.Parse(parts[0]);
                col = int.Parse(parts[1]);
            }
            catch
            {
                return false;
            }

            if (row < 0 || row >= 3 || col < 0 || col >= 3 || board[row, col] != ' ')
                return false;

            board[row, col] = player == 0 ? 'X' : 'O';
            return true;
        }

        /// <summary>
        /// Metoda sprawdzająca wygraną
        /// </summary>
        /// <returns></returns>
        private bool CheckWin()
        {
            for (int i = 0; i < 3; i++)
            {
                if (board[i, 0] == board[i, 1] && board[i, 1] == board[i, 2] && board[i, 0] != ' ')
                    return true;

                if (board[0, i] == board[1, i] && board[1, i] == board[2, i] && board[0, i] != ' ')
                    return true;
            }

            if (board[0, 0] == board[1, 1] && board[1, 1] == board[2, 2] && board[0, 0] != ' ')
                return true;

            if (board[0, 2] == board[1, 1] && board[1, 1] == board[2, 0] && board[0, 2] != ' ')
                return true;

            return false;
        }

        /// <summary>
        /// Metoda sprawdzająca remis
        /// </summary>
        /// <returns></returns>
        private bool CheckDraw()
        {
            foreach (char cell in board)
            {
                if (cell == ' ')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Metoda wyświetlająca planszę
        /// </summary>
        private void DisplayBoard()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    sb.Append(board[i, j]);
                    if (j < 2) sb.Append("|");
                }
                sb.AppendLine();
                if (i < 2) sb.AppendLine("-----");
            }
            SendMessage(sb.ToString());
        }
        #endregion

        /// <summary>
        /// Metoda wysyłająca wiadomość do wszystkich graczy w pokoju
        /// </summary>
        /// <param name="message"></param>
        private void SendMessage(string message)
        {
            foreach (var stream in streams)
            {
                SendMessage(message, stream);
            }
        }

        /// <summary>
        /// Metoda wysyłająca wiadomość do konkretnego gracza w pokoju
        /// </summary>
        /// <param name="message"></param>
        /// <param name="stream"></param>
        private void SendMessage(string message, NetworkStream stream)
        {
            // konwersja wiadomości do wysłania na tablicę bajtów
            byte[] data = Encoding.ASCII.GetBytes(message);

            try
            {
                stream.Write(data, 0, data.Length);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error sending message to a client: {ex.Message}");
            }
        }

        /// <summary>
        /// Metoda informująca o walkowerze
        /// </summary>
        /// <param name="disconnectingPlayer"></param>
        private void NotifyWalkover(int disconnectingPlayer)
        {
            int otherPlayer = 1 - disconnectingPlayer;
            if (streams[otherPlayer] != null)
            {
                // konwersja wiadomości do wysłania na tablicę bajtów
                byte[] data = Encoding.ASCII.GetBytes("Opponent disconnected. You win by walkover.");
                streams[otherPlayer].Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// Metoda zamykająca połączenia
        /// </summary>
        private void CloseConnections()
        {
            foreach (var client in players)
            {
                client?.Close();
            }
        }
    }
}
