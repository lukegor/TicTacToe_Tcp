using ClientServer.Core.Game;
using ClientServer.Core.Transports;

namespace ClientServer.App.ViewModels;

internal sealed record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby);
