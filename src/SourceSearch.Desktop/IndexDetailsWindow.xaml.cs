using System.Windows;
using SourceSearch.Presentation;
namespace SourceSearch.Desktop;
public partial class IndexDetailsWindow : Window
{
    public IndexDetailsWindow(SearchViewModel model)
    {
        InitializeComponent();
        var report = model.Report ?? throw new InvalidOperationException("尚无索引详情。");
        DataContext = new { Source = model.SnapshotSource, Summary = $"{report.Files.Count:N0} 个文件 · {report.IndexedLines:N0} 行 · 跳过 {report.Issues.Count:N0} 项 · 索引 {report.IndexBytes / 1048576d:N1} MiB", Issues = report.Issues.Select(i => new { i.Path, Reason = Translate(i.Reason) }).ToArray() };
    }
    private static string Translate(string reason) => reason switch
    {
        "SubmoduleNotInitialized" => "子模块未初始化", "UnreadableDirectory" => "目录不可读取",
        "UnreadableOrMissing" => "文件不可读取或不存在", "GitUnavailableIgnoreRulesNotApplied" => "Git不可用，未应用忽略规则",
        "TooLarge" => "文件超过大小限制", "InvalidEncoding" => "不支持的编码或非法字节", "ChangedDuringRead" => "读取期间文件发生变化",
        "Missing" => "文件已不存在", "ReparsePoint" => "跳过链接文件", "Binary" => "二进制文件", "AccessDenied" => "无读取权限", "ReadError" => "文件读取错误", "Unreadable" => "文件不可读取",
        _ => reason
    };
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
