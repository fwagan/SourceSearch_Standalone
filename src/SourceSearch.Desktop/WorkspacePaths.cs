using System.IO;
namespace SourceSearch.Desktop;
public static class WorkspacePaths
{
    public static string Root
    {
        get
        {
            string directory = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(directory, "SourceSearch.slnx")))
                directory = Directory.GetParent(directory)?.FullName ?? throw new DirectoryNotFoundException("SourceSearch.slnx not found above application.");
            return directory;
        }
    }
    public static string IndexRoot => Path.Combine(Root, "artifacts", "indexes", "ui");
    public static string CacheRoot => Path.Combine(Root, "artifacts", "indexes", "cache");
}
