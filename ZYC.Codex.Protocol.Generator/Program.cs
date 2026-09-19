using ZYC.Codex.Protocol.Generator.Generation;
using ZYC.Codex.Protocol.Generator.Schema;
using ZYC.CoreToolkit;
using ZYC.CoreToolkit.Dotnet;
using ProtocolProductInfo = ZYC.Codex.Protocol.ProductInfo;


namespace ZYC.Codex.Protocol.Generator;

internal class Program
{
    private static string BuildVersion => ProtocolProductInfo.Version;

    private static string VersionPropsFile => Path.Combine(ProjectRootFolder, "_version.props");

    private static string ProjectRootFolder => ProjectTools.GetSlnFolderPath()!;

    private static string ProtocolProjectFolder => Path.Combine(ProjectRootFolder, "ZYC.Codex.Protocol");

    private static string ProtocolCsprojFile => Path.Combine(ProtocolProjectFolder, "ZYC.Codex.Protocol.csproj");

    private static string OutputFolder => Path.Combine(ProjectRootFolder, "_bin");

    private static string GeneratorProjectFolder => Path.Combine(ProjectRootFolder, "ZYC.Codex.Protocol.Generator");


    public static void UpdateVersionProps()
    {
        var props =
            "<!--auto-generated-->\r\n" +
            "<Project>\r\n" +
            "  <PropertyGroup>\r\n" +
            $"    <Version>{BuildVersion}</Version>\r\n" +
            "  </PropertyGroup>\r\n" +
            "</Project>";

        File.WriteAllText(VersionPropsFile, props);
    }


    private static async Task Main()
    {
        IOTools.SetCurrentDirectoryToEntryScriptFileDirectory();

        UpdateVersionProps();

        var folder = "json-schema";

        var result = await CommandTools.ExecuteCommandAsync($"codex app-server generate-json-schema --out ./{folder}");
        if (result != 0)
        {
            throw new InvalidOperationException("Failed to generate JSON schema.");
        }

        var schemaFile = Path.Combine(IOTools.CurrentDirectory, folder, "codex_app_server_protocol.schemas.json");
        var schema = await SchemaLoader.LoadAsync(schemaFile);


        var types = new SchemaAnalyzer(schema).Analyze();
        new CSharpGenerator().Write(types, ProtocolProjectFolder);

        IOTools.CopyFile(
            Path.Combine(GeneratorProjectFolder, "ProductInfo.cs"),
            Path.Combine(ProtocolProjectFolder, "ProductInfo.cs"));

        await CommandTools.ExecuteCommandAsync($"dotnet build {ProtocolCsprojFile} -c release");

        await DotnetNuGetTools.PackProjectNoRestoreNoBuildAsync(
            ProtocolCsprojFile,
            OutputFolder,
            BuildVersion
        );

#if PUSH_NUGET
        var apiKey = await NuGetTrustedPublishingTools.GetApiKeyAsync("ZhuJianYun");
        await DotnetNuGetTools.PushNuGetAsync(
            OutputFolder,
            "https://api.nuget.org/v3/index.json",
            apiKey,
            null,
            false);

#endif
    }
}