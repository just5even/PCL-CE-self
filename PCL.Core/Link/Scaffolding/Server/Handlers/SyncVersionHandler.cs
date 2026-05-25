using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Scaffolding.Server.Abstractions;
using PCL.Core.Link.Sync;

namespace PCL.Core.Link.Scaffolding.Server.Handlers;

public sealed class SyncVersionHandler : IRequestHandler
{
    public string RequestType => "c:sync_version";

    public Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleAsync(
        ReadOnlyMemory<byte> requestBody, IServerContext context,
        string sessionId, CancellationToken ct)
    {
        if (context is not ScaffoldingServerContext sc || sc.CurrentManifest is null)
        {
            var errBody = Encoding.UTF8.GetBytes("{\"error\":\"Host has not selected an instance\"}");
            return Task.FromResult(((byte)32, (ReadOnlyMemory<byte>)errBody));
        }

        var json = $"{{\"version\":{sc.CurrentManifest.Version},\"instance_name\":\"{_Escape(sc.CurrentManifest.InstanceName)}\"}}";
        return Task.FromResult(((byte)0, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(json)));
    }

    private static string _Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
