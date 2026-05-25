using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link.Sync;

namespace PCL.Core.Test.Sync;

[TestClass]
public class DiffCalculatorTest
{
    [TestMethod]
    public void Compute_EmptyBoth_ReturnsEmptyDiff()
    {
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, Array.Empty<ManifestEntry>());
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, Array.Empty<ManifestEntry>());

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(1, diff.FromVersion);
        Assert.AreEqual(2, diff.ToVersion);
        Assert.AreEqual(0, diff.Entries.Count);
    }

    [TestMethod]
    public void Compute_AddedFiles_DetectsAdded()
    {
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, Array.Empty<ManifestEntry>());
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, new[]
        {
            new ManifestEntry("mods/jei.jar", 1024, "aaa111", null, null, null, DateTime.UtcNow),
            new ManifestEntry("config/test.cfg", 512, "bbb222", null, null, null, DateTime.UtcNow),
        });

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(2, diff.Entries.Count);
        Assert.IsTrue(diff.Entries.All(e => e.ChangeType == DiffChangeType.Added));
        Assert.AreEqual("mods/jei.jar", diff.Entries[0].RelativePath);
        Assert.AreEqual(1024, diff.Entries[0].NewFileSize);
        Assert.AreEqual("aaa111", diff.Entries[0].NewSha1Hash);
        Assert.IsNull(diff.Entries[0].OldSha1Hash);
    }

    [TestMethod]
    public void Compute_RemovedFiles_DetectsRemoved()
    {
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, new[]
        {
            new ManifestEntry("mods/old.jar", 2048, "ccc333", null, null, null, DateTime.UtcNow),
        });
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, Array.Empty<ManifestEntry>());

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(1, diff.Entries.Count);
        Assert.AreEqual(DiffChangeType.Removed, diff.Entries[0].ChangeType);
        Assert.AreEqual("mods/old.jar", diff.Entries[0].RelativePath);
        Assert.AreEqual(2048, diff.Entries[0].OldFileSize);
        Assert.IsNull(diff.Entries[0].NewSha1Hash);
        Assert.AreEqual("ccc333", diff.Entries[0].OldSha1Hash);
    }

    [TestMethod]
    public void Compute_ModifiedFile_DetectsHashChange()
    {
        var now = DateTime.UtcNow;
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, new[]
        {
            new ManifestEntry("mods/mod.jar", 4096, "hash_v1", null, null, null, now),
        });
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, new[]
        {
            new ManifestEntry("mods/mod.jar", 8192, "hash_v2", null, null, "https://example.com/mod.jar", now),
        });

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(1, diff.Entries.Count);
        Assert.AreEqual(DiffChangeType.Modified, diff.Entries[0].ChangeType);
        Assert.AreEqual(8192, diff.Entries[0].NewFileSize);
        Assert.AreEqual(4096, diff.Entries[0].OldFileSize);
        Assert.AreEqual("hash_v2", diff.Entries[0].NewSha1Hash);
        Assert.AreEqual("hash_v1", diff.Entries[0].OldSha1Hash);
        Assert.AreEqual("https://example.com/mod.jar", diff.Entries[0].DownloadUrl);
    }

    [TestMethod]
    public void Compute_SameHash_NoChange()
    {
        var now = DateTime.UtcNow;
        var entry = new ManifestEntry("mods/mod.jar", 4096, "samehash", null, null, null, now);
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, new[] { entry });
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, new[] { entry });

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(0, diff.Entries.Count);
    }

    [TestMethod]
    public void Compute_CaseInsensitivePath()
    {
        var now = DateTime.UtcNow;
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, new[]
        {
            new ManifestEntry("Mods/MyMod.jar", 100, "hash1", null, null, null, now),
        });
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, new[]
        {
            new ManifestEntry("mods/mymod.jar", 200, "hash2", null, null, null, now),
        });

        var diff = DiffCalculator.Compute(local, remote);

        // Should detect as Modified because paths match case-insensitively
        Assert.AreEqual(1, diff.Entries.Count);
        Assert.AreEqual(DiffChangeType.Modified, diff.Entries[0].ChangeType);
    }

    [TestMethod]
    public void Compute_MixedChanges_AllDetected()
    {
        var now = DateTime.UtcNow;
        var local = new InstanceManifest("test", "1.20.1", "Forge", 1, new[]
        {
            new ManifestEntry("mods/keep.jar", 100, "keep", null, null, null, now),
            new ManifestEntry("mods/remove.jar", 200, "rem", null, null, null, now),
            new ManifestEntry("mods/update.jar", 300, "old", null, null, null, now),
        });
        var remote = new InstanceManifest("test", "1.20.1", "Forge", 2, new[]
        {
            new ManifestEntry("mods/keep.jar", 100, "keep", null, null, null, now),
            new ManifestEntry("mods/update.jar", 400, "new", null, null, null, now),
            new ManifestEntry("mods/add.jar", 500, "add", null, null, null, now),
        });

        var diff = DiffCalculator.Compute(local, remote);

        Assert.AreEqual(3, diff.Entries.Count);
        Assert.AreEqual(1, diff.Entries.Count(e => e.ChangeType == DiffChangeType.Added));
        Assert.AreEqual(1, diff.Entries.Count(e => e.ChangeType == DiffChangeType.Removed));
        Assert.AreEqual(1, diff.Entries.Count(e => e.ChangeType == DiffChangeType.Modified));
    }
}
