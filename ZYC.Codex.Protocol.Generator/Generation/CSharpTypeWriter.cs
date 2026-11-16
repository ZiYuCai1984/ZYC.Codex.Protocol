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
                if (p is { IsOverride: true, Constant: { } overridden, ConstantMemberName: { } member })
                {
                    // An override cannot add the setter that the get-only base member lacks, so a private
                    // property carries the JSON value and keeps the deserialization check.
                    writer.Line("[JsonIgnore]");
                    writer.Line($"public override string {p.CSharpName} => {CodeWriter.Literal(overridden)};");
                    writer.Line();
                    writer.Line($"[JsonPropertyName({CodeWriter.Literal(p.JsonName)})]");
                    ConstantProperty(writer, model, p, $"private string {member}", "set");
                }
                else if (p.Constant != null)
                {
                    writer.Line($"[JsonPropertyName({CodeWriter.Literal(p.JsonName)})]");
                    ConstantProperty(writer, model, p, $"public string {p.CSharpName}", "private set");
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

    private static void ConstantProperty(CodeWriter writer, ObjectTypeModel model, PropertyModel p,
        string declaration, string setter)
    {
        var constant = p.Constant!;
        writer.Line("[JsonInclude]");
        writer.Line("[JsonRequired]");
        writer.Line("[JsonIgnore(Condition = JsonIgnoreCondition.Never)]");
        writer.Line(declaration);
        writer.Line("{");
        using (writer.Indent())
        {
            writer.Line($"get => {CodeWriter.Literal(constant)};");
            writer.Line(setter);
            writer.Line("{");
            using (writer.Indent())
            {
                writer.Line($"if (value != {CodeWriter.Literal(constant)})");
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
