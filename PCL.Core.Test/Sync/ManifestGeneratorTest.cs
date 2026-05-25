using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link.Sync;

namespace PCL.Core.Test.Sync;

[TestClass]
public class ManifestGeneratorTest
{
    [TestMethod]
    public async Task Generate_EmptyDirectory_ReturnsEmptyEntries()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(indiePath);

        var manifest = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual("test", manifest.InstanceName);
        Assert.AreEqual("1.20.1", manifest.MinecraftVersion);
        Assert.AreEqual("Forge", manifest.ModLoader);
        Assert.AreEqual(1, manifest.Version);
        Assert.AreEqual(0, manifest.Entries.Count);
    }

    [TestMethod]
    public async Task Generate_WithFiles_CreatesCorrectEntries()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(Path.Combine(indiePath, "mods"));
        Directory.CreateDirectory(Path.Combine(indiePath, "config"));

        File.WriteAllText(Path.Combine(indiePath, "mods", "test.jar"), "mod content");
        File.WriteAllText(Path.Combine(indiePath, "config", "test.cfg"), "cfg content");

        var manifest = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual(2, manifest.Entries.Count);
        Assert.IsTrue(manifest.Entries.Any(e => e.RelativePath == "mods/test.jar"));
        Assert.IsTrue(manifest.Entries.Any(e => e.RelativePath == "config/test.cfg"));

        var jarEntry = manifest.Entries.First(e => e.RelativePath == "mods/test.jar");
        Assert.AreEqual("mod content".Length, jarEntry.FileSize);
        Assert.IsNotNull(jarEntry.Sha1Hash);
        Assert.AreEqual(40, jarEntry.Sha1Hash.Length); // hex string length
    }

    [TestMethod]
    public async Task Generate_SkipsExcludedDirectories()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(Path.Combine(indiePath, "assets"));
        Directory.CreateDirectory(Path.Combine(indiePath, "libraries"));
        Directory.CreateDirectory(Path.Combine(indiePath, "logs"));
        Directory.CreateDirectory(Path.Combine(indiePath, "crash-reports"));
        Directory.CreateDirectory(Path.Combine(indiePath, ".cache"));
        Directory.CreateDirectory(Path.Combine(indiePath, "versions"));

        File.WriteAllText(Path.Combine(indiePath, "assets", "icon.png"), "asset");
        File.WriteAllText(Path.Combine(indiePath, "libraries", "lib.jar"), "lib");
        File.WriteAllText(Path.Combine(indiePath, "logs", "latest.log"), "log");
        File.WriteAllText(Path.Combine(indiePath, "crash-reports", "crash.txt"), "crash");
        File.WriteAllText(Path.Combine(indiePath, ".cache", "cache.dat"), "cache");
        File.WriteAllText(Path.Combine(indiePath, "versions", "version.json"), "ver");

        var manifest = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual(0, manifest.Entries.Count,
            "All excluded dirs should be skipped so no files should appear");
    }

    [TestMethod]
    public async Task Generate_DeterministicHash_SameContent_SameHash()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(indiePath);

        File.WriteAllText(Path.Combine(indiePath, "test.txt"), "hello world");

        var manifest1 = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);
        var manifest2 = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual(1, manifest1.Entries.Count);
        Assert.AreEqual(1, manifest2.Entries.Count);
        Assert.AreEqual(manifest1.Entries[0].Sha1Hash, manifest2.Entries[0].Sha1Hash,
            "Same file content should produce same SHA1 hash");
    }

    [TestMethod]
    public async Task Generate_DifferentContent_DifferentHash()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(indiePath);

        var filePath = Path.Combine(indiePath, "test.txt");
        File.WriteAllText(filePath, "hello world");
        var manifest1 = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        File.WriteAllText(filePath, "different content");
        var manifest2 = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreNotEqual(manifest1.Entries[0].Sha1Hash, manifest2.Entries[0].Sha1Hash);
    }

    [TestMethod]
    public async Task Generate_NestedDirectories_CorrectRelativePaths()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(Path.Combine(indiePath, "mods", "sub", "deep"));

        File.WriteAllText(Path.Combine(indiePath, "mods", "sub", "deep", "nested.jar"), "nested");

        var manifest = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual(1, manifest.Entries.Count);
        Assert.AreEqual("mods/sub/deep/nested.jar", manifest.Entries[0].RelativePath);
    }

    [TestMethod]
    public async Task Generate_RespectsCancellationToken()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(indiePath);

        // Create many files so we have a chance to cancel mid-scan
        for (int i = 0; i < 50; i++)
            File.WriteAllText(Path.Combine(indiePath, $"file_{i}.txt"), new string('x', 1000));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await ManifestGenerator.GenerateAsync(
                indiePath, indiePath, "test", "1.20.1", "Forge", 1, cts.Token);
            Assert.Fail("Expected OperationCanceledException was not thrown");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task Generate_RecordsLastModifiedUtc()
    {
        using var tmp = new TempDirectory();
        var indiePath = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(indiePath);

        var filePath = Path.Combine(indiePath, "test.txt");
        File.WriteAllText(filePath, "content");

        var manifest = await ManifestGenerator.GenerateAsync(
            indiePath, indiePath, "test", "1.20.1", "Forge", 1);

        Assert.AreEqual(1, manifest.Entries.Count);
        var entry = manifest.Entries[0];
        var fileInfo = new FileInfo(filePath);
        // Allow 1 second tolerance
        Assert.IsTrue(Math.Abs((entry.LastModifiedUtc - fileInfo.LastWriteTimeUtc).TotalSeconds) < 1);
    }

    /// <summary>
    /// Creates a temporary directory and deletes it on dispose.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PCL_Test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch { /* best effort */ }
        }
    }
}
