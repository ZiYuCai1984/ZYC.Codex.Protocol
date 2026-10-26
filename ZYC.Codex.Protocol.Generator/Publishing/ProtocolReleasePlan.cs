namespace ZYC.Codex.Protocol.Generator.Publishing;

internal class ProtocolReleasePlan
{
    public ProtocolReleasePlan(string codexVersion, string packageVersion, bool updateRequired, bool publishRequired)
    {
        CodexVersion = codexVersion;
        PackageVersion = packageVersion;
        UpdateRequired = updateRequired;
        PublishRequired = publishRequired;
    }

    public string CodexVersion { get; }
    public string PackageVersion { get; }
    public bool UpdateRequired { get; }
    public bool PublishRequired { get; }
    public bool Required => UpdateRequired || PublishRequired;
}
