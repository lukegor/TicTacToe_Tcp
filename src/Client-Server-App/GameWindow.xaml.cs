using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClientServer.Core.Game;

namespace ClientServer.App;

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

        // The Seated notification can precede this window's creation (the lobby
        // opens the window only after the event fires), so the initial snapshot
        // must be replayed from the session or the board sits at "Joining..."
        // until the next server broadcast.
        if (_session.CurrentState is { } initialState)
        {
            Render(initialState);
        }
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
        if (joined.Restored)
        {
            AppendLog("Reconnected — your seat was restored.");
        }

        Render(joined.State);
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
        _renderedState = state;
        string? myMark = _session.MyMark;
        bool spectator = myMark is null;
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && !spectator && state.Turn == myMark;

        Title = $"Tic-Tac-Toe — {state.Room}" + DescribeSeats(state, myMark, spectator);

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

        string status = (spectator ? "[Spectating] " : string.Empty) + state.Status switch
        {
            "waiting" => "Waiting for an opponent to join...",
            "won" when state.WinnerReason == "forfeit" => $"{state.Winner} wins by forfeit.",
            "won" when state.Winner == myMark => "You win!",
            "won" when spectator => $"{state.Winner} wins!",
            "won" => "You lose.",
            "draw" => "It's a draw.",
            _ when myTurn => $"Your move ({myMark}).",
            _ when spectator => $"{state.Turn}'s move.",
            _ => "Opponent's move.",
        };

        // The opponent has voted for a rematch and this client has not: make the
        // challenge unmistakable, both here and on the rematch button.
        if (RematchOfferedByOpponent(state) is { } challenger)
        {
            status += $"{Environment.NewLine}{challenger} offers a rematch.";
        }

        StatusText.Text = status;
        RefreshRematchButton(state);
    }

    private void RefreshRematchButton(GameStateRecord state)
    {
        bool decided = state.Status is "won" or "draw"; // "waiting" is not a finished game
        bool player = !_session.IsSpectator;
        bool mine = decided && player && state.RematchOfferedBy == _session.MyMark;
        RematchButton.IsEnabled = decided && player && !mine;
        RematchButton.Content = mine
            ? "Rematch offered..."
            : RematchOfferedByOpponent(state) is not null ? "Accept Rematch"
            : "Offer Rematch";
    }

    /// <summary>Name of the seat that requested the outstanding rematch, or null
    /// when there is no offer or it came from this client.</summary>
    private string? RematchOfferedByOpponent(GameStateRecord state) =>
        state.RematchOfferedBy is { } mark && mark != _session.MyMark ? SeatName(state, mark) : null;

    private static string SeatName(GameStateRecord state, string mark) =>
        mark == "X" ? state.XName ?? "X" : state.OName ?? "O";

    private static string DescribeSeats(GameStateRecord state, string? myMark, bool spectator)
    {
        if (state.XName is not null && state.OName is not null)
        {
            return $" — {state.XName} (X) vs {state.OName} (O)";
        }

        return spectator ? " (spectator)" : $" ({myMark})";
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
        _ = _session.LeaveRoomAsync();
        base.OnClosed(e);
    }
}
