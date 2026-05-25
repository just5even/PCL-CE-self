using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Link.Sync;

public static class DiffCalculator
{
    public static ManifestDiff Compute(InstanceManifest local, InstanceManifest remote)
    {
        var entries = new List<DiffEntry>();
        var localMap = local.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var remoteMap = remote.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);

        // Added or Modified in remote (compared to local)
        foreach (var (path, remoteEntry) in remoteMap)
        {
            if (!localMap.TryGetValue(path, out var localEntry))
            {
                entries.Add(new DiffEntry(
                    DiffChangeType.Added, path,
                    remoteEntry.FileSize, 0,
                    remoteEntry.Sha1Hash, null,
                    remoteEntry.DownloadUrl, remoteEntry.FileSize));
            }
            else if (!string.Equals(localEntry.Sha1Hash, remoteEntry.Sha1Hash, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new DiffEntry(
                    DiffChangeType.Modified, path,
                    remoteEntry.FileSize, localEntry.FileSize,
                    remoteEntry.Sha1Hash, localEntry.Sha1Hash,
                    remoteEntry.DownloadUrl, remoteEntry.FileSize));
            }
        }

        // Removed from remote
        foreach (var (path, localEntry) in localMap)
        {
            if (!remoteMap.ContainsKey(path))
            {
                entries.Add(new DiffEntry(
                    DiffChangeType.Removed, path,
                    0, localEntry.FileSize,
                    null, localEntry.Sha1Hash,
                    null, 0));
            }
        }

        return new ManifestDiff(local.Version, remote.Version, entries);
    }
}
