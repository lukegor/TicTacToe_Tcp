using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace test
{
    internal sealed class Server
    {
        private readonly int port;
        private TcpListener tcpListener;
        private List<Room> rooms;

        public Server(int port)
        {
            this.port = port;
            rooms = new List<Room>();
        }

        public void Start()
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Console.WriteLine($"Server started on port {port}...");

            while (true)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                Console.WriteLine("Client connected...");

                Thread thread = new Thread(() => HandleClient(client));
                thread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();

            string roomName = ValidateRoomName(stream);
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
                    //client.Close();
                }
            }
        }

        private string ValidateRoomName(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            string roomName = string.Empty;

            try
            {
                while (string.IsNullOrWhiteSpace(roomName))
                {
                    string requestMessage = "Enter the room name to join:";
                    byte[] requestData = Encoding.ASCII.GetBytes(requestMessage);
                    stream.Write(requestData, 0, requestData.Length);

                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string clientMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                    if (!string.IsNullOrWhiteSpace(clientMessage) && clientMessage.StartsWith("JOIN"))
                    {
                        roomName = clientMessage.Split(' ')[1];
                        if (string.IsNullOrWhiteSpace(roomName))
                        {
                            string errorMessage = "Room name cannot be empty. Please enter a valid room name.\n";
                            byte[] errorData = Encoding.ASCII.GetBytes(errorMessage);
                            stream.Write(errorData, 0, errorData.Length);
                        }
                    }
                }
            }
            catch(IOException ex)
            {
                Console.WriteLine($"Client disconnected or encountered error: {ex.Message}");
            }

            return roomName;
        }
    }
}
