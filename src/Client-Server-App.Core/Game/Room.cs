namespace Client_Server_App.Game;

/// <summary>
/// One named tic-tac-toe room: two player seats, spectators, authoritative game
/// state, disconnect grace. Emits every outgoing line through the injected
/// <paramref name="sendTo"/> delegate; owns no sockets — only its own
/// cancellable grace delay.
/// </summary>
internal sealed class Room : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Guid> _spectators = [];
    private readonly Action<Guid, string> _sendTo;
    private readonly Action<string> _log;
    private readonly TimeSpan _gracePeriod;
    private readonly TicTacToe _game = new();
    private CancellationTokenSource? _graceCts;
    private Guid? _playerX;
    private Guid? _playerO;
    private string? _xName;
    private string? _oName;
    private bool _xWantsRematch;
    private bool _oWantsRematch;
    private string? _graceDroppedMark;
    private string? _winnerReason;
    private int _round = 1;
    private bool _closed;

    public event Action? MembershipChanged;
    public event Action<Guid>? MemberReleased;
    public event Action<IReadOnlyList<Guid>>? RoomClosed;

    public Room(string name, Action<Guid, string> sendTo, Action<string> log, TimeSpan gracePeriod)
    {
        Name = name;
        _sendTo = sendTo;
        _log = log;
        _gracePeriod = gracePeriod;
    }

    public string Name { get; }

    public int PlayerCount
    {
        get
        {
            lock (_sync)
            {
                return PlayerCountCore();
            }
        }
    }

    public int SpectatorCount
    {
        get
        {
            lock (_sync)
            {
                return _spectators.Count;
            }
        }
    }

    public bool HasVacancy
    {
        get
        {
            lock (_sync)
            {
                return _playerX is null || _playerO is null;
            }
        }
    }

    /// <summary>Seats <paramref name="id"/> in the first vacant slot (X, then O).</summary>
    public void Seat(Guid id, string displayName)
    {
        string mark;
        bool restored;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            if (_playerX is null)
            {
                _playerX = id;
                _xName = displayName;
                mark = "X";
            }
            else
            {
                _playerO = id;
                _oName = displayName;
                mark = "O";
            }

            restored = _graceCts is not null;
            CancelGraceCore();
        }

        SendTo(id, new JoinedRecord(Name, mark, restored, SnapshotCore()));
        NotifyMembershipChanged();

        // Broadcast whenever both seats are filled: fresh games announce their
        // start, and restored seats push the returning player's name to the room.
        bool bothSeated;
        lock (_sync)
        {
            bothSeated = _playerX is not null
                && _playerO is not null
                && _game.Status == GameStatus.InProgress;
        }

        if (bothSeated)
        {
            BroadcastState();
        }
    }

    public void AddSpectator(Guid id)
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _spectators.Add(id);
        }

        SendTo(id, new JoinedRecord(Name, null, Restored: false, SnapshotCore()));
        NotifyMembershipChanged();
    }

    public bool Contains(Guid id)
    {
        lock (_sync)
        {
            return IsMemberCore(id);
        }
    }

    public void HandleMove(Guid id, int cell)
    {
        string? line;
        List<Guid> recipients;
        lock (_sync)
        {
            string? mark = MarkOfCore(id);
            if (mark is null || _closed)
            {
                return;
            }

            // A solo occupant cannot pre-play: the game exists only once both
            // seats are taken.
            if (PlayerCountCore() < 2)
            {
                return;
            }

            _ = _game.TryApplyMove(cell, ParseMark(mark));
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    public void HandleRematchOffer(Guid id)
    {
        bool restart;
        lock (_sync)
        {
            if (_closed || _game.Status == GameStatus.InProgress)
            {
                return;
            }

            if (id == _playerX)
            {
                _xWantsRematch = true;
            }
            else if (id == _playerO)
            {
                _oWantsRematch = true;
            }
            else
            {
                return;
            }

            restart = _xWantsRematch && _oWantsRematch;
        }

        // A lone vote must be visible: the opponent learns they are being
        // challenged through the next snapshot's RematchOfferedBy field.
        if (restart)
        {
            StartNextRound();
        }
        else
        {
            BroadcastState();
        }
    }

    public void HandleChat(Guid id, string line)
    {
        List<Guid> recipients;
        lock (_sync)
        {
            if (!IsMemberCore(id))
            {
                return;
            }

            recipients = MembersCore();
        }

        foreach (Guid recipient in recipients)
        {
            _sendTo(recipient, line);
        }
    }

    /// <summary>Explicit leave. A seated player with an opponent present forfeits immediately.</summary>
    public void HandleLeave(Guid id)
    {
        string? guiltyMark;
        lock (_sync)
        {
            guiltyMark = OccupiedMarkCore(id);
        }

        if (guiltyMark is not null && OpponentPresentCore())
        {
            AwardForfeit(guiltyMark);
        }

        Release(id, notifySelf: true);
    }

    /// <summary>Unsolicited transport loss. Players trigger the grace flow.</summary>
    public void HandleDisconnect(Guid id)
    {
        string? droppedMark;
        bool opponentPresent;
        lock (_sync)
        {
            droppedMark = OccupiedMarkCore(id);
            opponentPresent = PlayerCountCore() == 2;
        }

        if (droppedMark is null)
        {
            Release(id, notifySelf: false);
            return;
        }

        FreeSeat(droppedMark);

        bool closeNow;
        lock (_sync)
        {
            closeNow = PlayerCountCore() == 0;
        }

        if (closeNow)
        {
            RequestClose("roomClosed");
            return;
        }

        if (opponentPresent)
        {
            StartGrace(droppedMark);
        }

        NotifyMembershipChanged();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            CancelGraceCore();
        }
    }

    private void StartGrace(string droppedMark)
    {
        CancellationToken token;
        lock (_sync)
        {
            CancelGraceCore();
            _graceDroppedMark = droppedMark;
            _graceCts = new CancellationTokenSource();
            token = _graceCts.Token;
        }

        _log($"[{Name}] A player lost connection. Grace period: {_gracePeriod.TotalSeconds:0.#}s.");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_gracePeriod, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string? guiltyMark;
            bool closeRoom;
            lock (_sync)
            {
                if (_closed || _graceCts is null)
                {
                    return; // restored or closed meanwhile
                }

                CancelGraceCore();
                guiltyMark = _graceDroppedMark;
                closeRoom = PlayerCountCore() == 0;
            }

            _log($"[{Name}] Grace period expired.");
            if (closeRoom || guiltyMark is null)
            {
                RequestClose("roomClosed");
                return;
            }

            AwardForfeit(guiltyMark);
        });
    }

    private void AwardForfeit(string guiltyMark)
    {
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            // Only an undecided game can be forfeited; a departure after the
            // result is in must not relabel it (e.g. a win becoming "by forfeit").
            if (_game.Status == GameStatus.InProgress)
            {
                _winnerReason = "forfeit";
                _game.DeclareForfeit(ParseMark(guiltyMark == "X" ? "O" : "X"));
            }

            FreeSeatCore(guiltyMark);
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    private void Release(Guid id, bool notifySelf)
    {
        bool wasSpectator;
        lock (_sync)
        {
            wasSpectator = _spectators.Remove(id);
            string? occupiedMark = OccupiedMarkCore(id);
            if (occupiedMark is not null)
            {
                FreeSeatCore(occupiedMark);
            }
        }

        if (notifySelf)
        {
            SendTo(id, new LeftRecord("left"));
        }

        if (notifySelf || wasSpectator)
        {
            MemberReleased?.Invoke(id);
        }

        bool closeRoom;
        lock (_sync)
        {
            closeRoom = PlayerCountCore() == 0 && _spectators.Count == 0;
        }

        if (closeRoom)
        {
            RequestClose("roomClosed");
        }
        else
        {
            NotifyMembershipChanged();
        }
    }

    private void StartNextRound()
    {
        List<(Guid Id, string Mark)> reseated;
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            (_playerX, _playerO) = (_playerO, _playerX);
            (_xName, _oName) = (_oName, _xName);
            _xWantsRematch = false;
            _oWantsRematch = false;
            _winnerReason = null;
            _round++;
            _game.Reset(Player.X);

            reseated = SeatsCore().Select(seat => (seat.Id, seat.Mark)).ToList();
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        foreach ((Guid id, string mark) in reseated)
        {
            SendTo(id, new JoinedRecord(Name, mark, Restored: false, ParseSnapshot(line)));
        }

        SendToEach(recipients, line);
    }

    private void BroadcastState()
    {
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    private void RequestClose(string reason)
    {
        List<Guid> members;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            CancelGraceCore();
            members = MembersCore();
            _playerX = null;
            _playerO = null;
            _spectators.Clear();
        }

        string line = GameJson.Serialize(new LeftRecord(reason));
        SendToEach(members, line);
        RoomClosed?.Invoke(members);
    }

    private void NotifyMembershipChanged() => MembershipChanged?.Invoke();

    private bool IsMemberCore(Guid id) =>
        _playerX == id || _playerO == id || _spectators.Contains(id);

    private int PlayerCountCore() => (_playerX is null ? 0 : 1) + (_playerO is null ? 0 : 1);

    private bool OpponentPresentCore() => PlayerCountCore() == 2;

    private string? OccupiedMarkCore(Guid id) =>
        _playerX == id ? "X" : _playerO == id ? "O" : null;

    private string? MarkOfCore(Guid id) => OccupiedMarkCore(id);

    private IEnumerable<(Guid Id, string Mark)> SeatsCore()
    {
        if (_playerX is { } x)
        {
            yield return (x, "X");
        }

        if (_playerO is { } o)
        {
            yield return (o, "O");
        }
    }

    private List<Guid> MembersCore()
    {
        List<Guid> members = [];
        foreach ((Guid id, _) in SeatsCore())
        {
            members.Add(id);
        }

        members.AddRange(_spectators);
        return members;
    }

    private void FreeSeat(string mark)
    {
        lock (_sync)
        {
            FreeSeatCore(mark);
        }
    }

    private void FreeSeatCore(string mark)
    {
        if (mark == "X")
        {
            _playerX = null;
            _xName = null;
            _xWantsRematch = false;
        }
        else if (mark == "O")
        {
            _playerO = null;
            _oName = null;
            _oWantsRematch = false;
        }
    }

    private GameStateRecord SnapshotCore() => ParseSnapshot(SerializeSnapshotCore());

    private string SerializeSnapshotCore() => GameJson.Serialize(BuildStateCore());

    private static GameStateRecord ParseSnapshot(string line) =>
        (GameStateRecord)GameJson.TryParse(line)!;

    /// <summary>
    /// Room-level status. A pristine room with a vacant seat is "waiting":
    /// the game has not begun, so no one is on turn yet.
    /// </summary>
    private string StatusCodeCore()
    {
        if (PlayerCountCore() < 2 && _game.Board.All(cell => cell is null))
        {
            return "waiting";
        }

        return _game.Status switch
        {
            GameStatus.Won => "won",
            GameStatus.Draw => "draw",
            _ => "inProgress",
        };
    }

    private GameStateRecord BuildStateCore() => new(
        Board: _game.Board.Select(CellToString).ToArray(),
        Turn: _game.Turn.ToString(),
        Status: StatusCodeCore(),
        Winner: _game.Winner?.ToString(),
        WinningLine: _game.WinningLine?.ToArray(),
        Round: _round,
        Room: Name,
        WinnerReason: _winnerReason,
        XName: _xName,
        OName: _oName,
        RematchOfferedBy: _xWantsRematch ? "X" : _oWantsRematch ? "O" : null);

    private void CancelGraceCore()
    {
        if (_graceCts is { } cts)
        {
            _graceCts = null;
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void SendTo(Guid id, GameEnvelope message) => _sendTo(id, GameJson.Serialize(message));

    private void SendToEach(IEnumerable<Guid> ids, string line)
    {
        foreach (Guid id in ids)
        {
            _sendTo(id, line);
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
