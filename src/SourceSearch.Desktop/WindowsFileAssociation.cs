using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace SourceSearch.Desktop;

/// <summary>Reads the effective user default without changing file associations.</summary>
public static class WindowsFileAssociation
{
    private static readonly Guid RegistrationClass = new("591209c7-767b-42b2-9fba-44ee4615f2c7");
    private const uint FixedFileProgIdNoFixups = 0x800 | 0x2000 | 0x100;
    private const uint Executable = 2;

    public static string? FindExecutable(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension)) return null;
        object? registration = null;
        IntPtr association = IntPtr.Zero;
        try
        {
            registration = Activator.CreateInstance(Type.GetTypeFromCLSID(RegistrationClass)!);
            // AT_FILEEXTENSION=0, AL_EFFECTIVE=1: use the same default as ShellExecute.
            if (registration is not IAssociationRegistration query ||
                query.QueryCurrentDefault(extension, 0, 1, out association) != 0 || association == IntPtr.Zero) return null;
            string? progId = Marshal.PtrToStringUni(association);
            if (string.IsNullOrWhiteSpace(progId)) return null;
            uint length = 0;
            int result = AssocQueryStringW(FixedFileProgIdNoFixups, Executable, progId, null, null, ref length);
            if (result < 0 || length == 0 || length > 32768) return null;
            var output = new StringBuilder((int)length);
            return AssocQueryStringW(FixedFileProgIdNoFixups, Executable, progId, null, output, ref length) == 0 ? output.ToString() : null;
        }
        finally
        {
            if (association != IntPtr.Zero) Marshal.FreeCoTaskMem(association);
            if (registration is not null) Marshal.ReleaseComObject(registration);
        }
    }

    [ComImport, Guid("4e530b0a-e611-4c77-a3ac-9031d022281b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssociationRegistration
    {
        [PreserveSig]
        int QueryCurrentDefault([MarshalAs(UnmanagedType.LPWStr)] string query, int associationType, int associationLevel, out IntPtr association);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(uint flags, uint query, string association, string? extra, StringBuilder? output, ref uint length);
}
