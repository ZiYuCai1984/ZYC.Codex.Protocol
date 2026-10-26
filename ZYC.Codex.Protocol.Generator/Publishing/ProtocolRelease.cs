using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZYC.CoreToolkit;
using ProtocolProductInfo = ZYC.Codex.Protocol.ProductInfo;

namespace ZYC.Codex.Protocol.Generator.Publishing;

internal class ProtocolRelease
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public ProtocolRelease(ProtocolBuild build)
    {
        Build = build;
    }

    private ProtocolBuild Build { get; }
    private static Regex StableVersionPattern { get; } = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z");

    public async Task<ProtocolReleasePlan> CheckAsync(string? codexVersion = null)
    {
        string packageVersion;
        (codexVersion, packageVersion) = await ResolveVersionsAsync(codexVersion);
        var current = await Build.ReadVersionsAsync();
        var upstreamVersion = ParseStableVersion(codexVersion);
        if (current.CodexVersion.Length > 0 && upstreamVersion < ParseStableVersion(current.CodexVersion))
        {
            throw new InvalidOperationException(
                $"Refusing to downgrade Codex {current.CodexVersion} to {codexVersion}.");
        }

        Console.WriteLine($"Checking NuGet package {packageVersion}...");
        using var request = new HttpRequestMessage(HttpMethod.Head,
            $"https://api.nuget.org/v3-flatcontainer/zyc.codex.protocol/{packageVersion}/zyc.codex.protocol.nuspec");
        using var response = await Client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        var plan = new ProtocolReleasePlan(codexVersion, packageVersion,
            codexVersion != current.CodexVersion || packageVersion != current.PackageVersion,
            response.StatusCode == HttpStatusCode.NotFound);
        Console.WriteLine($"Codex: '{current.CodexVersion}' -> '{plan.CodexVersion}'; " +
                          $"package: {plan.PackageVersion}; update: {plan.UpdateRequired}; publish: {plan.PublishRequired}");
        return plan;
    }

    public async Task<string> UpdateAsync(string? codexVersion = null)
    {
        var versions = await ResolveVersionsAsync(codexVersion);
        await UpdateSourceAsync(versions.CodexVersion, versions.PackageVersion);
        return versions.PackageVersion;
    }

    public async Task GenerateAndPackAsync()
    {
        Console.WriteLine("Starting local protocol generation...");
        var packageVersion = await UpdateAsync();
        await Build.BuildAsync();
        await Build.PackAsync(packageVersion);
        Console.WriteLine("Local protocol generation completed.");
    }

    public async Task GenerateOnlyAsync()
    {
        var current = await Build.ReadVersionsAsync();
        if (current.CodexVersion.Length == 0)
        {
            throw new InvalidOperationException(
                "The checked-in schema has no recorded Codex version. Run without arguments or use --update first.");
        }

        await Build.GenerateAsync(GetPackageVersion(current.CodexVersion), current.CodexVersion);
    }

    public Task PublishAsync()
    {
        return ReleaseAsync(false);
    }

    public Task UpdateAndPublishAsync()
    {
        return ReleaseAsync(true);
    }

    private async Task ReleaseAsync(bool pushRepository)
    {
        var plan = await CheckAsync();
        if (!plan.Required)
        {
            Console.WriteLine("The protocol and NuGet package are already current.");
            return;
        }

        string? branch = null;
        if (pushRepository)
        {
            branch = Environment.GetEnvironmentVariable("DEFAULT_BRANCH");
            if (string.IsNullOrWhiteSpace(branch))
            {
                throw new InvalidOperationException("DEFAULT_BRANCH must name the branch to update and push.");
            }

            await Build.RunAsync("git", "check-ref-format", $"refs/heads/{branch}");
            if (await Build.ReadAsync("git", "branch", "--show-current") != branch)
            {
                throw new InvalidOperationException($"Check out the target branch '{branch}' before updating it.");
            }

            if ((await Build.ReadAsync("git", "status", "--porcelain")).Length > 0)
            {
                throw new InvalidOperationException("Automatic commits require a clean working tree.");
            }
        }

        if (plan.UpdateRequired)
        {
            await UpdateSourceAsync(plan.CodexVersion, plan.PackageVersion);
        }
        else
        {
            await Build.GenerateAsync(plan.PackageVersion, plan.CodexVersion);
        }

        await Build.BuildAsync();

        string? commit = null;
        if (pushRepository)
        {
            await Build.TestAsync();
            await CommitAsync(plan);
            commit = await Build.ReadAsync("git", "rev-parse", "HEAD");
        }

        await Build.PackAsync(plan.PackageVersion, commit);
        if (pushRepository)
        {
            // A rejected push stops publication. Never force-push or rebase untested code.
            await Build.RunAsync("git", "push", "origin", $"HEAD:refs/heads/{branch}");
        }

        if (plan.PublishRequired)
        {
            await Build.PublishAsync(plan.PackageVersion);
        }
    }

    private async Task UpdateSourceAsync(string codexVersion, string packageVersion)
    {
        var readmePath = Path.Combine(Build.RepositoryRoot, "README.md");
        var table =
            "<!-- codex-version:start -->\n" +
            "| Component | Version |\n" +
            "| --- | --- |\n" +
            $"| Codex CLI / schema | [{codexVersion}](https://www.npmjs.com/package/@openai/codex/v/{codexVersion}) |\n" +
            $"| NuGet package | [{packageVersion}](https://www.nuget.org/packages/ZYC.Codex.Protocol/{packageVersion}) |\n" +
            "<!-- codex-version:end -->";
        var readme = ReplaceOnce(await File.ReadAllTextAsync(readmePath),
            "(?s)<!-- codex-version:start -->.*?<!-- codex-version:end -->", table);

        var stagingDirectory = Path.Combine(Build.RepositoryRoot, $"_codex-schema-{Guid.NewGuid():N}");
        EnsureRepositoryPath(stagingDirectory);
        EnsureRepositoryPath(Build.SchemaDirectory);
        try
        {
            await ExportSchemaAsync(codexVersion, stagingDirectory);
            await Build.GenerateAsync(packageVersion, codexVersion, stagingDirectory);

            if (Directory.Exists(Build.SchemaDirectory))
            {
                Directory.Delete(Build.SchemaDirectory, true);
            }

            Directory.Move(stagingDirectory, Build.SchemaDirectory);
            await WriteSourceAsync(readmePath, readme);
            Console.WriteLine($"Updated schema snapshot and README for Codex {codexVersion}.");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    private async Task ExportSchemaAsync(string codexVersion, string stagingDirectory)
    {
        Console.WriteLine($"Exporting JSON schema with Codex {codexVersion}...");
        var exitCode = await CommandTools.ExecuteCommandAsync(
            $"npx --yes --package=@openai/codex@{codexVersion} codex app-server generate-json-schema --out \"{stagingDirectory}\"");
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Codex {codexVersion} schema export failed (exit code {exitCode}).");
        }

        foreach (var bundle in new[]
                     { "codex_app_server_protocol.schemas.json", "codex_app_server_protocol.v2.schemas.json" })
        {
            if (!File.Exists(Path.Combine(stagingDirectory, bundle)))
            {
                throw new InvalidOperationException($"Codex schema export did not produce {bundle}.");
            }
        }
    }

    private async Task CommitAsync(ProtocolReleasePlan plan)
    {
        await Build.RunAsync("git", "config", "user.name", "github-actions[bot]");
        await Build.RunAsync("git", "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com");
        await Build.RunAsync("git", "add", "--", "README.md",
            "ZYC.Codex.Protocol.Generator/json-schema", "ZYC.Codex.Protocol/ProductInfo.cs",
            ":(glob)ZYC.Codex.Protocol/*.g.cs");
        if ((await Build.ReadAsync("git", "diff", "--cached", "--name-only")).Length > 0)
        {
            await Build.RunAsync("git", "commit", "-m",
                $"chore: sync Codex {plan.CodexVersion} ({plan.PackageVersion})");
        }
    }

    private static async Task<(string CodexVersion, string PackageVersion)> ResolveVersionsAsync(string? codexVersion)
    {
        if (codexVersion == null)
        {
            Console.WriteLine("Resolving the latest stable Codex version...");
            using var metadata = JsonDocument.Parse(
                await Client.GetStringAsync("https://registry.npmjs.org/@openai%2Fcodex/latest"));
            codexVersion = metadata.RootElement.GetProperty("version").GetString()
                           ?? throw new InvalidOperationException(
                               "The npm latest tag did not contain a Codex version.");
        }

        ParseStableVersion(codexVersion);
        var packageVersion = GetPackageVersion(codexVersion);
        Console.WriteLine($"Using Codex {codexVersion}; package version: {packageVersion}.");
        return (codexVersion, packageVersion);
    }

    private static string GetPackageVersion(string codexVersion)
    {
        ParseStableVersion(codexVersion);
        return ProtocolProductInfo.Version.Replace(ProtocolProductInfo.CodexVersion, codexVersion, StringComparison.Ordinal);
    }

    private void EnsureRepositoryPath(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Build.RepositoryRoot) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Schema directory is outside the repository: {path}");
        }
    }

    private static Version ParseStableVersion(string version)
    {
        if (!StableVersionPattern.IsMatch(version))
        {
            throw new InvalidOperationException($"Expected a stable major.minor.patch version: {version}");
        }

        return Version.Parse(version);
    }

    private static string ReplaceOnce(string content, string pattern, string replacement)
    {
        var regex = new Regex(pattern);
        if (regex.Matches(content).Count != 1)
        {
            throw new InvalidOperationException($"Expected exactly one release metadata marker matching: {pattern}");
        }

        return regex.Replace(content, _ => replacement);
    }

    private static Task WriteSourceAsync(string path, string content)
    {
        return File.WriteAllTextAsync(path, content.ReplaceLineEndings("\r\n"), new UTF8Encoding(true));
    }
}
