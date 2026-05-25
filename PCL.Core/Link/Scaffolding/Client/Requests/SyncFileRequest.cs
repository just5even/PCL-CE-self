using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using PCL.Core.Link.Scaffolding.Client.Abstractions;

namespace PCL.Core.Link.Scaffolding.Client.Requests;

public sealed record FileChunkResponse(
    string RelativePath,
    long Offset,
    int Length,
    bool IsLastChunk,
    ReadOnlyMemory<byte> Data
);

public sealed class SyncFileRequest(string relativePath, long offset, int length)
    : IRequest<FileChunkResponse>
{
    public string RequestType => "c:sync_file";

    public void WriteRequestBody(IBufferWriter<byte> writer)
    {
        var json = $"{{\"path\":\"{_Escape(relativePath)}\",\"offset\":{offset},\"length\":{length}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        writer.Write(bytes);
    }

    public FileChunkResponse ParseResponseBody(ReadOnlyMemory<byte> body)
    {
        // Binary format: [2B pathLen][path][8B offset LE][4B dataLen LE][1B isLast][data]
        if (body.Length < 15)
            return new FileChunkResponse(relativePath, offset, 0, true, ReadOnlyMemory<byte>.Empty);

        var span = body.Span;
        var pathLen = BinaryPrimitives.ReadInt16LittleEndian(span);
        var path = Encoding.UTF8.GetString(span.Slice(2, pathLen));
        var pos = 2 + pathLen;
        var respOffset = BinaryPrimitives.ReadInt64LittleEndian(span[pos..]);
        var dataLen = BinaryPrimitives.ReadInt32LittleEndian(span[(pos + 8)..]);
        var isLast = span[pos + 12] == 1;
        var data = body.Slice(pos + 13, dataLen);

        return new FileChunkResponse(path, respOffset, dataLen, isLast, data);
    }

    private static string _Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
