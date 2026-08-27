using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TicTacToe.Core.Transports;
using Microsoft.Extensions.Logging.Abstractions;

namespace TicTacToe.Core.Game;

/// <summary>
/// Server-side referee: owns the room table, routes client envelopes, validates
/// room names, and pushes room-list updates to every connection.
/// </summary>
internal sealed class LobbyService : IDisposable
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(10);

    private readonly IServerTransport _server;
    private readonly TimeSpan _gracePeriod;
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Room> _membership = [];
    private readonly Dictionary<Guid, string> _playerNames = [];
    private readonly ILogger<LobbyService> _logger;
    private readonly object _sync = new();

    public event Action<string>? LogReceived;
    public event Action? RoomsChanged;

    public LobbyService(IServerTransport server, TimeSpan? gracePeriod = null, ILogger<LobbyService>? logger = null)
    {
        _server = server;
        _gracePeriod = gracePeriod ?? DefaultGracePeriod;
        _logger = logger ?? NullLogger<LobbyService>.Instance;
    }

    public void Start()
    {
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }

    public IReadOnlyList<RoomInfoRecord> GetRooms()
    {
        lock (_sync)
        {
            return [.. _rooms.Values.Select(ToInfo)];
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (Room room in _rooms.Values)
            {
                room.Dispose();
            }

            _rooms.Clear();
            _membership.Clear();
            _playerNames.Clear();
        }
    }

    private void OnClientConnected(Guid id) => PushRoomList();

    private void OnClientDisconnected(Guid id)
    {
        Room? room;
        string name;
        lock (_sync)
        {
            _membership.Remove(id, out room);
            name = LookupNameCore(id);
            _playerNames.Remove(id);
        }

        if (room is not null)
        {
            room.HandleDisconnect(id);
        }

        Log($"{name} ({Short(id)}) disconnected.");
    }

    private void OnMessageReceived(Guid sender, string line)
    {
        switch (GameJson.TryParse(line))
        {
            case HelloRecord hello:
                HandleHello(sender, hello.PlayerName);
                break;

            case CreateRoomRecord create:
                HandleCreate(sender, create.Name);
                break;

            case JoinRoomRecord join:
                HandleJoin(sender, join.Name);
                break;

            case LeaveRoomRecord:
                HandleLeave(sender);
                break;

            case MoveRequestRecord move:
                Route(sender, room => room.HandleMove(sender, move.Cell));
                break;

            case RematchOfferRecord:
                Route(sender, room => room.HandleRematchOffer(sender));
                break;

            case GameStateRecord:
            case RoomListRecord:
            case JoinedRecord:
            case LeftRecord:
            case ErrorRecord:
                // Server-originated envelopes are never accepted from clients.
                break;

            case null:
                Route(sender, room => room.HandleChat(sender, line));
                break;
        }
    }

    /// <summary>Records the announced display name; the connection log doubles as
    /// the identity announcement ("Alice (1a2b3c4d) connected.").</summary>
    private void HandleHello(Guid id, string rawName)
    {
        string name = ResolvePlayerName(rawName, id);
        lock (_sync)
        {
            _playerNames[id] = name;
        }

        Log($"{name} ({Short(id)}) connected.");
    }

    private void HandleCreate(Guid creator, string rawName)
    {
        string name = rawName.Trim();
        if (name.Length is 0 or > 30)
        {
            SendError(creator, "Room name must be 1-30 characters.");
            return;
        }

        Room room;
        lock (_sync)
        {
            if (_rooms.ContainsKey(name))
            {
                SendError(creator, $"Room '{name}' already exists.");
                return;
            }

            room = new Room(
                name,
                (id, line) => _ = SafeSendAsync(id, line),
                Log,
                _gracePeriod);
            room.MembershipChanged += PushRoomList;
            room.MemberReleased += ReleaseMember;
            room.RoomClosed += ids => CloseRoom(room, ids);
            _rooms[name] = room;
        }

        // Seat outside the lobby lock: Room raises MembershipChanged synchronously,
        // which re-enters PushRoomList -> GetRooms (same lock).
        string creatorName = LookupName(creator);
        room.Seat(creator, creatorName);
        lock (_sync)
        {
            _membership[creator] = room;
        }

        Log($"Room '{name}' created by {creatorName} ({Short(creator)}).");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void HandleJoin(Guid joiner, string rawName)
    {
        string name = rawName.Trim();
        Room? room;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(name, out room))
            {
                SendError(joiner, $"Room '{name}' does not exist.");
                return;
            }
        }

        if (room.HasVacancy)
        {
            string joinerName = LookupName(joiner);
            room.Seat(joiner, joinerName);
        }
        else
        {
            room.AddSpectator(joiner);
        }

        lock (_sync)
        {
            _membership[joiner] = room;
        }

        Log($"{LookupName(joiner)} ({Short(joiner)}) joined '{room.Name}'.");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void HandleLeave(Guid id)
    {
        Room? room;
        lock (_sync)
        {
            _membership.Remove(id, out room);
        }

        room?.HandleLeave(id);
    }

    private void Route(Guid sender, Action<Room> action)
    {
        Room? room;
        lock (_sync)
        {
            _membership.TryGetValue(sender, out room);
        }

        if (room is not null)
        {
            action(room);
        }
    }

    private void ReleaseMember(Guid id)
    {
        lock (_sync)
        {
            _membership.Remove(id);
        }

        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void CloseRoom(Room room, IReadOnlyList<Guid> evicted)
    {
        lock (_sync)
        {
            _rooms.Remove(room.Name);
            foreach (Guid id in evicted)
            {
                _membership.Remove(id);
            }
        }

        Log($"Room '{room.Name}' closed.");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void PushRoomList()
    {
        _ = _server.BroadcastLineAsync(GameJson.Serialize(new RoomListRecord(GetRooms())));
    }

    private void SendError(Guid id, string message)
    {
        _ = SafeSendAsync(id, GameJson.Serialize(new ErrorRecord(message)));
        Log($"Error for {Short(id)}: {message}");
    }

    private async Task SafeSendAsync(Guid id, string line)
    {
        try
        {
            await _server.SendToAsync(id, line).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Recipient vanished mid-write; disconnect handling takes over.
        }
    }

    private static RoomInfoRecord ToInfo(Room room) =>
        new(room.Name, room.PlayerCount, room.SpectatorCount);

    /// <summary>Normalizes a client-supplied player name; blank names fall back to
    /// a deterministic guest label derived from the connection id.</summary>
    private static string ResolvePlayerName(string raw, Guid id)
    {
        string name = raw.Trim();
        if (name.Length > 30)
        {
            name = name[..30];
        }

        return name.Length == 0 ? $"Guest-{Short(id)}" : name;
    }

    private string LookupName(Guid id)
    {
        lock (_sync)
        {
            return LookupNameCore(id);
        }
    }

    private string LookupNameCore(Guid id) => _playerNames.GetValueOrDefault(id, $"Guest-{Short(id)}");

    private void Log(string message)
    {
        _logger.LogInformation("{Message}", message);
        LogReceived?.Invoke(message);
    }

    private static string Short(Guid id) => id.ToString()[..8];
}
