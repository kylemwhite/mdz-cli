using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using Mdz.Core;
using Mdz.Models;

namespace Mdz.Commands;

/// <summary>
/// Implements the 'mdz create' subcommand.
/// Creates a .mdz archive from a source directory or individual files.
/// </summary>
public static class CreateCommand
{
    public static Command Build()
    {
        var sourceArg = new Argument<DirectoryInfo?>(
            name: "source",
            description: "Source directory containing the files to package.")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var outputArg = new Argument<FileInfo?>(
            name: "output",
            description: "Path to the output .mdz file to create.");
        outputArg.Arity = ArgumentArity.ZeroOrOne;

        var sourceOption = new Option<DirectoryInfo?>(
            aliases: ["--source", "-s"],
            description: "Required source directory. Can be provided positionally as <source>.");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Required output archive path. Can be provided positionally as <output>. If no extension is supplied, .mdz is added automatically.");

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Overwrite the output archive if it already exists.");

        var createIndexOption = new Option<bool>(
            aliases: ["--create-index", "-ci"],
            description: "Automatically create index.md with links to Markdown files when entry-point resolution is ambiguous.");

        var titleOption = new Option<string?>(
            aliases: ["--title", "-t"],
            description: "* Document title to include in manifest.json.");

        var entryPointOption = new Option<string?>(
            aliases: ["--entry-point", "-e"],
            description: "* Relative path to the entry-point Markdown file within the archive.");

        var languageOption = new Option<string?>(
            aliases: ["--language", "-l"],
            description: "* BCP 47 language tag for the document (e.g. 'en', 'fr-CA'). Defaults to 'en'.");

        var authorOption = new Option<string?>(
            aliases: ["--author", "-a"],
            description: "* Author name.");

        var descriptionOption = new Option<string?>(
            aliases: ["--description", "-d"],
            description: "* Short description of the document.");

        var versionOption = new Option<string?>(
            aliases: ["--doc-version"],
            description: "* Version of the document itself (e.g. '1.0.0').");

        var cmd = new Command("create", "Create a .mdz archive from a source directory.")
        {
            sourceArg,
            outputArg,
            sourceOption,
            outputOption,
            forceOption,
            createIndexOption,
            titleOption,
            entryPointOption,
            languageOption,
            authorOption,
            descriptionOption,
            versionOption,
        };

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var source = ctx.ParseResult.GetValueForOption(sourceOption) ?? ctx.ParseResult.GetValueForArgument(sourceArg);
            var output = ctx.ParseResult.GetValueForOption(outputOption) ?? ctx.ParseResult.GetValueForArgument(outputArg);
            var force = ctx.ParseResult.GetValueForOption(forceOption);
            var createIndex = ctx.ParseResult.GetValueForOption(createIndexOption);
            var title = ctx.ParseResult.GetValueForOption(titleOption);
            var entryPoint = ctx.ParseResult.GetValueForOption(entryPointOption);
            var language = ctx.ParseResult.GetValueForOption(languageOption);
            var author = ctx.ParseResult.GetValueForOption(authorOption);
            var description = ctx.ParseResult.GetValueForOption(descriptionOption);
            var docVersion = ctx.ParseResult.GetValueForOption(versionOption);

            ctx.ExitCode = Handle(output, source, force, createIndex, title, entryPoint, language, author, description, docVersion);
        });

        return cmd;
    }

    private static int Handle(
        FileInfo? output,
        DirectoryInfo? source,
        bool force,
        bool createIndex,
        string? title,
        string? entryPoint,
        string? language,
        string? author,
        string? description,
        string? docVersion)
    {
        if (source is null)
        {
            Console.Error.WriteLine("Error: Source directory is required. Provide <source> or --source.");
            return 1;
        }

        if (output is null)
        {
            Console.Error.WriteLine("Error: Output file is required. Provide <output> or --output.");
            return 1;
        }

        if (!source.Exists)
        {
            Console.Error.WriteLine($"Error: Source directory '{source.FullName}' does not exist.");
            return 1;
        }

        var outputPath = output.FullName;
        if (!Path.HasExtension(outputPath))
            outputPath += ".mdz";

        if (Directory.Exists(outputPath))
        {
            Console.Error.WriteLine($"Error: Output path '{outputPath}' is an existing directory.");
            return 1;
        }

        if (File.Exists(outputPath) && !force)
        {
            Console.Error.WriteLine($"Error: Output file '{outputPath}' already exists. Use --force to overwrite.");
            return 1;
        }

        var hasManifestOption =
            title is not null
            || entryPoint is not null
            || language is not null
            || author is not null
            || description is not null
            || docVersion is not null;

        // Build optional manifest. Any metadata option triggers manifest creation.
        Manifest? manifest = null;
        if (hasManifestOption)
        {
            var effectiveTitle = title;
            if (string.IsNullOrWhiteSpace(effectiveTitle))
            {
                effectiveTitle = source.Name;
                Console.WriteLine($"Info: --title was not provided. Using source folder name '{effectiveTitle}' for manifest title.");
            }

            manifest = new Manifest
            {
                Mdz = "1.0.0",
                Title = effectiveTitle,
                EntryPoint = entryPoint,
                Language = language ?? "en",
                Description = description,
                Version = docVersion,
                Authors = author is not null ? [new Author { Name = author }] : null,
            };
        }

        try
        {
            var scan = ScanSourceFiles(source.FullName, manifest);

            if (createIndex)
            {
                var files = scan.Files.ToList();

                var entryPointResolved = ResolveEntryPoint(files.Select(f => f.ArchivePath).ToList(), manifest);
                var tempIndexPath = string.Empty;

                if (entryPointResolved is null)
                {
                    if (!string.IsNullOrWhiteSpace(manifest?.EntryPoint))
                    {
                        Console.Error.WriteLine(
                            $"Error: Manifest entry-point '{manifest.EntryPoint}' was provided but does not exist. " +
                            "Update --entry-point or remove it to allow --create-index.");
                        return 1;
                    }

                    var markdownPaths = files
                        .Select(f => f.ArchivePath)
                        .Where(IsMarkdownFile)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    tempIndexPath = Path.Combine(Path.GetTempPath(), $"mdz-index-{Guid.NewGuid():N}.md");
                    var createCommand = BuildCreateCommandForFooter(
                        sourcePath: source.FullName,
                        outputPath: outputPath,
                        force: force,
                        createIndex: createIndex,
                        title: title,
                        entryPoint: entryPoint,
                        language: language,
                        author: author,
                        description: description,
                        docVersion: docVersion);
                    File.WriteAllText(
                        tempIndexPath,
                        BuildGeneratedIndex(markdownPaths, createCommand, title),
                        Encoding.UTF8);
                    files.Add(("index.md", tempIndexPath));
                    Console.WriteLine("Added generated archive entry 'index.md'.");

                    if (manifest is not null)
                        manifest.EntryPoint = "index.md";
                }

                try
                {
                    MdzArchive.CreateFromFiles(outputPath, files, manifest);
                    WriteCreateSummary(files.Count, scan);
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempIndexPath) && File.Exists(tempIndexPath))
                        File.Delete(tempIndexPath);
                }
            }
            else
            {
                var archivePaths = scan.Files.Select(file => file.ArchivePath).ToList();
                if (string.IsNullOrWhiteSpace(manifest?.EntryPoint)
                    && ResolveEntryPoint(archivePaths, manifest) is null
                    && IsInteractiveConsole())
                {
                    createIndex = PromptCreateIndex();
                    if (createIndex)
                        Console.WriteLine("Generating default index.md.");
                }

                if (createIndex)
                {
                    var files = scan.Files.ToList();
                    var markdownPaths = files
                        .Select(f => f.ArchivePath)
                        .Where(IsMarkdownFile)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var tempIndexPath = Path.Combine(Path.GetTempPath(), $"mdz-index-{Guid.NewGuid():N}.md");
                    var createCommand = BuildCreateCommandForFooter(
                        sourcePath: source.FullName,
                        outputPath: outputPath,
                        force: force,
                        createIndex: createIndex,
                        title: title,
                        entryPoint: entryPoint,
                        language: language,
                        author: author,
                        description: description,
                        docVersion: docVersion);
                    File.WriteAllText(
                        tempIndexPath,
                        BuildGeneratedIndex(markdownPaths, createCommand, title),
                        Encoding.UTF8);
                    files.Add(("index.md", tempIndexPath));
                    Console.WriteLine("Added generated archive entry 'index.md'.");

                    if (manifest is not null)
                        manifest.EntryPoint = "index.md";

                    try
                    {
                        MdzArchive.CreateFromFiles(outputPath, files, manifest);
                        WriteCreateSummary(files.Count, scan);
                    }
                    finally
                    {
                        if (File.Exists(tempIndexPath))
                            File.Delete(tempIndexPath);
                    }
                }
                else
                {
                    MdzArchive.CreateFromFiles(outputPath, scan.Files, manifest);
                    WriteCreateSummary(scan.Files.Count, scan);
                }
            }

            Console.WriteLine($"Created '{outputPath}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine("Archive was not created.");
            if (File.Exists(outputPath))
            {
                Console.Error.WriteLine(
                    $"Note: Existing file '{outputPath}' was left unchanged and may be from an earlier run.");
            }
            return 1;
        }
    }

    private static string BuildGeneratedIndex(
        IReadOnlyList<string> markdownPaths,
        string createCommand,
        string? title)
    {
        var sb = new StringBuilder();
        var pageTitle = string.IsNullOrWhiteSpace(title) ? "Index" : title;
        sb.AppendLine($"# {pageTitle}");
        sb.AppendLine();

        if (markdownPaths.Count == 0)
        {
            sb.AppendLine("No Markdown files were found.");
            return sb.ToString();
        }

        var root = new IndexNode(string.Empty, isDirectory: true);
        foreach (var path in markdownPaths)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var isDirectory = i < parts.Length - 1;
                current = current.GetOrAdd(parts[i], isDirectory);
            }
        }

        RenderIndexTree(root, sb, parentPath: string.Empty, indent: 0);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Generated by `{createCommand}`");
        sb.AppendLine();
        sb.AppendLine("More info: [markdownzip.org](https://markdownzip.org)");

        return sb.ToString();
    }

    private static string BuildCreateCommandForFooter(
        string sourcePath,
        string outputPath,
        bool force,
        bool createIndex,
        string? title,
        string? entryPoint,
        string? language,
        string? author,
        string? description,
        string? docVersion)
    {
        var parts = new List<string>
        {
            "mdz",
            "create",
            QuoteArgIfNeeded(sourcePath),
            QuoteArgIfNeeded(outputPath),
        };

        if (force)
            parts.Add("--force");
        if (createIndex)
            parts.Add("--create-index");

        AppendOption(parts, "--title", title);
        AppendOption(parts, "--entry-point", entryPoint);
        AppendOption(parts, "--language", language);
        AppendOption(parts, "--author", author);
        AppendOption(parts, "--description", description);
        AppendOption(parts, "--doc-version", docVersion);

        return string.Join(' ', parts);
    }

    private static void AppendOption(List<string> parts, string option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        parts.Add(option);
        parts.Add(QuoteArgIfNeeded(value));
    }

    private static string QuoteArgIfNeeded(string value)
    {
        if (value.Contains(' ') || value.Contains('\t') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }

    private static void RenderIndexTree(IndexNode node, StringBuilder sb, string parentPath, int indent)
    {
        var orderedChildren = node.Children
            .OrderByDescending(c => c.IsDirectory)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var child in orderedChildren)
        {
            var currentPath = string.IsNullOrEmpty(parentPath)
                ? child.Name
                : $"{parentPath}/{child.Name}";
            var indentText = new string(' ', indent * 2);

            if (child.IsDirectory)
            {
                sb.AppendLine($"{indentText}- {child.Name}/");
                RenderIndexTree(child, sb, currentPath, indent + 1);
            }
            else
            {
                var linkTarget = ToMarkdownLinkTarget(currentPath);
                sb.AppendLine($"{indentText}- [{child.Name}]({linkTarget})");
            }
        }
    }

    private static string? ResolveEntryPoint(IReadOnlyList<string> archivePaths, Manifest? manifest)
    {
        if (manifest?.EntryPoint is { Length: > 0 } ep
            && archivePaths.Any(path => path.Equals(ep, StringComparison.OrdinalIgnoreCase)))
        {
            return ep;
        }

        if (archivePaths.Any(path => path.Equals("index.md", StringComparison.OrdinalIgnoreCase)))
            return "index.md";

        var rootMarkdown = archivePaths
            .Where(path => !path.Contains('/'))
            .Where(IsMarkdownFile)
            .ToList();

        return rootMarkdown.Count == 1 ? rootMarkdown[0] : null;
    }

    private static bool IsMarkdownFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private static string ToMarkdownLinkTarget(string path)
    {
        var encoded = string.Join('/',
            path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return $"<{encoded}>";
    }

    private static bool IsInteractiveConsole() => !Console.IsInputRedirected;

    private static SourceScanResult ScanSourceFiles(string sourceDirectory, Manifest? manifest)
    {
        var files = new List<(string ArchivePath, string LocalPath)>();
        var skippedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var localPath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var archivePath = Path.GetRelativePath(sourceDirectory, localPath)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (manifest is not null && archivePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                AddSkip(skippedByReason, "manifest.json replaced by generated manifest");
                continue;
            }

            var pathError = PathValidator.Validate(archivePath);
            if (pathError is not null)
            {
                AddSkip(skippedByReason, "invalid path for MDZ rules");
                continue;
            }

            if (!CanRead(localPath))
            {
                AddSkip(skippedByReason, "unreadable/locked file");
                continue;
            }

            files.Add((archivePath, localPath));
        }

        return new SourceScanResult(files, skippedByReason);
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void AddSkip(Dictionary<string, int> skippedByReason, string reason)
    {
        skippedByReason.TryGetValue(reason, out var existing);
        skippedByReason[reason] = existing + 1;
    }

    private static void WriteCreateSummary(int addedFiles, SourceScanResult scan)
    {
        Console.WriteLine($"{addedFiles} file(s) added to archive.");
        if (scan.SkippedCount == 0)
            return;

        var reasonSummary = string.Join(
            "; ",
            scan.SkippedByReason
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => $"{kvp.Value} {kvp.Key}"));
        Console.WriteLine($"{scan.SkippedCount} file(s) skipped: {reasonSummary}.");
    }

    private static bool PromptCreateIndex()
    {
        Console.Write("No unambiguous entry point found. Create a default index.md now? [y/N]: ");
        var response = Console.ReadLine()?.Trim();
        return response is not null
            && (response.Equals("y", StringComparison.OrdinalIgnoreCase)
                || response.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class IndexNode(string name, bool isDirectory)
    {
        public string Name { get; } = name;
        public bool IsDirectory { get; private set; } = isDirectory;
        public List<IndexNode> Children { get; } = [];

        public IndexNode GetOrAdd(string name, bool isDirectory)
        {
            var existing = Children.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.IsDirectory |= isDirectory;
                return existing;
            }

            var created = new IndexNode(name, isDirectory);
            Children.Add(created);
            return created;
        }
    }

    private sealed record SourceScanResult(
        List<(string ArchivePath, string LocalPath)> Files,
        Dictionary<string, int> SkippedByReason)
    {
        public int SkippedCount => SkippedByReason.Values.Sum();
    }
}
