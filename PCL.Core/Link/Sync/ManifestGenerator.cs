using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.Hash;

namespace PCL.Core.Link.Sync;

public static class ManifestGenerator
{
    private static readonly HashSet<string> _ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets", "libraries", "versions", "logs", "crash-reports", ".cache"
    };

    private const int Sha1StreamBufferSize = 81920;

    public static async Task<InstanceManifest> GenerateAsync(
        string indiePath, string instancePath, string instanceName,
        string minecraftVersion, string modLoader, int version,
        CancellationToken ct = default)
    {
        var entries = new List<ManifestEntry>();

        // Scan the indie game directory
        if (Directory.Exists(indiePath))
        {
            await _ScanDirectoryAsync(indiePath, indiePath, entries, ct).ConfigureAwait(false);
        }

        // Also include instance-specific files (version JSON, jar, PCL settings)
        if (Directory.Exists(instancePath) && !string.Equals(
                Path.GetFullPath(indiePath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(instancePath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            await _ScanDirectoryAsync(instancePath, instancePath, entries, ct).ConfigureAwait(false);
        }

        return new InstanceManifest(instanceName, minecraftVersion, modLoader, version, entries);
    }

    private static async Task _ScanDirectoryAsync(
        string rootPath, string currentPath, List<ManifestEntry> entries, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(currentPath))
            {
                var dirName = Path.GetFileName(dir);
                if (_ExcludedDirs.Contains(dirName)) continue;
                await _ScanDirectoryAsync(rootPath, dir, entries, ct).ConfigureAwait(false);
            }

            foreach (var file in Directory.EnumerateFiles(currentPath))
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(rootPath, file)
                    .Replace('\\', '/');

                // Skip huge files (>1GB) - unlikely to be mod/config content
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > 1_073_741_824) continue;

                var sha1 = await _ComputeSha1HexAsync(file, ct).ConfigureAwait(false);

                entries.Add(new ManifestEntry(
                    relativePath,
                    fileInfo.Length,
                    sha1,
                    CurseForgeFileId: null,
                    ModrinthFileId: null,
                    DownloadUrl: null,
                    fileInfo.LastWriteTimeUtc
                ));
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<string> _ComputeSha1HexAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            Sha1StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA1Provider.Instance.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return hash.AsSpan().ToHexString();
    }
}
