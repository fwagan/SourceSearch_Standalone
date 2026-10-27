using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SourceSearch.Desktop;
using SourceSearch.Desktop.Controls;
using SourceSearch.Presentation;
namespace SourceSearch.UiValidation;

internal static class ResultOpenSmoke
{
    internal static async Task<IReadOnlyList<string>> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "先选中.cs"), clicked = Path.Combine(directory, "双击 文件.cs");
        File.WriteAllText(first, "User"); File.WriteAllText(clicked, "User");
        var launches = new List<ProcessStartInfo>(); var errors = new List<string>(); bool fail = false;
        var attempts = new List<string>(); string? editor = @"C:\Other\Editor.exe";
        bool failResolution = false, failEditor = false;
        var window = new MainWindow(directory, enableCache: false,
            startFile: info =>
            {
                attempts.Add(info.FileName);
                if (fail || (failEditor && info.FileName == editor)) throw new Win32Exception(1155, "Unable to launch the application.");
                launches.Add(info);
            },
            reportFileError: errors.Add,
            resolveDefaultEditor: path => failResolution ? throw new System.Runtime.InteropServices.COMException("Association unavailable") : editor);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        window.Show();
        try
        {
            var grid = (DataGrid)window.FindName("ResultsGrid");
            window.Model.Results.Add(new(first, 1, "User", [new(0, 4)], false));
            window.Model.Results.Add(new(clicked, 42, "User", [new(0, 4)], false));
            grid.SelectedItem = window.Model.Results[0];
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); grid.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
            var cell = Descendants<DataGridCell>(row).First();
            void DoubleClick(object source, MouseButton button = MouseButton.Left) => grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = Control.MouseDoubleClickEvent, Source = source });
            DoubleClick(cell);
            Check(launches.Count == 1 && launches[0].FileName == clicked && launches[0].UseShellExecute && launches[0].Arguments.Length == 0 && launches[0].ArgumentList.Count == 0,
                "Double-click opens the clicked row via the default file association, preserving spaces and Unicode");
            DoubleClick(Descendants<TextBlock>(cell).First());
            DoubleClick(Descendants<HighlightedLine>(row).Single().Inlines.OfType<Run>().First());
            Check(launches.Count == 3 && launches.All(info => info.FileName == clicked), "Double-clicking rendered path text or a highlighted inline opens its result file");
            DoubleClick(cell, MouseButton.Right);
            DoubleClick(Descendants<DataGridColumnHeader>(grid).First()); DoubleClick(grid);
            Check(launches.Count == 3, "Right double-click, headers and blank grid do not open the selected result");
            File.Delete(clicked); DoubleClick(cell);
            Check(launches.Count == 3 && errors.Count == 1 && errors[0].Contains(clicked), "A deleted result reports its path without launching a program");
            File.WriteAllText(clicked, "User"); fail = true; DoubleClick(cell);
            Check(errors.Count == 2 && errors[1].Contains(clicked), "Default-program launch failure is reported without crashing");
            fail = false; DoubleClick(cell);
            Check(launches.Count == 4, "Opening another result still works after a launch failure");
            launches.Clear(); attempts.Clear(); errors.Clear();
            editor = @"C:\Program Files\Microsoft VS Code\Code.exe"; DoubleClick(cell);
            Check(launches.Count == 1 && launches[0].FileName == editor && !launches[0].UseShellExecute &&
                launches[0].ArgumentList.SequenceEqual(new[] { "--reuse-window", "--goto", clicked + ":42" }),
                "A VS Code default association opens the clicked result at its indexed line");
            launches.Clear(); editor = @"C:\VS Code Insiders\Code - Insiders.exe"; DoubleClick(cell);
            Check(launches.Count == 1 && launches[0].FileName == editor && launches[0].ArgumentList.Contains(clicked + ":42"),
                "A VS Code Insiders default association also supports line navigation");
            launches.Clear(); editor = null; DoubleClick(cell);
            Check(launches.Count == 1 && launches[0].FileName == clicked && launches[0].UseShellExecute && launches[0].ArgumentList.Count == 0,
                "An unrecognized association opens the original file without position arguments");
            launches.Clear(); failResolution = true; DoubleClick(cell);
            Check(launches.Count == 1 && launches[0].FileName == clicked && launches[0].ArgumentList.Count == 0 && errors.Count == 0,
                "Association lookup errors silently fall back to normal file opening");
            launches.Clear(); attempts.Clear(); failResolution = false; failEditor = true; editor = @"C:\VS Code\Code.exe"; DoubleClick(cell);
            Check(attempts.SequenceEqual(new[] { editor, clicked }) && launches.Count == 1 && launches[0].FileName == clicked &&
                launches[0].UseShellExecute && launches[0].ArgumentList.Count == 0 && errors.Count == 0,
                "Failure to start VS Code silently falls back to the system default open action");
            launches.Clear(); attempts.Clear(); fail = true; DoubleClick(cell);
            Check(attempts.SequenceEqual(new[] { editor, clicked }) && launches.Count == 0 && errors.Count == 1 && errors[0].Contains(clicked),
                "An error is reported only if both VS Code and the normal open action fail");
        }
        finally { await window.CloseAsync(); }
        return checks;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T found) yield return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
