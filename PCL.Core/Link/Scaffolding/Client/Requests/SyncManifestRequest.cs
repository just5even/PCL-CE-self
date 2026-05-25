using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using PCL.Core.Link.Scaffolding.Client.Abstractions;
using PCL.Core.Link.Sync;

namespace PCL.Core.Link.Scaffolding.Client.Requests;

public sealed record SyncManifestResponse(
    int ManifestVersion,
    string InstanceName,
    int TotalChunks,
    int ChunkIndex,
    IReadOnlyList<ManifestEntry> Entries,
    bool IsLastChunk
);

public sealed class SyncManifestRequest(int fromVersion, int chunkIndex)
    : IRequest<SyncManifestResponse>
{
    public string RequestType => "c:sync_manifest";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
        var json = $"{{\"from_version\":{fromVersion},\"chunk_index\":{chunkIndex}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        writer.Write(bytes);
    }

    public SyncManifestResponse ParseResponseBody(ReadOnlyMemory<byte> body)
    {
        var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var version = root.TryGetProperty("manifest_version", out var mv) ? mv.GetInt32() : 0;
        var name = root.TryGetProperty("instance_name", out var ni) ? ni.GetString() ?? "" : "";
        var totalChunks = root.TryGetProperty("total_chunks", out var tc) ? tc.GetInt32() : 1;
        var chunkIdx = root.TryGetProperty("chunk_index", out var ci) ? ci.GetInt32() : 0;
        var isLast = root.TryGetProperty("is_last_chunk", out var il) && il.GetBoolean();

        var entries = new List<ManifestEntry>();
        if (root.TryGetProperty("entries", out var arr))
        {
            foreach (var elem in arr.EnumerateArray())
            {
                entries.Add(new ManifestEntry(
                    elem.TryGetProperty("relative_path", out var rp) ? rp.GetString() ?? "" : "",
                    elem.TryGetProperty("file_size", out var fs) ? fs.GetInt64() : 0,
                    elem.TryGetProperty("sha1_hash", out var sh) ? sh.GetString() ?? "" : "",
                    elem.TryGetProperty("curse_forge_file_id", out var cf) ? cf.GetString() : null,
                    elem.TryGetProperty("modrinth_file_id", out var mr) ? mr.GetString() : null,
                    elem.TryGetProperty("download_url", out var du) ? du.GetString() : null,
                    elem.TryGetProperty("last_modified_utc", out var lm) ? lm.GetDateTime() : DateTime.UtcNow
                ));
            }
        }

        return new SyncManifestResponse(version, name, totalChunks, chunkIdx, entries, isLast);
    }
}
