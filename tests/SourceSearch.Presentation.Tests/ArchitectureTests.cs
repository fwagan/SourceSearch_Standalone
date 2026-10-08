using System.Xml.Linq;
namespace SourceSearch.Presentation.Tests;
public sealed class ArchitectureTests
{
    [Fact]
    public void Desktop_dependency_graph_excludes_validation_projects()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SourceSearch.slnx"))) root = Directory.GetParent(root)!.FullName;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Check(string project)
        {
            if (!visited.Add(project)) return;
            Assert.DoesNotContain("Validation", Path.GetFileName(project));
            var xml = XDocument.Load(project);
            foreach (var reference in xml.Descendants("ProjectReference"))
                Check(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")!.Value)));
        }
        Check(Path.Combine(root, "src/SourceSearch.Desktop/SourceSearch.Desktop.csproj"));
    }
}
