using System.Collections.ObjectModel;
using System.IO;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class GameViewModel : ObservableObject
{
    private readonly PlayerSession _session;
    private readonly IUiDispatcher _ui;
    private GameStateRecord? _rendered;

    public ObservableCollection<CellViewModel> Cells { get; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = "Joining...";
    [ObservableProperty] public partial string TitleText { get; set; } = "Tic-Tac-Toe";
    [ObservableProperty] public partial string OutputText { get; set; } = "";
    [ObservableProperty] public partial string RematchLabel { get; set; } = "Offer Rematch";
    [ObservableProperty] public partial bool RematchEnabled { get; set; }

    public GameViewModel(PlayerSession session, IUiDispatcher ui)
    {
        _session = session;
        _ui = ui;

        for (int i = 0; i < 9; i++)
        {
            Cells.Add(new CellViewModel(i));
        }

        session.StateReceived += state => _ui.Post(() => ApplyState(state));
        session.Seated += joined => _ui.Post(() =>
        {
            if (joined.Restored)
            {
                AppendOutput("Reconnected — your seat was restored.");
            }

            ApplyState(joined.State);
        });
        session.ErrorReceived += message => AppendOutput(message);
        session.LogReceived += AppendOutput;
        session.ReconnectingStarted += () => _ui.Post(ShowBanner);

        if (session.CurrentState is { } initial)
        {
            ApplyState(initial);
        }
    }

    private void ShowBanner()
    {
        StatusText = "Connection lost — rejoining...";
        foreach (CellViewModel cell in Cells)
        {
            cell.IsEnabled = false;
        }

        RematchEnabled = false;
    }

    private void ApplyState(GameStateRecord state)
    {
        _rendered = state;
        string? myMark = _session.MyMark;
        bool spectator = myMark is null;
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && !spectator && state.Turn == myMark;

        TitleText = $"Tic-Tac-Toe — {state.Room}" + DescribeSeats(state, myMark, spectator);

        for (int i = 0; i < Cells.Count; i++)
        {
            Cells[i].Mark = state.Board[i];
            Cells[i].IsEnabled = myTurn && state.Board[i] == "";
            Cells[i].IsHighlighted = false;
        }

        if (state.WinningLine is not null)
        {
            foreach (int cell in state.WinningLine)
            {
                Cells[cell].IsHighlighted = true;
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

        if (RematchOfferedByOpponent(state) is { } challenger)
        {
            status += $"{Environment.NewLine}{challenger} offers a rematch.";
        }

        StatusText = status;
        RefreshRematch(state);
    }

    private void RefreshRematch(GameStateRecord state)
    {
        bool decided = state.Status is "won" or "draw";
        bool player = !_session.IsSpectator;
        bool mine = decided && player && state.RematchOfferedBy == _session.MyMark;
        RematchEnabled = decided && player && !mine;
        RematchLabel = mine
            ? "Rematch offered..."
            : RematchOfferedByOpponent(state) is not null ? "Accept Rematch"
            : "Offer Rematch";
    }

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

    private void AppendOutput(string message) => OutputText += message + Environment.NewLine;

    private bool MoveCanExecute(int cell) =>
        _rendered is { Status: "inProgress" }
        && !_session.IsSpectator
        && _session.MyMark is { } myMark
        && _rendered.Turn == myMark
        && cell is >= 0 and < 9
        && Cells[cell].IsEnabled;

    [RelayCommand(CanExecute = nameof(MoveCanExecute))]
    private async Task MoveAsync(int cell)
    {
        await SafeCallAsync(() => _session.PlayCellAsync(cell), "Move failed");
    }

    [RelayCommand]
    private async Task OfferRematchAsync()
    {
        await SafeCallAsync(() => _session.SendRematchOfferAsync(), "Rematch failed");
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        await SafeCallAsync(() => _session.LeaveRoomAsync(), "Leave failed");
    }

    private async Task SafeCallAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                        or IOException or System.Net.Sockets.SocketException)
        {
            AppendOutput($"{failurePrefix}: {ex.Message}");
        }
    }
}
