using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PCL.Core.Link.Sync;

namespace PCL;

public partial class PageSyncDiff
{
    private ManifestDiff? _diff;
    private TaskCompletionSource<bool>? _tcs;

    public PageSyncDiff()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the diff dialog and return true if user clicks Sync Now.
    /// </summary>
    public Task<bool> ShowDialogAsync(ManifestDiff diff)
    {
        _diff = diff;
        _tcs = new TaskCompletionSource<bool>();

        var added = diff.Entries.Count(e => e.ChangeType == DiffChangeType.Added);
        var removed = diff.Entries.Count(e => e.ChangeType == DiffChangeType.Removed);
        var modified = diff.Entries.Count(e => e.ChangeType == DiffChangeType.Modified);

        TxtSummary.Text = "检测到实例变更";
        TxtDetail.Text = $"新增 {added} 个文件, 删除 {removed} 个文件, 修改 {modified} 个文件";

        _BuildDiffList();

        // Force visibility
        Visibility = Visibility.Visible;
        CardProgress.Visibility = Visibility.Collapsed;

        return _tcs.Task;
    }

    private void _BuildDiffList()
    {
        StackDiffList.Children.Clear();

        if (_diff is null) return;

        foreach (var entry in _diff.Entries.Take(200)) // Limit entries for UI
        {
            var item = new MyListItem
            {
                Title = entry.RelativePath,
                Info = _FormatInfo(entry),
                Type = MyListItem.CheckType.Clickable,
                Logo = _GetIcon(entry.ChangeType),
                Tag = entry
            };
            StackDiffList.Children.Add(item);
        }

        if (_diff.Entries.Count > 200)
        {
            var more = new MyListItem
            {
                Title = $"... 还有 {_diff.Entries.Count - 200} 个文件",
                Type = MyListItem.CheckType.Clickable
            };
            StackDiffList.Children.Add(more);
        }
    }

    private static string _FormatInfo(DiffEntry entry)
    {
        var sizeText = entry.ChangeType == DiffChangeType.Removed
            ? _FormatSize(entry.OldFileSize)
            : _FormatSize(entry.NewFileSize);

        var source = entry.DownloadUrl is not null
            ? (entry.DownloadUrl.Contains("curseforge") ? "CurseForge" :
               entry.DownloadUrl.Contains("modrinth") ? "Modrinth" : "官方源")
            : "P2P";

        return $"{sizeText} | {source}";
    }

    private static string _FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1048576 => $"{bytes / 1024.0:F1} KB",
            < 1073741824 => $"{bytes / 1048576.0:F1} MB",
            _ => $"{bytes / 1073741824.0:F2} GB"
        };
    }

    private static string _GetIcon(DiffChangeType type)
    {
        return type switch
        {
            DiffChangeType.Added => "pack://application:,,,/Images/Icons/Download.png",
            DiffChangeType.Removed => "pack://application:,,,/Images/Icons/Delete.png",
            DiffChangeType.Modified => "pack://application:,,,/Images/Icons/Refresh.png",
            _ => "pack://application:,,,/Images/Icons/File.png"
        };
    }

    private void BtnSyncNow_Click(object sender, RoutedEventArgs e)
    {
        _tcs?.TrySetResult(true);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _tcs?.TrySetResult(false);
    }

    /// <summary>
    /// Update progress during sync.
    /// </summary>
    public void UpdateProgress(int filesDone, int totalFiles, string currentFile)
    {
        Dispatcher.InvokeAsync(() =>
        {
            CardProgress.Visibility = Visibility.Visible;
            ProgSync.Value = totalFiles > 0 ? (double)filesDone / totalFiles * 100 : 0;
            TxtProgress.Text = $"正在同步: {currentFile} ({filesDone}/{totalFiles})";
        });
    }
}
