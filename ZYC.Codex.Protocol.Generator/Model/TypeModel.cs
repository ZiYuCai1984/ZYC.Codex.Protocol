namespace ZYC.Codex.Protocol.Generator.Model;

public abstract record TypeModel(string Name, string? Description);

public record NullTypeModel(string Name) : TypeModel(Name, null);

public enum TypeKind
{
    Primitive,
    Named,
    Nullable,
    Array,
    Dictionary,
    Any
}

public record TypeReference(TypeKind Kind, string? Name = null, TypeReference? Element = null)
{
    public TypeReference AsNullable()
    {
        return Kind == TypeKind.Nullable ? this : new TypeReference(TypeKind.Nullable, Element: this);
    }
}