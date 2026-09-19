using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Generation;

internal static class CSharpUnionWriter
{
    internal static void Base(CodeWriter writer, UnionTypeModel model)
    {
        writer.Summary(model.Description);
        writer.Line($"[JsonConverter(typeof({model.Name}JsonConverter))]");
        writer.Line($"public abstract class {model.Name}" +
            (model.HasValueSemantics ? $" : IEquatable<{model.Name}>" : ""));
        writer.Line("{");
        using (writer.Indent())
        {
            writer.Line($"private protected {model.Name}() {{ }}");
            foreach (var m in model.Members.Where(m => m.Payload?.Kind == TypeKind.Primitive))
            {
                writer.Line(
                    $"public static implicit operator {model.Name}({CSharpTypeWriter.Type(m.Payload!)} value) => {CreateMember(model, m, "value")};");
            }

            if (model.HasValueSemantics)
            {
                writer.Line();
                Equality(writer, model);
            }
        }

        writer.Line("}");
    }

    internal static void Member(CodeWriter writer, UnionTypeModel model, UnionMemberModel member)
    {
        writer.Summary(member.Description);
        writer.Line($"[JsonConverter(typeof({member.TypeName}JsonConverter))]");
        writer.Line($"public class {member.TypeName} : {model.Name}");
        writer.Line("{");
        using (writer.Indent())
        {
            if (member.Payload is { } payload)
            {
                if (model.HasValueSemantics)
                {
                    writer.Line($"public {member.TypeName}({CSharpTypeWriter.Type(payload)} value)");
                    writer.Line("{");
                    using (writer.Indent())
                    {
                        writer.Line(payload.Name == "string"
                            ? "Value = value ?? throw new ArgumentNullException(nameof(value));"
                            : "Value = value;");
                    }

                    writer.Line("}");
                    writer.Line();
                    writer.Line($"public {CSharpTypeWriter.Type(payload)} Value {{ get; }}");
                }
                else
                {
                    writer.Line($"public {CSharpTypeWriter.Type(payload)} Value {{ get; set; }} = default!;");
                }
            }
        }

        writer.Line("}");
    }

    internal static string CreateMember(UnionTypeModel model, UnionMemberModel member, string value)
    {
        return model.HasValueSemantics
            ? $"new {member.TypeName}({value})"
            : $"new {member.TypeName} {{ Value = {value} }}";
    }

    private static void Equality(CodeWriter writer, UnionTypeModel model)
    {
        writer.Line($"public bool Equals({model.Name}? other)");
        writer.Line("{");
        using (writer.Indent())
        {
            writer.Line("if (ReferenceEquals(this, other))");
            writer.Line("    return true;");
            writer.Line("return (this, other) switch");
            writer.Line("{");
            using (writer.Indent())
            {
                foreach (var member in model.Members)
                {
                    writer.Line($"({member.TypeName} left, {member.TypeName} right) => EqualityComparer<{CSharpTypeWriter.Type(member.Payload!)}>.Default.Equals(left.Value, right.Value),");
                }

                writer.Line("_ => false");
            }

            writer.Line("};");
        }

        writer.Line("}");
        writer.Line();
        writer.Line($"public override bool Equals(object? obj) => obj is {model.Name} other && Equals(other);");
        writer.Line();
        writer.Line("public override int GetHashCode() => this switch");
        writer.Line("{");
        using (writer.Indent())
        {
            for (var i = 0; i < model.Members.Count; i++)
            {
                var member = model.Members[i];
                writer.Line($"{member.TypeName} value => unchecked((EqualityComparer<{CSharpTypeWriter.Type(member.Payload!)}>.Default.GetHashCode(value.Value) * 397) ^ {i + 1}),");
            }

            writer.Line("_ => base.GetHashCode()");
        }

        writer.Line("};");
        writer.Line();
        writer.Line($"public static bool operator ==({model.Name}? left, {model.Name}? right) =>");
        writer.Line("    ReferenceEquals(left, right) || (left is not null && left.Equals(right));");
        writer.Line($"public static bool operator !=({model.Name}? left, {model.Name}? right) => !(left == right);");
    }
}
