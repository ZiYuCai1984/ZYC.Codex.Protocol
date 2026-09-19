namespace ZYC.Codex.Protocol.Generator.Model;

public record EnumValueModel(string Name, string JsonValue);

public record EnumTypeModel(
    string Name,
    IReadOnlyList<EnumValueModel> Values,
    string? Description = null) : TypeModel(Name, Description);