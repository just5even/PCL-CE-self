using System;
using System.Collections.Generic;

namespace PCL.Core.Link.Sync;

public record ManifestEntry(
    string RelativePath,
    long FileSize,
    string Sha1Hash, // 40-char hex string
    string? CurseForgeFileId,
    string? ModrinthFileId,
    string? DownloadUrl,
    DateTime LastModifiedUtc
);

public record InstanceManifest(
    string InstanceName,
    string MinecraftVersion,
    string ModLoader,
    int Version,
    IReadOnlyList<ManifestEntry> Entries
);

public enum DiffChangeType
{
    Added,
    Removed,
    Modified
}

public record DiffEntry(
    DiffChangeType ChangeType,
    string RelativePath,
    long NewFileSize,
    long OldFileSize,
    string? NewSha1Hash,  // null for Removed
    string? OldSha1Hash,  // null for Added
    string? DownloadUrl,
    long DownloadSize
);

public record ManifestDiff(
    int FromVersion,
    int ToVersion,
    IReadOnlyList<DiffEntry> Entries
);
