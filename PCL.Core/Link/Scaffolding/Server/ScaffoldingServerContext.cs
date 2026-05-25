using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Link.Scaffolding.Server.Abstractions;
using PCL.Core.Link.Sync;
using PCL.Core.App;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.Link.Scaffolding.Server;

/// <summary>
/// Scaffolding Server running context.
/// </summary>
public class ScaffoldingServerContext : IServerContext
{
    private readonly ConcurrentDictionary<string, TrackedPlayerProfile> _trackedPlayers = [];

    /// <inheritdoc />
    public ConcurrentDictionary<string, TrackedPlayerProfile> TrackedPlayers
    {
        get => _trackedPlayers;
        private init => _trackedPlayers = value;
    }

    public IReadOnlyList<PlayerProfile> PlayerProfiles =>
        _trackedPlayers.Values.Select(player => player.Profile).ToList().AsReadOnly();

    /// <inheritdoc />
    public event Action<IReadOnlyList<PlayerProfile>>? PlayerProfilesPing;

    public void OnPlayerProfilesChanged()
    {
        var currentProfiles = PlayerProfiles;
        Task.Run(() => PlayerProfilesPing?.Invoke(currentProfiles));
    }

    /// <inheritdoc />
    public int MinecraftServerProt { get; }

    /// <inheritdoc />
    public LobbyInfo UserLobbyInfo { get; }

    /// <inheritdoc />
    public string PlayerName { get; }

    // Sync fields
    public InstanceManifest? CurrentManifest { get; set; }
    public string? SelectedInstancePath { get; set; }
    public string? SelectedInstanceIndiePath { get; set; }
    public string? SelectedInstanceName { get; set; }

    private readonly Dictionary<int, InstanceManifest> _manifestVersionHistory = [];
    private readonly object _historyLock = new();

    public void AddManifestVersion(int version, InstanceManifest manifest)
    {
        lock (_historyLock)
        {
            _manifestVersionHistory[version] = manifest;
            // Keep only last 10 versions
            var oldestKeys = _manifestVersionHistory.Keys.OrderBy(k => k)
                .Take(_manifestVersionHistory.Count - 10).ToList();
            foreach (var key in oldestKeys)
                _manifestVersionHistory.Remove(key);
        }
    }

    public InstanceManifest? TryGetManifestVersion(int version)
    {
        lock (_historyLock)
            return _manifestVersionHistory.GetValueOrDefault(version);
    }

    private ScaffoldingServerContext(
        ConcurrentDictionary<string, TrackedPlayerProfile> profiles,
        int mcPort,
        LobbyInfo info,
        string playerName)
    {
        TrackedPlayers = profiles;
        MinecraftServerProt = mcPort;
        UserLobbyInfo = info;
        PlayerName = playerName;
    }

    /// <summary>
    /// Create a <see cref="ScaffoldingServerContext"/>.
    /// </summary>
    /// <param name="playerName">Player name.</param>
    /// <param name="mcPort">Minecraft shared port.</param>
    public static ScaffoldingServerContext Create(string playerName, int mcPort)
    {
        var machineId = Utils.Secret.Identify.LauncherId;
        var profile = new PlayerProfile
        {
            Name = playerName,
            MachineId = Utils.Secret.Identify.LauncherId,
            // Please update ScaffoldingFactory.cs at the same time.
            Vendor = $"PCL CE {Basics.VersionName}, EasyTier {EasyTierMetadata.CurrentEasyTierVer}",
            Kind = PlayerKind.HOST
        };

        var tracked = new TrackedPlayerProfile { Profile = profile, LastSeenUtc = DateTime.UtcNow };

        var roomCode = LobbyCodeGenerator.Generate();

        var profiles = new ConcurrentDictionary<string, TrackedPlayerProfile>();
        profiles.TryAdd(machineId, tracked);

        return new ScaffoldingServerContext(profiles, mcPort, roomCode, playerName);
    }
}