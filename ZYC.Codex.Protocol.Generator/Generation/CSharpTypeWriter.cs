using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Generation;

public static class CSharpTypeWriter
{
    public static string Type(TypeReference type)
    {
        return type.Kind switch
        {
            TypeKind.Primitive or TypeKind.Named => type.Name!,
            TypeKind.Nullable => Type(type.Element!) + "?",
            TypeKind.Array => $"List<{Type(type.Element!)}>",
            TypeKind.Dictionary => $"Dictionary<string, {Type(type.Element!)}>",
            TypeKind.Any => "JsonElement",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    internal static void Imports(TypeReference type, ISet<string> imports)
    {
        if (type.Kind is TypeKind.Array or TypeKind.Dictionary)
        {
            imports.Add("System.Collections.Generic");
        }

        if (type.Kind == TypeKind.Any)
        {
            imports.Add("System.Text.Json");
        }

        if (type.Name is "Guid" or "DateTimeOffset")
        {
            imports.Add("System");
        }

        if (type.Element != null)
        {
            Imports(type.Element, imports);
        }
    }

    internal static void Object(CodeWriter writer, ObjectTypeModel model)
    {
        writer.Summary(model.Description);
        if (model.Closed)
        {
            writer.Line("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]");
        }

        writer.Line($"public class {model.Name}" + (model.BaseType is { } parent ? $" : {parent}" : ""));
        writer.Line("{");
        using (writer.Indent())
        {
            foreach (var p in model.Properties)
            {
                writer.Summary(p.Description);
                if (p is { IsOverride: true, Constant: not null, ConstantMemberName: { } member })
                {
                    // An override cannot add the setter that the get-only base member lacks, so a private
                    // property carries the JSON value and keeps the deserialization check.
                    writer.Line("[JsonIgnore]");
                    writer.Line($"public override {Type(p.Type)} {p.CSharpName} => {ConstantLiteral(p)};");
                    writer.Line();
                    writer.Line($"[JsonPropertyName({CodeWriter.Literal(p.JsonName)})]");
                    ConstantProperty(writer, model, p, $"private {Type(p.Type)} {member}", "set");
                }
                else if (p.Constant != null)
                {
                    writer.Line($"[JsonPropertyName({CodeWriter.Literal(p.JsonName)})]");
                    ConstantProperty(writer, model, p, $"public {Type(p.Type)} {p.CSharpName}", "private set");
                }
                else
                {
                    writer.Line($"[JsonPropertyName({CodeWriter.Literal(p.JsonName)})]");
                    if (!p.Required)
                    {
                        writer.Line("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]");
                    }
                    else
                    {
                        writer.Line("[JsonRequired]");
                        writer.Line("[JsonIgnore(Condition = JsonIgnoreCondition.Never)]");
                    }

                    var propertyType = p.Required ? Type(p.Type) : $"Optional<{Type(p.Type)}>";
                    writer.Line(
                        $"public {(p.IsOverride ? "override " : "")}{propertyType} {p.CSharpName} {{ get; set; }}{(p.Required ? " = default!;" : "")}");
                }

                writer.Line();
            }

            if (model.ExtensionDataName is { } extension)
            {
                writer.Line("[JsonExtensionData]");
                writer.Line($"public Dictionary<string, JsonElement>? {extension} {{ get; set; }}");
            }
        }

        writer.Line("}");
    }

    // String constants are quoted; integer constants keep their JSON number text as the C# literal.
    private static string ConstantLiteral(PropertyModel p)
    {
        return p.Type is { Kind: TypeKind.Primitive, Name: "string" } ? CodeWriter.Literal(p.Constant!) : p.Constant!;
    }

    private static void ConstantProperty(CodeWriter writer, ObjectTypeModel model, PropertyModel p,
        string declaration, string setter)
    {
        var constant = p.Constant!;
        var literal = ConstantLiteral(p);
        writer.Line("[JsonInclude]");
        writer.Line("[JsonRequired]");
        writer.Line("[JsonIgnore(Condition = JsonIgnoreCondition.Never)]");
        writer.Line(declaration);
        writer.Line("{");
        using (writer.Indent())
        {
            writer.Line($"get => {literal};");
            writer.Line(setter);
            writer.Line("{");
            using (writer.Indent())
            {
                writer.Line($"if (value != {literal})");
                writer.Line("{");
                using (writer.Indent())
                {
                    writer.Line($"throw new JsonException({CodeWriter.Literal($"Expected {model.Name}.{p.JsonName} to be {constant}.")});");
                }

                writer.Line("}");
            }

            writer.Line("}");
        }

        writer.Line("}");
    }

    internal static void Enum(CodeWriter writer, EnumTypeModel model)
    {
        writer.Summary(model.Description);
        writer.Line($"[JsonConverter(typeof({model.Name}JsonConverter))]");
        writer.Line($"public enum {model.Name}");
        writer.Line("{");
        using (writer.Indent())
        {
            foreach (var value in model.Values)
            {
                writer.Line(value.Name + ",");
            }
        }

        writer.Line("}");
    }
}
