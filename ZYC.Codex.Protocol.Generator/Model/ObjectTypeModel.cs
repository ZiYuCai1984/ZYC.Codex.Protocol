namespace ZYC.Codex.Protocol.Generator.Model;

public record ObjectTypeModel(
    string Name,
    IReadOnlyList<PropertyModel> Properties,
    string? Description = null,
    string? BaseType = null,
    string? ExtensionDataName = null,
    bool Closed = false) : TypeModel(Name, Description);