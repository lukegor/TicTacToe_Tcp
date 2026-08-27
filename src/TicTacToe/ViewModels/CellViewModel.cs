using CommunityToolkit.Mvvm.ComponentModel;

namespace TicTacToe.ViewModels;

internal sealed partial class CellViewModel(int index) : ObservableObject
{
    public int CellIndex { get; } = index;

    [ObservableProperty] public partial string Mark { get; set; } = "";
    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
}
