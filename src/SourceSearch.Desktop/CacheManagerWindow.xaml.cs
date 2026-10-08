using System.Windows;
using SourceSearch.Core;
namespace SourceSearch.Desktop;
public partial class CacheManagerWindow : Window
{
    private readonly IIndexCache cache;
    private readonly CancellationTokenSource lifetime = new();
    private bool closed, busy;
    public CacheManagerWindow(IIndexCache cache) { this.cache=cache;InitializeComponent(); }
    private void Busy(bool value) {busy=value;RefreshButton.IsEnabled=!value;DeleteButton.IsEnabled=!value;}
    public async Task RefreshAsync()
    {
        if(busy||closed)return;Busy(true);StatusText.Text="正在读取缓存…";
        try
        {
            var entries=await cache.ListAsync(lifetime.Token);
            if(!closed){EntriesGrid.ItemsSource=entries;StatusText.Text=$"{entries.Count} 个缓存。删除将移除所选分支缓存及其历史快照。";}
        }
        catch(OperationCanceledException) when(closed) { }
        catch(Exception error) {if(!closed)StatusText.Text="读取失败："+error.Message;}
        finally {if(!closed)Busy(false);}
    }
    private async void Window_Loaded(object sender,RoutedEventArgs e)=>await RefreshAsync();
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await RefreshAsync();
    private async void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(busy||EntriesGrid.SelectedItem is not CacheEntryInfo entry)return;
        if(MessageBox.Show(this,$"删除这个缓存及其历史快照？\n{entry.SourceRoot}\n{entry.Branch}","删除索引缓存",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        Busy(true);bool deleted=false;
        try {await cache.DeleteAsync(entry.Key,lifetime.Token);deleted=true;}
        catch(OperationCanceledException) when(closed) { }
        catch(Exception error) {if(!closed)StatusText.Text="删除失败："+error.Message;}
        finally {if(!closed)Busy(false);}
        if(deleted&&!closed)await RefreshAsync();
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void Window_Closed(object? sender,EventArgs e){closed=true;lifetime.Cancel();}
}
