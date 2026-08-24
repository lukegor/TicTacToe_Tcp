using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Client-side session: renders authoritative states, sends move and rematch
/// requests. Performs no local rule enforcement.
/// </summary>
internal sealed class TicTacToeClientService
{
    private readonly IClientTransport _client;
    private int _round;

    public event Action<GameStateRecord>? StateChanged;
    public event Action<string>? LogReceived;
    public event Action? RematchRequested;
    public event Action? OpponentDisconnected;

    public GameStateRecord? CurrentState { get; private set; }

    public TicTacToeClientService(IClientTransport client) => _client = client;

    public void Start()
    {
        _client.MessageReceived += OnLineReceived;
        _client.Disconnected += () => OpponentDisconnected?.Invoke();
    }

    public async Task PlayCellAsync(int cell) =>
        await SendAsync(new MoveRequestRecord(cell)).ConfigureAwait(false);

    public async Task SendRematchOfferAsync() =>
        await SendAsync(new RematchOfferRecord()).ConfigureAwait(false);

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case GameStateRecord state:
                if (CurrentState is { } previous && state.Round < previous.Round)
                {
                    break; // stale replay from a previous round
                }

                _round = state.Round;
                CurrentState = state;
                StateChanged?.Invoke(state);
                break;

            case RematchOfferRecord:
                RematchRequested?.Invoke();
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private async Task SendAsync(GameEnvelope message)
    {
        try
        {
            await _client.SendLineAsync(GameJson.Serialize(message)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
        {
            // Connection gone or not established; UI learns via OpponentDisconnected.
        }
    }
}
