using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Scaffolding.Client;
using PCL.Core.Link.Scaffolding.Client.Abstractions;
using PCL.Core.Link.Scaffolding.Client.Requests;
using PCL.Core.Logging;
using PCL.Core.Utils.Hash;

namespace PCL.Core.Link.Sync;

public static class HybridFileDownloader
{
    private const int ChunkSize = 61440; // 60KB for protocol frame limit (65536)

    /// <summary>
    /// Try official download first, fall back to P2P.
    /// </summary>
    public static async Task<bool> DownloadAsync(
        DiffEntry entry,
        string destRoot,
        ScaffoldingClient? p2pClient,
        Func<string, string, CancellationToken, Task<bool>>? officialDownloader,
        CancellationToken ct = default)
    {
        var destPath = Path.Combine(destRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var destDir = Path.GetDirectoryName(destPath);
        if (destDir is not null)
            Directory.CreateDirectory(destDir);

        // Try official source first
        if (entry.DownloadUrl is not null && officialDownloader is not null)
        {
            try
            {
                var success = await officialDownloader(entry.DownloadUrl, destPath, ct).ConfigureAwait(false);
                if (success)
                {
                    // Verify hash
                    if (entry.NewSha1Hash is not null && _VerifyFileHash(destPath, entry.NewSha1Hash))
                        return true;
                    // Hash mismatch, delete and fall through to P2P
                    try { File.Delete(destPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogWrapper.Warn("HybridFileDownloader",
                    $"Official download failed for {entry.RelativePath}: {ex.Message}");
            }
        }

        // P2P fallback
        if (p2pClient is not null)
        {
            return await P2PDownloadAsync(entry, destPath, p2pClient, ct).ConfigureAwait(false);
        }

        return false;
    }

    public static async Task<bool> P2PDownloadAsync(
        DiffEntry entry, string destPath, ScaffoldingClient client, CancellationToken ct)
    {
        if (!client.IsConnected)
            return false;

        var fileSize = entry.NewFileSize;
        var downloaded = 0L;
        var destDir = Path.GetDirectoryName(destPath);
        if (destDir is not null)
            Directory.CreateDirectory(destDir);

        // Download to temp file, then rename
        var tempPath = destPath + ".p2ptmp";

        try
        {
            await using var fs = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (downloaded < fileSize)
            {
                ct.ThrowIfCancellationRequested();

                var requestLength = (int)Math.Min(ChunkSize, fileSize - downloaded);
                var request = new SyncFileRequest(entry.RelativePath, downloaded, requestLength);
                var response = await client.SendRequestAsync(request, ct).ConfigureAwait(false);

                if (response.Data.IsEmpty)
                    throw new IOException($"Empty chunk received for {entry.RelativePath} at offset {downloaded}");

                await fs.WriteAsync(response.Data, ct).ConfigureAwait(false);
                downloaded += response.Data.Length;

                if (response.IsLastChunk)
                    break;
            }

            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "HybridFileDownloader",
                $"P2P download failed for {entry.RelativePath}");
            try { File.Delete(tempPath); } catch { }
            return false;
        }

        // Verify hash
        if (entry.NewSha1Hash is not null && !_VerifyFileHash(tempPath, entry.NewSha1Hash))
        {
            try { File.Delete(tempPath); } catch { }
            return false;
        }

        // Atomic rename
        try { File.Delete(destPath); } catch { }
        File.Move(tempPath, destPath);
        return true;
    }

    private static bool _VerifyFileHash(string filePath, string expectedHex)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var hash = SHA1Provider.Instance.ComputeHash(fs).AsSpan();
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return string.Equals(hex, expectedHex, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // P2P-only: request a single file via the sync protocol, used for small files in hybrid mode
    public static async Task<bool> P2PDownloadFileAsync(
        string relativePath, long fileSize, string destPath,
        ScaffoldingClient client, CancellationToken ct = default)
    {
        var entry = new DiffEntry(
            DiffChangeType.Added, relativePath, fileSize, 0,
            null, null, null, fileSize);
        return await P2PDownloadAsync(entry, destPath, client, ct).ConfigureAwait(false);
    }
}
