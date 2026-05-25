using System;
using System.Buffers;
using System.Text.Json;
using PCL.Core.Link.Scaffolding.Client.Abstractions;

namespace PCL.Core.Link.Scaffolding.Client.Requests;

public sealed record SyncVersionResponse(int Version, string InstanceName);

public sealed class SyncVersionRequest : IRequest<SyncVersionResponse>
{
    public string RequestType => "c:sync_version";

    public void WriteRequestBody(IBufferWriter<byte> writer) { }

    public SyncVersionResponse ParseResponseBody(ReadOnlyMemory<byte> body)
    {
        var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
        var name = root.TryGetProperty("instance_name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
        return new SyncVersionResponse(version, name);
    }
}
