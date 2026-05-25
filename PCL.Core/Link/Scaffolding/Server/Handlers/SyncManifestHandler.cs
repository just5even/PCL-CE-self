using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Scaffolding.Server.Abstractions;
using PCL.Core.Link.Sync;

namespace PCL.Core.Link.Scaffolding.Server.Handlers;

public sealed class SyncManifestHandler : IRequestHandler
{
    public string RequestType => "c:sync_manifest";

    private const int ChunkSize = 200;

    public Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleAsync(
        ReadOnlyMemory<byte> requestBody, IServerContext context,
        string sessionId, CancellationToken ct)
    {
        if (context is not ScaffoldingServerContext sc || sc.CurrentManifest is null)
        {
            var errBody = Encoding.UTF8.GetBytes("{\"error\":\"Host has not selected an instance\"}");
            return Task.FromResult(((byte)32, (ReadOnlyMemory<byte>)errBody));
        }

        // Parse request
        var json = JsonDocument.Parse(requestBody);
        var root = json.RootElement;
        var fromVersion = root.TryGetProperty("from_version", out var fv) && fv.ValueKind == JsonValueKind.Number
            ? fv.GetInt32() : 0;
        var chunkIndex = root.TryGetProperty("chunk_index", out var ci) && ci.ValueKind == JsonValueKind.Number
            ? ci.GetInt32() : 0;

        // Determine manifest to send (full or diff)
        var currentManifest = sc.CurrentManifest;
        IReadOnlyList<ManifestEntry> entries;

        if (fromVersion > 0 && sc.TryGetManifestVersion(fromVersion) is { } oldManifest)
        {
            var diff = DiffCalculator.Compute(oldManifest, currentManifest);
            entries = diff.Entries.Select(e => new ManifestEntry(
                e.RelativePath, e.NewFileSize,
                e.NewSha1Hash ?? "",
                null, null, e.DownloadUrl,
                DateTime.UtcNow
            )).ToList();
        }
        else
        {
            entries = currentManifest.Entries;
        }

        // Paginate
        var totalChunks = Math.Max(1, (int)Math.Ceiling((double)entries.Count / ChunkSize));
        var chunk = entries.Skip(chunkIndex * ChunkSize).Take(ChunkSize).ToList();
        var isLastChunk = chunkIndex >= totalChunks - 1;

        // Build response JSON
        var entriesJson = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var responseJson = $"{{\"manifest_version\":{currentManifest.Version},\"instance_name\":\"{_Escape(currentManifest.InstanceName)}\"," +
                          $"\"total_chunks\":{totalChunks},\"chunk_index\":{chunkIndex}," +
                          $"\"entries\":{entriesJson}," +
                          $"\"is_last_chunk\":{(isLastChunk ? "true" : "false")}}}";

        return Task.FromResult(((byte)0, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(responseJson)));
    }

    private static string _Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
