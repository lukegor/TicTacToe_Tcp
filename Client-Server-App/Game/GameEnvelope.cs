using System.Text.Json.Serialization;

namespace Client_Server_App.Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MoveRequestRecord), "moveRequest")]
[JsonDerivedType(typeof(RematchOfferRecord), "rematchOffer")]
[JsonDerivedType(typeof(GameStateRecord), "state")]
internal abstract record GameEnvelope;

internal sealed record MoveRequestRecord(int Cell) : GameEnvelope;

internal sealed record RematchOfferRecord : GameEnvelope;

internal sealed record GameStateRecord(
    IReadOnlyList<string> Board,
    string Turn,
    string Status,
    string? Winner,
    IReadOnlyList<int>? WinningLine,
    int Round) : GameEnvelope;
