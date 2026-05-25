using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PCL.Core.App.IoC;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Scaffolding.Client;
using PCL.Core.Link.Scaffolding.Client.Requests;
using PCL.Core.Link.Scaffolding.Server;
using PCL.Core.Logging;

namespace PCL.Core.Link.Sync;

[LifecycleService(LifecycleState.Loaded)]
public class SyncService() : GeneralService("sync", "Instance Sync Service")
{
    public static SyncState CurrentState { get; private set; } = SyncState.Idle;
    public static InstanceManifest? RemoteManifest { get; private set; }
    public static InstanceManifest? LocalManifest { get; private set; }
    public static ManifestDiff? CurrentDiff { get; private set; }

    private static InstanceWatcher? _watcher;
    private static string? _hostInstanceIndiePath;
    private static string? _hostInstanceVersionPath;
    private static string? _hostInstanceName;
    private static string? _hostMcVersion;
    private static string? _hostModLoader;
    private static int _currentManifestVersion;

    #region UI Events

    public static event Action<SyncState, SyncState>? StateChanged;
    public static event Action<ManifestDiff>? DiffReady;
    public static event Action<int, int>? SyncProgress;
#pragma warning disable CS0067
    public static event Action<long, long>? DownloadProgress;
#pragma warning restore CS0067

    #endregion

    /// <inheritdoc />
    public override void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _SetState(SyncState.Idle);
    }

    #region Host Operations

    /// <summary>
    /// Host selects an instance to share in the room.
    /// </summary>
    public static async Task<bool> HostSelectInstanceAsync(
        string indiePath, string instancePath,
        string instanceName, string mcVersion, string modLoader)
    {
        _SetState(SyncState.GeneratingManifest);

        try
        {
            _hostInstanceIndiePath = indiePath;
            _hostInstanceVersionPath = instancePath;
            _hostInstanceName = instanceName;
            _hostMcVersion = mcVersion;
            _hostModLoader = modLoader;
            _currentManifestVersion = 1;

            var manifest = await ManifestGenerator.GenerateAsync(
                indiePath, instancePath, instanceName,
                mcVersion, modLoader, _currentManifestVersion).ConfigureAwait(false);

            RemoteManifest = manifest;
            LocalManifest = manifest;

            // Store manifest on the server context
            var serverEntity = LobbyService.CurrentServerEntity;
            if (serverEntity?.Server.ServerContext is ScaffoldingServerContext ctx)
            {
                ctx.CurrentManifest = manifest;
                ctx.SelectedInstanceIndiePath = indiePath;
                ctx.SelectedInstancePath = instancePath;
                ctx.SelectedInstanceName = instanceName;
                ctx.AddManifestVersion(_currentManifestVersion, manifest);
            }

            // Start file watcher
            _watcher?.Dispose();
            _watcher = new InstanceWatcher(indiePath);
            _watcher.InstanceChanged += async () =>
            {
                LogWrapper.Info("SyncService", "Instance files changed, bumping manifest version.");
                await HostRefreshManifestAsync().ConfigureAwait(false);
            };
            _watcher.Start();

            _SetState(SyncState.Ready);
            return true;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "SyncService", "Failed to select host instance.");
            _SetState(SyncState.Error);
            return false;
        }
    }

    public static async Task HostRefreshManifestAsync()
    {
        if (_hostInstanceIndiePath is null || _hostInstanceVersionPath is null ||
            _hostInstanceName is null)
            return;

        _currentManifestVersion++;

        var manifest = await ManifestGenerator.GenerateAsync(
            _hostInstanceIndiePath, _hostInstanceVersionPath, _hostInstanceName,
            _hostMcVersion ?? "Unknown", _hostModLoader ?? "Unknown",
            _currentManifestVersion).ConfigureAwait(false);

        RemoteManifest = manifest;
        LocalManifest = manifest;

        var serverEntity = LobbyService.CurrentServerEntity;
        if (serverEntity?.Server.ServerContext is ScaffoldingServerContext ctx)
        {
            ctx.CurrentManifest = manifest;
            ctx.AddManifestVersion(_currentManifestVersion, manifest);
        }
    }

    #endregion

    #region Client Operations

    /// <summary>
    /// Fetch the remote manifest and compute a diff against the local instance.
    /// </summary>
    public static async Task<ManifestDiff> FetchAndComputeDiffAsync(
        ScaffoldingClient client, string localIndiePath, string localInstancePath,
        string instanceName, string mcVersion, string modLoader,
        CancellationToken ct = default)
    {
        _SetState(SyncState.FetchingManifest);

        try
        {
            // 1. Quick version check
            var versionReq = new SyncVersionRequest();
            var versionResp = await client.SendRequestAsync(versionReq, ct).ConfigureAwait(false);
            var remoteVersion = versionResp.Version;

            // 2. If we already have a manifest from same version, skip
            if (RemoteManifest is not null && RemoteManifest.Version == remoteVersion
                && CurrentDiff is not null)
            {
                _SetState(SyncState.Ready);
                return CurrentDiff;
            }

            // 3. Fetch full remote manifest (paginated)
            var entries = new List<ManifestEntry>();
            var chunkIndex = 0;
            var totalChunks = 1;

            while (chunkIndex < totalChunks)
            {
                ct.ThrowIfCancellationRequested();
                var manifestReq = new SyncManifestRequest(fromVersion: 0, chunkIndex);
                var manifestResp = await client.SendRequestAsync(manifestReq, ct).ConfigureAwait(false);
                totalChunks = manifestResp.TotalChunks;
                entries.AddRange(manifestResp.Entries);
                chunkIndex++;
            }

            RemoteManifest = new InstanceManifest(
                versionResp.InstanceName, mcVersion, modLoader, remoteVersion, entries);

            // 4. Generate local manifest
            LocalManifest = await ManifestGenerator.GenerateAsync(
                localIndiePath, localInstancePath, instanceName,
                mcVersion, modLoader, 0).ConfigureAwait(false);

            // 5. Compute diff
            _SetState(SyncState.ComputingDiff);
            var diff = DiffCalculator.Compute(LocalManifest, RemoteManifest);
            CurrentDiff = diff;

            DiffReady?.Invoke(diff);
            _SetState(SyncState.Ready);
            return diff;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "SyncService", "Failed to fetch or compute diff.");
            _SetState(SyncState.Error);
            throw;
        }
    }

    /// <summary>
    /// Apply the diff to the local instance after user confirmation.
    /// </summary>
    public static async Task ApplySyncAsync(
        ScaffoldingClient client, ManifestDiff diff,
        string localIndiePath,
        Func<string, string, CancellationToken, Task<bool>>? officialDownloader = null,
        CancellationToken ct = default)
    {
        _SetState(SyncState.Syncing);

        try
        {
            var entries = diff.Entries;
            var total = entries.Count;
            var done = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                SyncProgress?.Invoke(done, total);

                switch (entry.ChangeType)
                {
                    case DiffChangeType.Added:
                    case DiffChangeType.Modified:
                        await HybridFileDownloader.DownloadAsync(
                            entry, localIndiePath, client, officialDownloader, ct)
                            .ConfigureAwait(false);
                        break;

                    case DiffChangeType.Removed:
                        var delPath = Path.Combine(localIndiePath,
                            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        try { File.Delete(delPath); } catch { }
                        break;
                }

                done++;
            }

            SyncProgress?.Invoke(done, total);

            // Update local manifest to match remote
            if (RemoteManifest is not null)
            {
                LocalManifest = RemoteManifest;
                CurrentDiff = null;
            }

            _SetState(SyncState.Ready);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "SyncService", "Failed to apply sync.");
            _SetState(SyncState.Error);
            throw;
        }
    }

    #endregion

    #region Private Helpers

    private static void _SetState(SyncState newState)
    {
        var oldState = CurrentState;
        if (oldState == newState) return;
        CurrentState = newState;
        LogWrapper.Info("SyncService", $"State changed from {oldState} to {newState}");
        _RunInUiAsync(() => StateChanged?.Invoke(oldState, newState));
    }

    private static void _RunInUiAsync(Action action)
    {
        Application.Current?.Dispatcher.InvokeAsync(action);
    }

    #endregion
}

public enum SyncState
{
    Idle,
    GeneratingManifest,
    FetchingManifest,
    ComputingDiff,
    Syncing,
    Ready,
    Error
}
