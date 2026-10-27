using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SourceSearch.Presentation;
namespace SourceSearch.Desktop;

public partial class MainWindow
{
    private void OpenResultFile(ResultRow row)
    {
        var editor = GetVsCodeStartInfo(row);
        if (editor is not null)
        {
            try { startFile(editor); return; }
            catch (Exception error) when (IsOpenError(error)) { /* Retry with the normal file association. */ }
        }
        startFile(new ProcessStartInfo(row.FilePath) { UseShellExecute = true });
    }

    private ProcessStartInfo? GetVsCodeStartInfo(ResultRow row)
    {
        try
        {
            string? executable = resolveDefaultEditor(row.FilePath);
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)) return null;
            string name = Path.GetFileName(executable);
            if (!name.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)) return null;
            var info = new ProcessStartInfo(executable) { UseShellExecute = false };
            info.ArgumentList.Add("--reuse-window");
            info.ArgumentList.Add("--goto");
            info.ArgumentList.Add(row.FilePath + ":" + row.LineNumber.ToString(CultureInfo.InvariantCulture));
            return info;
        }
        catch (Exception error) when (IsOpenError(error) || error is COMException or System.Security.SecurityException or
            NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        { return null; } // Optional association detection must never prevent ordinary opening.
    }

    private static bool IsOpenError(Exception error) =>
        error is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException or ArgumentException;
}
