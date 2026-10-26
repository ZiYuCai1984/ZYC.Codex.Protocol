using System.Diagnostics;
using System.Security;
using System.Text;
using ZYC.Codex.Protocol.Generator.Generation;
using ZYC.Codex.Protocol.Generator.Schema;
using ZYC.CoreToolkit;
using ZYC.CoreToolkit.SimpleExec;
using ProtocolProductInfo = ZYC.Codex.Protocol.ProductInfo;

namespace ZYC.Codex.Protocol.Generator.Publishing;

internal class ProtocolBuild
{
    public ProtocolBuild(string repositoryRoot)
    {
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public string RepositoryRoot { get; }
    public string GeneratorDirectory => Path.Combine(RepositoryRoot, "ZYC.Codex.Protocol.Generator");
    public string ProtocolDirectory => Path.Combine(RepositoryRoot, "ZYC.Codex.Protocol");
    public string SchemaDirectory => Path.Combine(GeneratorDirectory, "json-schema");
    private string ProtocolProject => Path.Combine(ProtocolDirectory, "ZYC.Codex.Protocol.csproj");
    private string OutputDirectory => Path.Combine(RepositoryRoot, "_bin");

    public async Task GenerateAsync(string version, string codexVersion, string? schemaDirectory = null)
    {
        Console.WriteLine($"Generating protocol code for Codex '{codexVersion}' (package {version})...");
        var productInfo = await File.ReadAllTextAsync(Path.Combine(GeneratorDirectory, "ProductInfo.cs"));
        productInfo = productInfo.Replace(ProtocolProductInfo.CodexVersion, codexVersion, StringComparison.Ordinal);
        var props =
            "<!--auto-generated-->\r\n" +
            "<Project>\r\n" +
            "  <PropertyGroup>\r\n" +
            $"    <Version>{SecurityElement.Escape(version)}</Version>\r\n" +
            $"    <Description>{SecurityElement.Escape(ProtocolProductInfo.Description)}</Description>\r\n" +
            "  </PropertyGroup>\r\n" +
            "</Project>";
        var schema = await SchemaLoader.LoadAsync(
            Path.Combine(schemaDirectory ?? SchemaDirectory, "codex_app_server_protocol.schemas.json"));
        var types = new SchemaAnalyzer(schema).Analyze();
        new CSharpGenerator().Write(types, ProtocolDirectory);
        await File.WriteAllTextAsync(Path.Combine(RepositoryRoot, "_version.props"), props);
        await File.WriteAllTextAsync(Path.Combine(ProtocolDirectory, "ProductInfo.cs"),
            productInfo.ReplaceLineEndings("\r\n"), new UTF8Encoding(true));
        Console.WriteLine($"Generated protocol code and metadata for {version}.");
    }

    public async Task<(string PackageVersion, string CodexVersion)> ReadVersionsAsync()
    {
        // Read the generated source, not metadata compiled into the running generator.
        var productInfo = await File.ReadAllTextAsync(Path.Combine(ProtocolDirectory, "ProductInfo.cs"));
        var codexVersion = ReadProperty(productInfo, "CodexVersion");
        var version = ReadProperty(productInfo, "Version").Replace("{CodexVersion}", codexVersion, StringComparison.Ordinal);
        return (version, codexVersion);
    }

    public Task BuildAsync()
    {
        Console.WriteLine("Building the protocol project...");
        return RunAsync("dotnet", "build", ProtocolProject, "-c", "Release");
    }

    public Task TestAsync()
    {
        Console.WriteLine("Running generator and protocol tests...");
        // Reuse the existing project outputs to avoid overwriting the running generator.
        return RunAsync("dotnet", "test",
            Path.Combine(RepositoryRoot, "ZYC.Codex.Protocol.Generator.Tests", "ZYC.Codex.Protocol.Generator.Tests.csproj"),
            "-c", "Release", "-p:BuildProjectReferences=false");
    }

    public async Task PackAsync(string version, string? commit = null)
    {
        Console.WriteLine($"Packing ZYC.Codex.Protocol {version}...");
        var arguments = new List<string>
        {
            "pack", ProtocolProject, "-c", "Release", "--no-build", "--no-restore",
            "-o", OutputDirectory,
            $"-p:Version={version}", $"-p:PackageVersion={version}"
        };
        if (commit != null)
        {
            arguments.Add($"-p:RepositoryCommit={commit}");
        }

        await RunAsync("dotnet", arguments.ToArray());
        if (!File.Exists(GetPackagePath(version)))
        {
            throw new FileNotFoundException("Expected NuGet package was not produced.", GetPackagePath(version));
        }

        Console.WriteLine($"Package created: {GetPackagePath(version)}");
    }

    public async Task PublishAsync(string version)
    {
        Console.WriteLine($"Publishing ZYC.Codex.Protocol {version}...");
        var apiKey = await NuGetTrustedPublishingTools.GetApiKeyAsync("ZhuJianYun");
        await RunAsync("dotnet", "nuget", "push", GetPackagePath(version),
            "--source", "https://api.nuget.org/v3/index.json", "--api-key", apiKey, "--skip-duplicate");
    }

    public async Task RunAsync(string command, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo(command)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        await Task.WhenAll(
            ForwardOutputAsync(process.StandardOutput, Console.Out),
            ForwardOutputAsync(process.StandardError, Console.Error),
            process.WaitForExitAsync());
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} failed with exit code {process.ExitCode}.");
        }
    }

    public async Task<string> ReadAsync(string command, params string[] arguments)
    {
        var result = await Command.ReadAsync(command, arguments, workingDirectory: RepositoryRoot);
        return result.StandardOutput.Trim();
    }

    private string GetPackagePath(string version)
    {
        return Path.Combine(OutputDirectory, $"ZYC.Codex.Protocol.{version}.nupkg");
    }

    private static async Task ForwardOutputAsync(StreamReader source, TextWriter destination)
    {
        while (await source.ReadLineAsync() is { } line)
        {
            await destination.WriteLineAsync(line);
        }
    }

    private static string ReadProperty(string content, string name)
    {
        return content.Split('\n')
            .Single(line => line.TrimStart().StartsWith($"public static string {name} =>", StringComparison.Ordinal))
            .Split('"')[1];
    }
}
