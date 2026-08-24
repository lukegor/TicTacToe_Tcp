using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Authoritative tic-tac-toe session for the host. Validates remote move requests,
/// owns round/rematch progression, and broadcasts state after every change.
/// </summary>
internal sealed class TicTacToeHostService
{
    private readonly IServerTransport _server;
    private readonly TicTacToe _game = new();
    private readonly object _sync = new();
    private int _round = 1;
    private bool _hostWantsRematch;
    private bool _clientWantsRematch;

    public event Action<GameStateRecord>? StateChanged;
    public event Action<string>? LogReceived;
    public event Action? RematchRequested;
    public event Action? OpponentDisconnected;

    public int Round => _round;
    public GameStateRecord? CurrentState { get; private set; }

    public TicTacToeHostService(IServerTransport server) => _server = server;

    public void Start()
    {
        _server.MessageReceived += OnLineReceived;
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _ = PublishStateAsync();
    }

    public async Task PlayMoveAsync(int cell)
    {
        lock (_sync)
        {
            _ = _game.TryApplyMove(cell, ParseMark(GameRoles.HostMark(_round)));
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    public async Task RequestRematchAsync()
    {
        bool start;
        lock (_sync)
        {
            start = _clientWantsRematch;
            if (!start)
            {
                _hostWantsRematch = true;
            }
        }

        if (start)
        {
            await StartNextRoundAsync().ConfigureAwait(false);
            return;
        }

        await SendAsync(new RematchOfferRecord()).ConfigureAwait(false);
    }

    private void OnClientConnected() => _ = PublishStateAsync();

    private void OnClientDisconnected() => OpponentDisconnected?.Invoke();

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case MoveRequestRecord request:
                _ = ApplyRemoteMoveAsync(request.Cell);
                break;

            case RematchOfferRecord:
                HandleRematchOffer();
                break;

            case GameStateRecord:
                // Hosts never consume states.
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private async Task ApplyRemoteMoveAsync(int cell)
    {
        lock (_sync)
        {
            _ = _game.TryApplyMove(cell, ParseMark(GameRoles.ClientMark(_round)));
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    private void HandleRematchOffer()
    {
        bool start;
        lock (_sync)
        {
            _clientWantsRematch = true;
            start = _hostWantsRematch;
        }

        if (start)
        {
            _ = StartNextRoundAsync();
        }
        else
        {
            RematchRequested?.Invoke();
        }
    }

    private async Task StartNextRoundAsync()
    {
        lock (_sync)
        {
            _round++;
            _hostWantsRematch = false;
            _clientWantsRematch = false;
            _game.Reset(Player.X);
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    private async Task PublishStateAsync()
    {
        GameStateRecord state = BuildState();
        CurrentState = state;
        StateChanged?.Invoke(state);
        try
        {
            await _server.BroadcastLineAsync(GameJson.Serialize(state)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Receiver vanished; state remains available locally.
        }
    }

    private async Task SendAsync(GameEnvelope message)
    {
        try
        {
            await _server.BroadcastLineAsync(GameJson.Serialize(message)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Receiver vanished.
        }
    }

    private GameStateRecord BuildState()
    {
        lock (_sync)
        {
            return new GameStateRecord(
                Board: _game.Board.Select(CellToString).ToArray(),
                Turn: _game.Turn.ToString(),
                Status: _game.Status switch
                {
                    GameStatus.Won => "won",
                    GameStatus.Draw => "draw",
                    _ => "inProgress",
                },
                Winner: _game.Winner?.ToString(),
                WinningLine: _game.WinningLine?.ToArray(),
                Round: _round);
        }
    }

    private static Player ParseMark(string mark) => mark == "X" ? Player.X : Player.O;

    private static string CellToString(Player? cell) => cell switch
    {
        Player.X => "X",
        Player.O => "O",
        _ => "",
    };
}
