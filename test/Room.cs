using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace test
{
    public sealed class Room
    {
        public string Name { get; private set; }
        private TcpClient[] players = new TcpClient[2];
        private NetworkStream[] streams = new NetworkStream[2];
        private int playerCount = 0;
        private char[,] board = new char[3, 3];
        private int currentPlayer = 0;
        public bool gameEnded = false;

        public int PlayerCount => playerCount;

        public Room(string name)
        {
            Name = name;
        }

        public void AddPlayer(TcpClient client, NetworkStream stream)
        {
            if (playerCount < 2)
            {
                players[playerCount] = client;
                streams[playerCount] = stream;
                playerCount++;

                if (playerCount == 2)
                {
                    StartGame();
                }
            }
        }

        private void StartGame()
        {
            gameEnded = false;
            Thread gameThread = new Thread(() => GameLoop());
            gameThread.Start();
        }

        private void GameLoop()
        {
            InitializeBoard();

            while (!gameEnded)
            {
                SendMessage("Your turn", streams[currentPlayer]);
                SendMessage("Opponent's turn", streams[1 - currentPlayer]);
                bool validMove = false;

                while (!validMove)
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead;
                    try
                    {
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
                    string move = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                    validMove = ProcessMove(move, currentPlayer);
                    if (!validMove)
                    {
                        SendMessage("Invalid move. Try again.", streams[currentPlayer]);
                    }
                    else
                    {
                        DisplayBoard();
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

            CloseConnections();
            playerCount = 0;
            gameEnded = false;
        }

        #region GameLogic/GameMaintenance
        private void InitializeBoard()
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    board[i, j] = ' ';
        }

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

        private bool CheckDraw()
        {
            foreach (char cell in board)
            {
                if (cell == ' ')
                    return false;
            }
            return true;
        }

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

        private void SendMessage(string message)
        {
            foreach (var stream in streams)
            {
                SendMessage(message, stream);
            }
        }

        private void SendMessage(string message, NetworkStream stream)
        {
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

        private void NotifyWalkover(int disconnectingPlayer)
        {
            int otherPlayer = 1 - disconnectingPlayer;
            if (streams[otherPlayer] != null)
            {
                byte[] data = Encoding.ASCII.GetBytes("Opponent disconnected. You win by walkover.");
                streams[otherPlayer].Write(data, 0, data.Length);
            }
        }

        private void CloseConnections()
        {
            foreach (var client in players)
            {
                client?.Close();
            }
        }
    }
}
