using Mdz.Commands;

namespace Mdz.Tests;

public class ArchivePathResolverTests : IDisposable
{
    private readonly string _tempDir;

    public ArchivePathResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdz-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ResolveInputArchivePath_NoExtensionAndMdzExists_AppendsExtension()
    {
        var pathWithoutExt = Path.Combine(_tempDir, "demo");
        File.WriteAllText(pathWithoutExt + ".mdz", "x");

        var resolved = ArchivePathResolver.ResolveInputArchivePath(pathWithoutExt);

        Assert.Equal(Path.GetFullPath(pathWithoutExt + ".mdz"), resolved);
    }

    [Fact]
    public void ResolveInputArchivePath_NoExtensionAndExactFileExists_UsesExactPath()
    {
        var pathWithoutExt = Path.Combine(_tempDir, "archive");
        File.WriteAllText(pathWithoutExt, "x");

        var resolved = ArchivePathResolver.ResolveInputArchivePath(pathWithoutExt);

        Assert.Equal(Path.GetFullPath(pathWithoutExt), resolved);
    }

    [Fact]
    public void ResolveInputArchivePath_WithExtension_DoesNotChangePath()
    {
        var pathWithExt = Path.Combine(_tempDir, "demo.mdz");

        var resolved = ArchivePathResolver.ResolveInputArchivePath(pathWithExt);

        Assert.Equal(Path.GetFullPath(pathWithExt), resolved);
    }
}
