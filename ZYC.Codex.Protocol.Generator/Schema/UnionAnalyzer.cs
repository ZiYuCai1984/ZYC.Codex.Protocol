using System.Text.Json;
using NJsonSchema;
using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Schema;

internal class UnionAnalyzer
{
    private SchemaAnalyzer Analyzer { get; }

    public UnionAnalyzer(SchemaAnalyzer analyzer)
    {
        Analyzer = analyzer;
    }

    internal static string[]? StringValues(JsonSchema schema)
    {
        return (schema.Type & ~JsonObjectType.Null) is JsonObjectType.String or JsonObjectType.None &&
               schema.Enumeration.Count > 0 && schema.Enumeration.All(x => x is string)
            ? schema.Enumeration.Cast<string>().ToArray()
            : null;
    }

    internal TypeReference Resolve(JsonSchema schema, IReadOnlyList<JsonSchema> branches)
    {
        var actual = branches.Select(b => b.ActualSchema).ToList();
        foreach (var branch in branches.Concat(actual))
        {
            Analyzer.Resolver.Check(branch);
        }

        if (schema.AllOf.Count > 0)
        {
            throw Analyzer.Document.Unsupported(schema, "Combined allOf and union.");
        }

        if (schema.Type is not (JsonObjectType.None or JsonObjectType.Object) &&
            !actual.All(b => StringValues(b) != null && schema.Type == JsonObjectType.String))
        {
            throw Analyzer.Document.Unsupported(schema, "Union has an unsupported sibling type constraint.");
        }

        if (actual.All(b => StringValues(b) != null) && schema.Properties.Count == 0)
        {
            var values = actual.SelectMany(b => StringValues(b)!).ToArray();
            if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            {
                throw Analyzer.Document.Unsupported(schema, "Duplicate string union value.");
            }

            return values.Length == 1
                ? new TypeReference(TypeKind.Primitive, "string")
                : Analyzer.Declare(schema, name => Analyzer.Enum(name, values, schema.Description));
        }

        if (actual.All(IsObject))
        {
            var properties = actual.Select(Analyzer.Properties).ToList();
            var discriminator = properties[0].Keys
                .OrderBy(p => p == "method" ? 0 : p == "type" ? 1 : 2).ThenBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault(p =>
                    properties.All(b =>
                        b.TryGetValue(p, out var property) && StringValues(property.ActualSchema) is [_]) &&
                    properties.Select(b => StringValues(b[p].ActualSchema)![0]).Distinct(StringComparer.Ordinal)
                        .Count() == actual.Count);
            if (discriminator != null)
            {
                var values = properties.Select(p => StringValues(p[discriminator].ActualSchema)![0]).ToList();
                if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
                {
                    throw Analyzer.Document.Unsupported(schema, $"Duplicate discriminator '{discriminator}'.");
                }

                return Analyzer.Declare(schema, name =>
                {
                    var members = actual.Zip(values).OrderBy(x => x.Second, StringComparer.Ordinal).Select(pair =>
                    {
                        var branchName = Analyzer.Allocate(pair.First.Title ?? name + "_" + pair.Second);
                        Analyzer.Add(Analyzer.Object(pair.First, branchName, name, schema));
                        return new UnionMemberModel(branchName, pair.Second, JsonValueKind.Object,
                            [new PropertyMatch([discriminator], [pair.Second])]);
                    }).ToList();
                    return new UnionTypeModel(name, discriminator, members, schema.Description);
                });
            }
        }

        // Rust's externally tagged enums use a string for a unit variant or a closed,
        // single-property object for a payload variant. These occur in ReviewDecision etc.
        if (schema.Properties.Count == 0 && actual.All(b => StringValues(b) != null ||
                                                            (IsObject(b) && b.Properties.Count == 1 &&
                                                             !b.AllowAdditionalProperties &&
                                                             b.RequiredProperties.SequenceEqual(b.Properties.Keys))))
        {
            return Analyzer.Declare(schema, name =>
            {
                var cases = actual.SelectMany(b => StringValues(b) is { } values
                        ? values.Select(v => (Branch: b, Tag: v, Literal: true))
                        : [(Branch: b, Tag: b.Properties.Keys.Single(), Literal: false)])
                    .OrderBy(x => x.Tag, StringComparer.Ordinal).ToList();
                if (cases.Select(x => x.Tag).Distinct(StringComparer.Ordinal).Count() != cases.Count)
                {
                    throw Analyzer.Document.Unsupported(schema, "Duplicate external enum tag.");
                }

                var members = cases.Select(c =>
                {
                    var memberName =
                        Analyzer.Allocate(c.Literal ? name + "_" + c.Tag : c.Branch.Title ?? name + "_" + c.Tag);
                    if (!c.Literal)
                    {
                        Analyzer.Add(Analyzer.Object(c.Branch, memberName, name));
                    }

                    return new UnionMemberModel(memberName, c.Tag,
                        c.Literal ? JsonValueKind.String : JsonValueKind.Object,
                        c.Literal ? [] : [new PropertyMatch([c.Tag])], Literal: c.Literal ? c.Tag : null,
                        Description: c.Branch.Description);
                }).ToList();
                return new UnionTypeModel(name, null, members, schema.Description);
            });
        }

        return new ProtocolUnionRules(Analyzer).Resolve(schema, actual);
    }

    private static bool IsObject(JsonSchema schema)
    {
        return schema.Type == JsonObjectType.Object ||
               (schema.Type == JsonObjectType.None && schema.Properties.Count + schema.AllOf.Count > 0);
    }
}
