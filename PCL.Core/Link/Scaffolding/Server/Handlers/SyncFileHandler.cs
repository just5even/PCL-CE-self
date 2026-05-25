using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Scaffolding.Server.Abstractions;

namespace PCL.Core.Link.Scaffolding.Server.Handlers;

public sealed class SyncFileHandler : IRequestHandler
{
    public string RequestType => "c:sync_file";

    private const int MaxChunkSize = 61440; // 60KB

    public async Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleAsync(
        ReadOnlyMemory<byte> requestBody, IServerContext context,
        string sessionId, CancellationToken ct)
    {
        if (context is not ScaffoldingServerContext sc || sc.SelectedInstanceIndiePath is null)
        {
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"No instance selected\"}"));
        }

        // Parse JSON request: {"path":"...", "offset":N, "length":N}
        var json = JsonDocument.Parse(requestBody);
        var root = json.RootElement;
        var relativePath = root.TryGetProperty("path", out var p) ? p.GetString() : null;
        var offset = root.TryGetProperty("offset", out var o) ? o.GetInt64() : 0;
        var length = root.TryGetProperty("length", out var l) ? Math.Min(l.GetInt32(), MaxChunkSize) : MaxChunkSize;

        if (string.IsNullOrEmpty(relativePath))
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"Missing path\"}"));

        // Security: prevent path traversal
        relativePath = relativePath.Replace('\\', '/');
        if (relativePath.Contains("..") || Path.IsPathRooted(relativePath))
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"Invalid path\"}"));

        var fullPath = Path.GetFullPath(Path.Combine(sc.SelectedInstanceIndiePath, relativePath));

        // Verify path stays within instance dir
        if (!fullPath.StartsWith(sc.SelectedInstanceIndiePath, StringComparison.OrdinalIgnoreCase))
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"Path traversal denied\"}"));

        if (!File.Exists(fullPath))
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"File not found\"}"));

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var remaining = fileInfo.Length - offset;
            if (remaining <= 0)
            {
                // Empty chunk = EOF signal
                return _BuildResponse(relativePath, offset, 0, true, ReadOnlyMemory<byte>.Empty);
            }

            var readLength = (int)Math.Min(Math.Min(length, remaining), MaxChunkSize);
            var buffer = new byte[readLength];

            await using var fs = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

            fs.Seek(offset, SeekOrigin.Begin);
            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, readLength), ct).ConfigureAwait(false);
            var isLast = (offset + bytesRead) >= fileInfo.Length;

            return _BuildResponse(relativePath, offset, bytesRead, isLast, buffer.AsMemory(0, bytesRead));
        }
        catch (Exception)
        {
            return (32, Encoding.UTF8.GetBytes("{\"error\":\"Failed to read file\"}"));
        }
    }

    private static (byte Status, ReadOnlyMemory<byte> Body) _BuildResponse(
        string relativePath, long offset, int dataLength, bool isLast, ReadOnlyMemory<byte> data)
    {
        var pathBytes = Encoding.UTF8.GetBytes(relativePath);
        var totalLength = 2 + pathBytes.Length + 8 + 4 + 1 + dataLength;
        var buffer = new byte[totalLength];
        var span = buffer.AsSpan();

        // [2B pathLen][path][8B offset LE][4B dataLen LE][1B isLast][data]
        BinaryPrimitives.WriteInt16LittleEndian(span, (short)pathBytes.Length);
        pathBytes.CopyTo(span[2..]);
        var pos = 2 + pathBytes.Length;
        BinaryPrimitives.WriteInt64LittleEndian(span[pos..], offset);
        BinaryPrimitives.WriteInt32LittleEndian(span[(pos + 8)..], dataLength);
        span[pos + 12] = (byte)(isLast ? 1 : 0);
        data.Span.CopyTo(span[(pos + 13)..]);

        return (0, buffer);
    }
}
