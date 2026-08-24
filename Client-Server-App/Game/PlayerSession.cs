using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Client-side session: lobby interactions, seated gameplay, and automatic
/// reconnect-to-seat after unexpected transport loss.
/// </summary>
internal sealed class PlayerSession : IDisposable
{
    public static readonly TimeSpan DefaultReconnectBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private readonly Func<Task<IClientTransport>> _connectFactory;
    private readonly TimeSpan _reconnectBudget;
    private readonly CancellationTokenSource _disposal = new();
    private IClientTransport? _transport;

    public event Action<IReadOnlyList<RoomInfoRecord>>? RoomsUpdated;
    public event Action<JoinedRecord>? Seated;
    public event Action<GameStateRecord>? StateReceived;
    public event Action<string>? ReturnedToLobby;
    public event Action<string>? ErrorReceived;
    public event Action<string>? LogReceived;
    public event Action? ReconnectingStarted;

    public PlayerSessionState State { get; private set; } = PlayerSessionState.Connecting;
    public string? CurrentRoomName { get; private set; }
    public string? MyMark { get; private set; }
    public bool IsSpectator => State == PlayerSessionState.Seated && MyMark is null;
    public GameStateRecord? CurrentState { get; private set; }
    public IReadOnlyList<RoomInfoRecord> LatestRooms { get; private set; } = [];

    public PlayerSession(Func<Task<IClientTransport>> connectFactory, TimeSpan? reconnectBudget = null)
    {
        _connectFactory = connectFactory;
        _reconnectBudget = reconnectBudget ?? DefaultReconnectBudget;
    }

    /// <summary>Opens the first connection. Throws when the server is unreachable.</summary>
    public async Task ConnectAsync()
    {
        IClientTransport transport = await _connectFactory().ConfigureAwait(true);
        AttachTransport(transport);
        State = PlayerSessionState.Lobby;
    }

    public async Task CreateRoomAsync(string name) =>
        await SendAsync(new CreateRoomRecord(name)).ConfigureAwait(true);

    public async Task JoinRoomAsync(string name) =>
        await SendAsync(new JoinRoomRecord(name)).ConfigureAwait(true);

    public async Task LeaveRoomAsync()
    {
        if (State != PlayerSessionState.Seated)
        {
            return;
        }

        await SendAsync(new LeaveRoomRecord()).ConfigureAwait(true);
        ReturnToLobby("left");
    }

    public async Task PlayCellAsync(int cell)
    {
        if (State != PlayerSessionState.Seated || IsSpectator)
        {
            return;
        }

        await SendAsync(new MoveRequestRecord(cell)).ConfigureAwait(true);
    }

    public async Task SendRematchOfferAsync()
    {
        if (State != PlayerSessionState.Seated || IsSpectator)
        {
            return;
        }

        await SendAsync(new RematchOfferRecord()).ConfigureAwait(true);
    }

    private void AttachTransport(IClientTransport transport)
    {
        DetachTransport();
        _transport = transport;
        _transport.MessageReceived += OnLineReceived;
        _transport.Disconnected += OnDisconnected;
    }

    private void DetachTransport()
    {
        if (_transport is not null)
        {
            _transport.MessageReceived -= OnLineReceived;
            _transport.Disconnected -= OnDisconnected;
        }
    }

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case RoomListRecord list:
                LatestRooms = list.Rooms;
                RoomsUpdated?.Invoke(list.Rooms);
                break;

            case JoinedRecord joined:
                CurrentRoomName = joined.Room;
                MyMark = joined.Mark;
                CurrentState = joined.State;
                State = PlayerSessionState.Seated;
                Seated?.Invoke(joined);
                if (joined.Restored)
                {
                    LogReceived?.Invoke($"Reconnected to '{joined.Room}'.");
                }

                break;

            case GameStateRecord state:
                if (State == PlayerSessionState.Seated && state.Room == CurrentRoomName)
                {
                    CurrentState = state;
                    StateReceived?.Invoke(state);
                }

                break;

            case LeftRecord left:
                ReturnToLobby(left.Reason);
                break;

            case ErrorRecord error:
                ErrorReceived?.Invoke(error.Message);
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private void OnDisconnected()
    {
        if (State == PlayerSessionState.Seated && CurrentRoomName is not null)
        {
            EnterReconnecting();
        }
        else if (State != PlayerSessionState.Reconnecting)
        {
            State = PlayerSessionState.Disconnected;
            ErrorReceived?.Invoke("Connection lost.");
        }
    }

    private void EnterReconnecting()
    {
        State = PlayerSessionState.Reconnecting;
        ReconnectingStarted?.Invoke();
        LogReceived?.Invoke("Connection lost - rejoining...");
        _ = ReconnectLoopAsync(_disposal.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + _reconnectBudget;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                IClientTransport transport = await _connectFactory().ConfigureAwait(false);
                AttachTransport(transport);
                await transport.SendLineAsync(GameJson.Serialize(new JoinRoomRecord(CurrentRoomName!)), CancellationToken.None).ConfigureAwait(false);
                return; // the joined acknowledgement completes the transition
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
            {
                try
                {
                    await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ReturnToLobby("reconnectFailed");
        ErrorReceived?.Invoke("Could not rejoin the room in time.");
    }

    private void ReturnToLobby(string reason)
    {
        State = PlayerSessionState.Lobby;
        CurrentRoomName = null;
        MyMark = null;
        CurrentState = null;
        ReturnedToLobby?.Invoke(reason);
    }

    private async Task SendAsync(GameEnvelope message)
    {
        IClientTransport? transport = _transport;
        if (transport is null)
        {
            throw new InvalidOperationException("The session is not connected.");
        }

        try
        {
            await transport.SendLineAsync(GameJson.Serialize(message)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
        {
            // Broken pipe: the Disconnected event drives recovery.
        }
    }

    public void Dispose()
    {
        _disposal.Cancel();
        DetachTransport();
        (_transport as IDisposable)?.Dispose();
        _transport = null;
    }
}
