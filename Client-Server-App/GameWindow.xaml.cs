using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Tic-tac-toe board driven by a <see cref="PlayerSession"/>. Works for players
/// and spectators; handles the reconnecting banner and the Leave flow.
/// </summary>
public partial class GameWindow : Window
{
    private readonly Button[] _cells = new Button[9];
    private readonly PlayerSession _session;
    private readonly Brush _defaultCellBackground = Brushes.White;
    private GameStateRecord? _renderedState;
    private bool _rematchOfferedLocally;

    internal GameWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        CreateCells();
        session.StateReceived += OnStateReceived;
        session.Seated += OnSeatedInternal;
        session.ReturnedToLobby += OnReturnedToLobby;
        session.ErrorReceived += OnLogMessage;
        session.LogReceived += OnLogMessage;
        session.ReconnectingStarted += OnReconnectingStarted;
    }

    private void OnStateReceived(GameStateRecord state) =>
        Dispatcher.BeginInvoke(() => Render(state));

    private void OnSeatedInternal(JoinedRecord joined) =>
        Dispatcher.BeginInvoke(() => OnSeated(joined));

    private void OnReturnedToLobby(string reason) =>
        Dispatcher.BeginInvoke(Close);

    private void OnLogMessage(string message) =>
        Dispatcher.BeginInvoke(() => AppendLog(message));

    private void OnReconnectingStarted() =>
        Dispatcher.BeginInvoke(ShowReconnectBanner);

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

    private void OnSeated(JoinedRecord joined)
    {
        Title = $"Tic-Tac-Toe — {joined.Room}" + (joined.Mark is null ? " (spectator)" : $" ({joined.Mark})");

        if (_renderedState is null || joined.State.Round >= _renderedState.Round)
        {
            if (_renderedState is { } previous && joined.State.Round > previous.Round)
            {
                _rematchOfferedLocally = false;
            }

            _renderedState = joined.State;
        }

        if (joined.Restored)
        {
            AppendLog("Reconnected — your seat was restored.");
        }

        Render(_renderedState);
    }

    private void ShowReconnectBanner()
    {
        StatusText.Text = "Connection lost — rejoining...";
        foreach (Button cell in _cells)
        {
            cell.IsEnabled = false;
        }

        RematchButton.IsEnabled = false;
    }

    private async void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (_renderedState is not { Status: "inProgress" } || _session.IsSpectator)
        {
            return;
        }

        if (_session.MyMark is not { } myMark || _renderedState.Turn != myMark)
        {
            return;
        }

        int cell = (int)((Button)sender).Tag!;
        await SafeCallAsync(() => _session.PlayCellAsync(cell), "Move failed");
    }

    private async void RematchButton_Click(object sender, RoutedEventArgs e)
    {
        _rematchOfferedLocally = true;
        RefreshRematchButton();
        await SafeCallAsync(() => _session.SendRematchOfferAsync(), "Rematch failed");
    }

    private async void LeaveButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveButton.IsEnabled = false;
        await SafeCallAsync(() => _session.LeaveRoomAsync(), "Leave failed");

        // ReturnedToLobby closes this window; LeaveRoomAsync raises it locally.
    }

    private async Task SafeCallAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or SocketException)
        {
            AppendLog($"{failurePrefix}: {ex.Message}");
        }
    }

    private void Render(GameStateRecord state)
    {
        if (_renderedState is { } previous && state.Round > previous.Round)
        {
            _rematchOfferedLocally = false;
        }

        _renderedState = state;
        string? myMark = _session.MyMark;
        bool spectator = myMark is null;
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && !spectator && state.Turn == myMark;

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

        StatusText.Text = (spectator ? "[Spectating] " : string.Empty) + state.Status switch
        {
            "won" when state.WinnerReason == "forfeit" => $"{state.Winner} wins by forfeit.",
            "won" when state.Winner == myMark => "You win!",
            "won" when spectator => $"{state.Winner} wins!",
            "won" => "You lose.",
            "draw" => "It's a draw.",
            _ when myTurn => $"Your move ({myMark}).",
            _ when spectator => $"{state.Turn}'s move.",
            _ => "Opponent's move.",
        };

        RefreshRematchButton(inProgress);
    }

    private void RefreshRematchButton(bool inProgress = false)
    {
        bool gameOver = _renderedState is not null && !inProgress;
        bool player = !_session.IsSpectator;
        RematchButton.IsEnabled = gameOver && player && !_rematchOfferedLocally;
        RematchButton.Content = _rematchOfferedLocally ? "Rematch offered..." : "Offer Rematch";
    }

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    protected override void OnClosed(EventArgs e)
    {
        _session.StateReceived -= OnStateReceived;
        _session.Seated -= OnSeatedInternal;
        _session.ReturnedToLobby -= OnReturnedToLobby;
        _session.ErrorReceived -= OnLogMessage;
        _session.LogReceived -= OnLogMessage;
        _session.ReconnectingStarted -= OnReconnectingStarted;
        base.OnClosed(e);
    }
}
