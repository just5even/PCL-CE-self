using System;
using System.IO;
using System.Threading;

namespace PCL.Core.Link.Sync;

public sealed class InstanceWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly Timer _debounceTimer;
    private bool _dirty;
    private bool _disposed;

    public event Action? InstanceChanged;

    public InstanceWatcher(string instancePath)
    {
        _debounceTimer = new(_OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        if (!Directory.Exists(instancePath))
            return;

        _watcher = new FileSystemWatcher(instancePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                         | NotifyFilters.Size | NotifyFilters.DirectoryName,
            EnableRaisingEvents = false
        };

        _watcher.Created += (_, e) => _MarkDirty(e.FullPath);
        _watcher.Changed += (_, e) => _MarkDirty(e.FullPath);
        _watcher.Deleted += (_, e) => _MarkDirty(e.FullPath);
        _watcher.Renamed += (_, e) => _MarkDirty(e.FullPath);
    }

    public void Start()
    {
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = false;
    }

    private void _MarkDirty(string path)
    {
        // Ignore changes in temp/cache dirs
        var rel = Path.GetRelativePath(_watcher?.Path ?? "", path);
        if (rel.StartsWith("logs", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("crash-reports", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith(".cache", StringComparison.OrdinalIgnoreCase))
            return;

        _dirty = true;
        // 5-second debounce
        _debounceTimer.Change(5000, Timeout.Infinite);
    }

    private void _OnDebounceElapsed(object? state)
    {
        if (_dirty)
        {
            _dirty = false;
            InstanceChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
        _watcher?.Dispose();
        _watcher = null;
    }
}
