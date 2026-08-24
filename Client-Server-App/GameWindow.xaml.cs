using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Tic-tac-toe board for either role. Construct with exactly one service;
/// the caller is responsible for creating the service and calling Start().
/// </summary>
public partial class GameWindow : Window
{
    private readonly Button[] _cells = new Button[9];
    private readonly TicTacToeClientService? _client;
    private readonly Brush _defaultCellBackground = Brushes.White;
    private GameStateRecord? _renderedState;
    private bool _rematchOfferedLocally;
    private bool _opponentLeft;

    internal GameWindow(TicTacToeClientService client)
    {
        _client = client;
        InitializeComponent();
        CreateCells();
        client.StateChanged += OnStateChanged;
        client.LogReceived += message => AppendLog(message);
        client.RematchRequested += OnRematchRequested;
        client.OpponentDisconnected += OnOpponentDisconnected;
        Title = "Tic-Tac-Toe (Client)";
        Render(null);
    }

    private void CreateCells()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            Button cell = new()
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Background = _defaultCellBackground,
                IsEnabled = false,
                Tag = i,
            };
            cell.Click += CellButton_Click;
            _cells[i] = cell;
            BoardGrid.Children.Add(cell);
        }
    }

    private async void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (_renderedState is not { Status: "inProgress" })
        {
            return;
        }

        string myMark = MyMark(_renderedState.Round);
        if (_renderedState.Turn != myMark)
        {
            return;
        }

        int cell = (int)((Button)sender).Tag!;
        try
        {
            if (_client is not null)
            {
                await _client.PlayCellAsync(cell);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or System.Net.Sockets.SocketException)
        {
            AppendLog($"Move failed: {ex.Message}");
        }
    }

    private async void RematchButton_Click(object sender, RoutedEventArgs e)
    {
        _rematchOfferedLocally = true;
        RefreshRematchButton();
        try
        {
            if (_client is not null)
            {
                await _client.SendRematchOfferAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or System.Net.Sockets.SocketException)
        {
            AppendLog($"Rematch failed: {ex.Message}");
        }
    }

    private void OnStateChanged(GameStateRecord state) =>
        Dispatcher.BeginInvoke(() => Render(state));

    private void OnRematchRequested() =>
        Dispatcher.BeginInvoke(() =>
        {
            AppendLog("Opponent wants a rematch.");
            RefreshRematchButton();
        });

    private void OnOpponentDisconnected() =>
        Dispatcher.BeginInvoke(() =>
        {
            _opponentLeft = true;
            StatusText.Text = "Opponent disconnected.";
            foreach (Button cell in _cells)
            {
                cell.IsEnabled = false;
            }

            RematchButton.IsEnabled = false;
        });

    private void Render(GameStateRecord? state)
    {
        if (state is null)
        {
            StatusText.Text = "Waiting for opponent...";
            return;
        }

        if (_renderedState is { } previous && state.Round > previous.Round)
        {
            _rematchOfferedLocally = false;
        }

        _renderedState = state;
        string myMark = MyMark(state.Round);
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && state.Turn == myMark;

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].Content = state.Board[i] == "" ? "" : state.Board[i];
            _cells[i].IsEnabled = myTurn && state.Board[i] == "";
            _cells[i].Background = _defaultCellBackground;
        }

        if (state.WinningLine is not null)
        {
            foreach (int cell in state.WinningLine)
            {
                _cells[cell].Background = Brushes.LightGoldenrodYellow;
            }
        }

        StatusText.Text = state.Status switch
        {
            "won" when state.Winner == myMark => $"You win! ({state.Winner})",
            "won" => $"You lose. ({state.Winner} wins)",
            "draw" => "It's a draw.",
            _ when _opponentLeft => "Opponent disconnected.",
            _ when myTurn => $"Your move ({myMark}).",
            _ => "Opponent's move.",
        };

        RefreshRematchButton(inProgress);
    }

    private void RefreshRematchButton(bool inProgress = false)
    {
        bool gameOver = _renderedState is not null && !inProgress;
        RematchButton.IsEnabled = gameOver && !_rematchOfferedLocally;
        RematchButton.Content = _rematchOfferedLocally ? "Rematch offered..." : "Offer Rematch";
    }

    private string MyMark(int round) => GameRoles.ClientMark(round);

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    protected override void OnClosed(EventArgs e)
    {
        if (_client is not null)
        {
            _client.StateChanged -= OnStateChanged;
            _client.RematchRequested -= OnRematchRequested;
            _client.OpponentDisconnected -= OnOpponentDisconnected;
        }

        base.OnClosed(e);
    }
}
