using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Generation;

internal static class CSharpConverterWriter
{
    private static string Q(string value)
    {
        return CodeWriter.Literal(value);
    }

    internal static void Null(CodeWriter w, NullTypeModel model)
    {
        w.Line($"public class {model.Name}JsonConverter : JsonConverter<{model.Name}>");
        w.Line("{");
        using (w.Indent())
        {
            w.Line(
                $"public override {model.Name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>");
            w.Line(
                "    reader.TokenType == JsonTokenType.Null ? default : throw new JsonException(\"Expected null.\");");
            w.Line(
                $"public override void Write(Utf8JsonWriter writer, {model.Name} value, JsonSerializerOptions options) => writer.WriteNullValue();");
        }

        w.Line("}");
    }

    internal static void Enum(CodeWriter w, EnumTypeModel model)
    {
        var name = model.Name;
        w.Line($"public class {name}JsonConverter : JsonConverter<{name}>");
        w.Line("{");
        using (w.Indent())
        {
            w.Line($"private static {name} Parse(string? value) => value switch");
            w.Line("{");
            using (w.Indent())
            {
                foreach (var v in model.Values)
                {
                    w.Line($"{Q(v.JsonValue)} => {name}.{v.Name},");
                }

                w.Line($"_ => throw new JsonException({Q("Unknown " + name + " value: ")} + value)");
            }

            w.Line("};");
            w.Line($"private static string Format({name} value) => value switch");
            w.Line("{");
            using (w.Indent())
            {
                foreach (var v in model.Values)
                {
                    w.Line($"{name}.{v.Name} => {Q(v.JsonValue)},");
                }

                w.Line($"_ => throw new JsonException({Q("Unknown " + name + " value: ")} + value)");
            }

            w.Line("};");
            w.Line(
                $"public override {name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>");
            w.Line(
                $"    reader.TokenType == JsonTokenType.String ? Parse(reader.GetString()) : throw new JsonException({Q("Expected a string for " + name + ".")});");
            w.Line(
                $"public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options) => writer.WriteStringValue(Format(value));");
            w.Line(
                $"public override {name} ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());");
            w.Line(
                $"public override void WriteAsPropertyName(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options) => writer.WritePropertyName(Format(value));");
        }

        w.Line("}");
    }

    internal static void Union(CodeWriter w, UnionTypeModel model)
    {
        w.Line($"public class {model.Name}JsonConverter : JsonConverter<{model.Name}>");
        w.Line("{");
        using (w.Indent())
        {
            w.Line(
                $"public override {model.Name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            w.Line("{");
            using (w.Indent())
            {
                w.Line("using var document = JsonDocument.ParseValue(ref reader);");
                w.Line("var root = document.RootElement;");
                if (model.DiscriminatorProperty is { } property)
                {
                    w.Line(
                        $"if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty({Q(property)}, out var discriminator) || discriminator.ValueKind != JsonValueKind.String)");
                    w.Line(
                        $"    throw new JsonException({Q("Missing or invalid " + model.Name + " " + property + ".")});");
                    w.Line("return discriminator.GetString() switch");
                    w.Line("{");
                    using (w.Indent())
                    {
                        foreach (var m in model.Members)
                        {
                            w.Line($"{Q(m.DiscriminatorValue)} => {Read(model, m)},");
                        }

                        w.Line(
                            $"_ => throw new JsonException({Q("Unknown " + model.Name + " " + property + " '")} + discriminator.GetString() + {Q("'.")})");
                    }

                    w.Line("};");
                }
                else
                {
                    var index = 0;
                    foreach (var m in model.Members)
                    {
                        var conditions = new List<string> { $"root.ValueKind == JsonValueKind.{m.Kind}" };
                        if (m.Literal is { } literal)
                        {
                            conditions.Add($"root.GetString() == {Q(literal)}");
                        }

                        foreach (var test in m.Tests)
                        {
                            var parent = "root";
                            for (var i = 0; i < test.Path.Length; i++)
                            {
                                var variable = "property" + index++;
                                var last = i == test.Path.Length - 1;
                                var read = $"{parent}.TryGetProperty({Q(test.Path[i])}, out var {variable})";
                                conditions.Add(last && !test.Exists ? "!" + read : read);
                                if (!last)
                                {
                                    conditions.Add($"{variable}.ValueKind == JsonValueKind.Object");
                                }
                                else if (test.Values is { } values)
                                {
                                    conditions.Add($"{variable}.ValueKind == JsonValueKind.String");
                                    conditions.Add("(" + string.Join(" || ",
                                        values.Select(v => $"{variable}.GetString() == {Q(v)}")) + ")");
                                }

                                parent = variable;
                            }
                        }

                        w.Line($"if ({string.Join(" && ", conditions)})");
                        w.Line($"    return {Read(model, m)};");
                    }

                    w.Line($"throw new JsonException({Q("Unknown " + model.Name + " wire shape or value.")});");
                }
            }

            w.Line("}");
            w.Line();
            w.Line(
                $"public override void Write(Utf8JsonWriter writer, {model.Name} value, JsonSerializerOptions options)");
            w.Line("{");
            using (w.Indent())
            {
                w.Line("switch (value)");
                w.Line("{");
                using (w.Indent())
                {
                    foreach (var m in model.Members)
                    {
                        w.Line($"case {m.TypeName} concrete:");
                        using (w.Indent())
                        {
                            Write(w, m);
                            w.Line("break;");
                        }
                    }

                    w.Line($"default: throw new JsonException({Q("Unknown runtime type for " + model.Name + ".")});");
                }

                w.Line("}");
            }

            w.Line("}");
        }

        w.Line("}");
    }

    private static string Read(UnionTypeModel model, UnionMemberModel member)
    {
        return member.Literal != null
            ? $"new {member.TypeName}()"
            : member.Payload is { } type
                ? CSharpUnionWriter.CreateMember(model, member, $"root.Deserialize<{CSharpTypeWriter.Type(type)}>(options)!")
                : $"root.Deserialize<{member.TypeName}>(options) ?? throw new JsonException({Q("Expected " + member.TypeName + ".")})";
    }

    private static void Write(CodeWriter w, UnionMemberModel member)
    {
        if (member.Literal is { } literal)
        {
            w.Line($"writer.WriteStringValue({Q(literal)});");
        }
        else
        {
            w.Line($"JsonSerializer.Serialize(writer, concrete{(member.Payload != null ? ".Value" : "")}, options);");
        }
    }

    internal static void Member(CodeWriter w, UnionTypeModel model, UnionMemberModel member)
    {
        w.Line($"public class {member.TypeName}JsonConverter : JsonConverter<{member.TypeName}>");
        w.Line("{");
        using (w.Indent())
        {
            w.Line(
                $"public override {member.TypeName} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>");
            w.Line(
                $"    JsonSerializer.Deserialize<{model.Name}>(ref reader, options) as {member.TypeName} ?? throw new JsonException({Q("Expected " + member.TypeName + ".")});");
            w.Line(
                $"public override void Write(Utf8JsonWriter writer, {member.TypeName} concrete, JsonSerializerOptions options)");
            w.Line("{");
            using (w.Indent())
            {
                Write(w, member);
            }

            w.Line("}");
        }

        w.Line("}");
    }
}
