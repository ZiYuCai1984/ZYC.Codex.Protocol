using System.Text.Json;

namespace ZYC.Codex.Protocol.Generator.Model;

// Matching data describes the wire shape; it contains no schema objects or C# expressions.
public record PropertyMatch(string[] Path, string[]? Values = null, bool Exists = true);

public record UnionMemberModel(
    string TypeName,
    string DiscriminatorValue,
    JsonValueKind Kind,
    IReadOnlyList<PropertyMatch> Tests,
    TypeReference? Payload = null,
    string? Literal = null,
    string? Description = null);