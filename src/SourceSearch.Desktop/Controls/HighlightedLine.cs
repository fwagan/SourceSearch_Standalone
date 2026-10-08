using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SourceSearch.Presentation;
namespace SourceSearch.Desktop.Controls;

/// <summary>Created only for realized DataGrid rows. Uses the result's original, clipped match offsets.</summary>
public sealed class HighlightedLine : TextBlock
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(ResultRow), typeof(HighlightedLine), new PropertyMetadata(null, Changed));
    public ResultRow? Row { get => (ResultRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var block = (HighlightedLine)target; block.Inlines.Clear();
        if (args.NewValue is not ResultRow row) return;
        int end = 0;
        foreach (var match in row.Matches)
        {
            if (match.Start > end) block.Inlines.Add(new Run(row.Text[end..match.Start]));
            block.Inlines.Add(new Run(row.Text.Substring(match.Start, match.Length))
            { Background = (Brush)block.FindResource("MatchBackground"), Foreground = (Brush)block.FindResource("MatchForeground") });
            end = match.Start + match.Length;
        }
        if (end < row.Text.Length) block.Inlines.Add(new Run(row.Text[end..]));
        if (row.IsTruncated) block.Inlines.Add(new Run(" … [仅显示前 2000 字符]") { Foreground = (Brush)block.FindResource("AppMuted") });
    }
}
