using ZYC.Codex.Protocol.Generator.Publishing;
using ZYC.CoreToolkit;

namespace ZYC.Codex.Protocol.Generator;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var repositoryRoot = ProjectTools.GetSlnxFolderPath()
                             ?? throw new InvalidOperationException("Cannot locate the solution directory.");
        var build = new ProtocolBuild(repositoryRoot);
        var release = new ProtocolRelease(build);

        switch (args)
        {
            case []:
#if PUSH_NUGET
                // Preserve the original push-nuget.yml invocation.
                await release.PublishAsync();
#else
                await release.GenerateAndPackAsync();
#endif
                break;
            case ["--generate-only"]:
                await release.GenerateOnlyAsync();
                break;
            case ["--check-update"]:
                await release.CheckAsync();
                break;
            case ["--update"]:
                await release.UpdateAsync();
                break;
            case ["--update", var codexVersion]:
                await release.UpdateAsync(codexVersion);
                break;
            case ["--publish"]:
                await release.PublishAsync();
                break;
            case ["--update-and-publish"]:
                await release.UpdateAndPublishAsync();
                break;
            default:
                throw new ArgumentException(
                    "Usage: ZYC.Codex.Protocol.Generator " +
                    "[--generate-only | --check-update | --update [CodexVersion] | --publish | --update-and-publish]");
        }
    }
}
