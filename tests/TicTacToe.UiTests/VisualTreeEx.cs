using System.Windows;
using System.Windows.Media;

namespace TicTacToe.TestSupport;

public static class VisualTreeEx
{
    public static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is { } child)
            {
                if (child is T match)
                {
                    yield return match;
                }

                foreach (T nested in FindChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
