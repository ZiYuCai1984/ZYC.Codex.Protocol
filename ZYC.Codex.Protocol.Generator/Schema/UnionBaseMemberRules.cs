namespace ZYC.Codex.Protocol.Generator.Schema;

// Tagged unions whose base class exposes the listed wire properties as abstract members, so callers can read
// or assign them without knowing the concrete branch (for example, a transport assigning request ids).
// Every branch must declare each listed property as required with the same type; otherwise generation stops.
internal static class UnionBaseMemberRules
{
    private static Dictionary<string, string[]> Members { get; } = new(StringComparer.Ordinal)
    {
        ["ClientRequest"] = ["id", "method"]
    };

    internal static IReadOnlyCollection<string> For(string unionOwner)
    {
        return Members.TryGetValue(unionOwner, out var members) ? members : [];
    }
}
