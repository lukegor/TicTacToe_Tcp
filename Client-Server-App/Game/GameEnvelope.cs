using System.Text.Json.Serialization;

namespace Client_Server_App.Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MoveRequestRecord), "moveRequest")]
[JsonDerivedType(typeof(RematchOfferRecord), "rematchOffer")]
[JsonDerivedType(typeof(GameStateRecord), "state")]
[JsonDerivedType(typeof(CreateRoomRecord), "createRoom")]
[JsonDerivedType(typeof(JoinRoomRecord), "joinRoom")]
[JsonDerivedType(typeof(LeaveRoomRecord), "leaveRoom")]
[JsonDerivedType(typeof(RoomListRecord), "roomList")]
[JsonDerivedType(typeof(JoinedRecord), "joined")]
[JsonDerivedType(typeof(LeftRecord), "left")]
[JsonDerivedType(typeof(ErrorRecord), "error")]
internal abstract record GameEnvelope;

internal sealed record MoveRequestRecord(int Cell) : GameEnvelope;

internal sealed record RematchOfferRecord : GameEnvelope;

internal sealed record GameStateRecord(
    IReadOnlyList<string> Board,
    string Turn,
    string Status,
    string? Winner,
    IReadOnlyList<int>? WinningLine,
    int Round,
    string Room = "",
    string? WinnerReason = null) : GameEnvelope;

internal sealed record CreateRoomRecord(string Name) : GameEnvelope;

internal sealed record JoinRoomRecord(string Name) : GameEnvelope;

internal sealed record LeaveRoomRecord : GameEnvelope;

internal sealed record RoomInfoRecord(string Name, int Players, int Spectators) : GameEnvelope
{
    public string Label => $"{Players} player(s), {Spectators} spectator(s)";
}

internal sealed record RoomListRecord(IReadOnlyList<RoomInfoRecord> Rooms) : GameEnvelope;

/// <summary>Targeted acknowledgement after joining. A null <paramref name="Mark"/> means spectator.</summary>
internal sealed record JoinedRecord(string Room, string? Mark, bool Restored, GameStateRecord State) : GameEnvelope;

internal sealed record LeftRecord(string Reason) : GameEnvelope;

internal sealed record ErrorRecord(string Message) : GameEnvelope;
