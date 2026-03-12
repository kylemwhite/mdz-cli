namespace Mdz.Commands;

public static class ArchivePathResolver
{
    public static string ResolveInputArchivePath(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        if (Path.HasExtension(fullPath))
            return fullPath;

        if (File.Exists(fullPath))
            return fullPath;

        return fullPath + ".mdz";
    }
}
