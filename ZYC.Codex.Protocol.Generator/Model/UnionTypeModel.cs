namespace ZYC.Codex.Protocol.Generator.Model;

public record UnionTypeModel(
    string Name,
    string? DiscriminatorProperty,
    IReadOnlyList<UnionMemberModel> Members,
    string? Description = null,
    bool HasValueSemantics = false) : TypeModel(Name, Description);
