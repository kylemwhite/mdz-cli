using System.CommandLine;
using System.Reflection;
using Mdz.Commands;

var version = Assembly.GetEntryAssembly()?
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion
    ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
    ?? "unknown";

var rootCommand = new RootCommand(
    $"mdz - command-line tool for creating, extracting, validating, and inspecting .mdz files. (v{version}) " +
    "Use 'mdz <command> --help' for command-specific options.")
{
    CreateCommand.Build(),
    ExtractCommand.Build(),
    ValidateCommand.Build(),
    LsCommand.Build(),
    InspectCommand.Build(),
};

if (args.Length == 0)
    args = ["--help"];

return await rootCommand.InvokeAsync(args);
