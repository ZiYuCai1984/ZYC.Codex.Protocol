namespace ZYC.Codex.Protocol.Generator.Model;

public record PropertyModel(
    string JsonName,
    string CSharpName,
    TypeReference Type,
    bool Required,
    string? Description,
    string? Constant = null);