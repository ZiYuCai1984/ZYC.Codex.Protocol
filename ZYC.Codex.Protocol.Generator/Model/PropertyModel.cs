namespace ZYC.Codex.Protocol.Generator.Model;

// IsOverride: the property implements an abstract member declared by its union base.
// ConstantMemberName: for an overridden constant, the private property that carries the JSON value and
// validates it, because an override cannot add the setter that the get-only base member lacks.
public record PropertyModel(
    string JsonName,
    string CSharpName,
    TypeReference Type,
    bool Required,
    string? Description,
    string? Constant = null,
    bool IsOverride = false,
    string? ConstantMemberName = null);