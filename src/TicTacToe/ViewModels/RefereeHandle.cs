using TicTacToe.Core.Game;
using TicTacToe.Core.Transports;

namespace TicTacToe.ViewModels;

internal sealed record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby);
