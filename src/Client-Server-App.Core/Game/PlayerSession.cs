using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client_Server_App.Game;

/// <summary>
/// Client-side session: lobby interactions, seated gameplay, and automatic
/// reconnect-to-seat after unexpected transport loss.
/// </summary>
internal sealed class PlayerSession : IDisposable
{
    public static readonly TimeSpan DefaultReconnectBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeatAckTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShedSettleDelay = TimeSpan.FromMilliseconds(200);

    private readonly Func<Task<IClientTransport>> _connectFactory;
    private readonly TimeSpan _reconnectBudget;
    private readonly CancellationTokenSource _disposal = new();
    private readonly ILogger<PlayerSession> _logger;
    private IClientTransport? _transport;
    private bool _wasPlayerWhenDropped;
    private int _seatAckVersion;

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

    public PlayerSession(
        Func<Task<IClientTransport>> connectFactory,
        TimeSpan? reconnectBudget = null,
        string? displayName = null,
        ILogger<PlayerSession>? logger = null)
    {
        _connectFactory = connectFactory;
        _reconnectBudget = reconnectBudget ?? DefaultReconnectBudget;
        _logger = logger ?? NullLogger<PlayerSession>.Instance;
        DisplayName = ResolveDisplayName(displayName);
    }

    /// <summary>Name announced to the referee via <c>hello</c> on every connection
    /// (initial and reconnect); a guest name is generated when skipped.</summary>
    public string DisplayName { get; }

    public static string ResolveDisplayName(string? candidate)
    {
        string trimmed = (candidate ?? string.Empty).Trim();
        return trimmed.Length > 0 ? trimmed : $"Guest-{Random.Shared.Next(1000, 10000)}";
    }

    /// <summary>Opens the first connection. Throws when the server is unreachable.</summary>
    public async Task ConnectAsync()
    {
        IClientTransport transport = await _connectFactory().ConfigureAwait(true);
        AttachTransport(transport);
        transport.Start();
        await SendHelloAsync(transport).ConfigureAwait(true);
        State = PlayerSessionState.Lobby;
    }

    /// <summary>Announces the display name; must precede any room request so the
    /// referee can attribute the connection. TCP ordering guarantees delivery.</summary>
    private async Task SendHelloAsync(IClientTransport transport) =>
        await transport.SendLineAsync(GameJson.Serialize(new HelloRecord(DisplayName))).ConfigureAwait(false);

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
                _seatAckVersion++;
                Seated?.Invoke(joined);
                if (joined.Restored)
                {
                    _logger.LogInformation("Reconnected to '{Room}'.", joined.Room);
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
                if (State == PlayerSessionState.Reconnecting && left.Reason == "left")
                {
                    break; // echo of our own shed-leave; stay in the reconnect loop
                }

                ReturnToLobby(left.Reason);
                break;

            case ErrorRecord error:
                ErrorReceived?.Invoke(error.Message);
                break;

            case null:
                _logger.LogDebug("Non-protocol line received: {Line}", line);
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
        _wasPlayerWhenDropped = MyMark is not null;
        State = PlayerSessionState.Reconnecting;
        ReconnectingStarted?.Invoke();
        _logger.LogWarning("Connection lost while seated in '{Room}'; rejoining.", CurrentRoomName);
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
                transport.Start();
                await SendHelloAsync(transport).ConfigureAwait(false);

                // Only an ack confirming the intended seat settles the loop;
                // a spectator mis-seat returns false and we retry.
                bool settled = await SendJoinAndVerifyAsync(transport, cancellationToken).ConfigureAwait(false);
                if (settled)
                {
                    _wasPlayerWhenDropped = false;
                    return;
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
            {
                // Connection or handshake failed; retry until the budget expires.
            }

            try
            {
                await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Announce the failure before transitioning so observers that poll the
        // state never see the lobby without the accompanying error notice.
        ErrorReceived?.Invoke("Could not rejoin the room in time.");
        ReturnToLobby("reconnectFailed");
    }

    /// <summary>
    /// Sends <c>joinRoom</c> and waits for a FRESH seat acknowledgement (one that
    /// arrives after this call — stale seating from a previous attempt is ignored).
    /// A returning player who is seated as a spectator lost the ordering race
    /// against the server's disconnect processing — sheds the seat, signals retry.
    /// </summary>
    private async Task<bool> SendJoinAndVerifyAsync(IClientTransport transport, CancellationToken cancellationToken)
    {
        int baseline = _seatAckVersion;
        await transport.SendLineAsync(GameJson.Serialize(new JoinRoomRecord(CurrentRoomName!)), CancellationToken.None).ConfigureAwait(false);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + SeatAckTimeout;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            bool freshAck = _seatAckVersion != baseline;
            if (freshAck && State == PlayerSessionState.Seated)
            {
                if (_wasPlayerWhenDropped && MyMark is null)
                {
                    await transport.SendLineAsync(GameJson.Serialize(new LeaveRoomRecord()), CancellationToken.None).ConfigureAwait(false);
                    await Task.Delay(ShedSettleDelay, cancellationToken).ConfigureAwait(false);
                    return false;
                }

                return true;
            }

            if (State is PlayerSessionState.Lobby or PlayerSessionState.Disconnected)
            {
                return false;
            }

            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
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
            if (State is PlayerSessionState.Seated or PlayerSessionState.Reconnecting)
            {
                return; // Broken pipe: the Disconnected event drives recovery.
            }

            // Lobby-state requests must not vanish silently; the caller reports this.
            throw new InvalidOperationException("The connection to the server is down.", ex);
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
