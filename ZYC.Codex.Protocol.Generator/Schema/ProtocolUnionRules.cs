using System.Text.Json;
using NJsonSchema;
using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Schema;

// Explicit exceptions for the untagged unions present in the checked-in protocol.
// New shapes must be reviewed here; there is deliberately no trial-deserialization fallback.
internal class ProtocolUnionRules
{
    public ProtocolUnionRules(SchemaAnalyzer analyzer)
    {
        Analyzer = analyzer;
    }

    private SchemaAnalyzer Analyzer { get; }

    private static Dictionary<string, string[]> McpGroups { get; } = new(StringComparer.Ordinal)
    {
        ["McpElicitationPrimitiveSchema"] =
        [
            "McpElicitationEnumSchema", "McpElicitationStringSchema", "McpElicitationNumberSchema",
            "McpElicitationBooleanSchema"
        ],
        ["McpElicitationEnumSchema"] =
        [
            "McpElicitationSingleSelectEnumSchema", "McpElicitationMultiSelectEnumSchema",
            "McpElicitationLegacyTitledEnumSchema"
        ],
        ["McpElicitationSingleSelectEnumSchema"] =
            ["McpElicitationUntitledSingleSelectEnumSchema", "McpElicitationTitledSingleSelectEnumSchema"],
        ["McpElicitationMultiSelectEnumSchema"] =
            ["McpElicitationUntitledMultiSelectEnumSchema", "McpElicitationTitledMultiSelectEnumSchema"]
    };

    internal TypeReference Resolve(JsonSchema schema, IReadOnlyList<JsonSchema> branches)
    {
        var owner = Analyzer.Document.Source(schema).Owner;
        if (schema.Properties.Count > 0)
        {
            throw Analyzer.Document.Unsupported(schema, "Untagged union with common properties.");
        }

        List<(JsonSchema Schema, string Tag, JsonValueKind Kind, PropertyMatch[] Tests)> cases = [];
        if (owner == "RequestId" && branches.Count == 2 && branches.Select(b => b.Type).ToHashSet()
                .SetEquals([JsonObjectType.String, JsonObjectType.Integer]))
        {
            cases.AddRange(branches.Select(b => (b, b.Type == JsonObjectType.String ? "String" : "Integer",
                b.Type == JsonObjectType.String ? JsonValueKind.String : JsonValueKind.Number,
                Array.Empty<PropertyMatch>())));
        }
        else if (owner is "ForcedChatgptWorkspaceIds" or "FunctionCallOutputBody" or "ThreadListCwdFilter" &&
                 branches.Count == 2 &&
                 branches.Select(b => b.Type).ToHashSet().SetEquals([JsonObjectType.String, JsonObjectType.Array]))
        {
            cases.AddRange(branches.Select(b => (b, b.Type == JsonObjectType.String ? "String" : "Array",
                b.Type == JsonObjectType.String ? JsonValueKind.String : JsonValueKind.Array,
                Array.Empty<PropertyMatch>())));
        }
        else if (owner == "ResourceContent" && branches.Count == 2 &&
                 branches.All(b => b.Type == JsonObjectType.Object))
        {
            foreach (var b in branches)
            {
                var key = b.RequiredProperties.Contains("text") ? "text" : "blob";
                if (!b.RequiredProperties.Contains(key) || !b.RequiredProperties.Contains("uri"))
                {
                    throw Analyzer.Document.Unsupported(b, "Unexpected ResourceContent branch.");
                }

                cases.Add((b, key, JsonValueKind.Object, [new PropertyMatch([key])]));
            }
        }
        else if (owner == "JSONRPCMessage" && branches.Count == 4 && branches.Select(Name).ToHashSet()
                     .SetEquals(["JSONRPCRequest", "JSONRPCNotification", "JSONRPCResponse", "JSONRPCError"]))
        {
            foreach (var b in branches)
            {
                PropertyMatch[] tests = Name(b) switch
                {
                    "JSONRPCRequest" => [new PropertyMatch(["method"]), new PropertyMatch(["id"])],
                    "JSONRPCNotification" => [new PropertyMatch(["method"]), new PropertyMatch(["id"], Exists: false)],
                    "JSONRPCResponse" =>
                    [
                        new PropertyMatch(["result"]), new PropertyMatch(["id"]),
                        new PropertyMatch(["method"], Exists: false), new PropertyMatch(["error"], Exists: false)
                    ],
                    _ =>
                    [
                        new PropertyMatch(["error"]), new PropertyMatch(["id"]),
                        new PropertyMatch(["method"], Exists: false)
                    ]
                };
                cases.Add((b, Name(b), JsonValueKind.Object, tests));
            }
        }
        else if (McpGroups.ContainsKey(owner))
        {
            void Flatten(string group, IReadOnlyList<JsonSchema> parts)
            {
                if (!parts.Select(Name).Order(StringComparer.Ordinal)
                        .SequenceEqual(McpGroups[group].Order(StringComparer.Ordinal)))
                {
                    throw Analyzer.Document.Unsupported(schema, $"Unexpected {group} alternatives.");
                }

                foreach (var b in parts)
                {
                    var name = Name(b);
                    if (McpGroups.ContainsKey(name))
                    {
                        Flatten(name, b.AnyOf.Select(x => x.ActualSchema).ToList());
                    }
                    else
                    {
                        PropertyMatch[] tests = name switch
                        {
                            "McpElicitationStringSchema" =>
                            [
                                new PropertyMatch(["type"], ["string"]), new PropertyMatch(["enum"], Exists: false),
                                new PropertyMatch(["oneOf"], Exists: false),
                                new PropertyMatch(["enumNames"], Exists: false)
                            ],
                            "McpElicitationNumberSchema" => [new PropertyMatch(["type"], ["number", "integer"])],
                            "McpElicitationBooleanSchema" => [new PropertyMatch(["type"], ["boolean"])],
                            "McpElicitationLegacyTitledEnumSchema" =>
                            [
                                new PropertyMatch(["type"], ["string"]), new PropertyMatch(["enum"]),
                                new PropertyMatch(["enumNames"])
                            ],
                            "McpElicitationUntitledSingleSelectEnumSchema" =>
                            [
                                new PropertyMatch(["type"], ["string"]), new PropertyMatch(["enum"]),
                                new PropertyMatch(["enumNames"], Exists: false)
                            ],
                            "McpElicitationTitledSingleSelectEnumSchema" =>
                                [new PropertyMatch(["type"], ["string"]), new PropertyMatch(["oneOf"])],
                            "McpElicitationUntitledMultiSelectEnumSchema" =>
                                [new PropertyMatch(["type"], ["array"]), new PropertyMatch(["items", "enum"])],
                            "McpElicitationTitledMultiSelectEnumSchema" =>
                                [new PropertyMatch(["type"], ["array"]), new PropertyMatch(["items", "anyOf"])],
                            _ => throw Analyzer.Document.Unsupported(b, "Unexpected MCP elicitation schema.")
                        };
                        cases.Add((b, name, JsonValueKind.Object, tests));
                    }
                }
            }

            Flatten(owner, branches);
        }
        else
        {
            throw Analyzer.Document.Unsupported(schema,
                "Union is neither nullable, tagged nor an explicitly supported protocol shape.");
        }

        if (cases.Select(c => c.Tag).Distinct(StringComparer.Ordinal).Count() != cases.Count)
        {
            throw Analyzer.Document.Unsupported(schema, "Ambiguous protocol union branches.");
        }

        return Analyzer.Declare(schema, name => new UnionTypeModel(name, null,
            cases.OrderBy(c => c.Tag, StringComparer.Ordinal).Select(c => new UnionMemberModel(
                Analyzer.Allocate(name + "_" + c.Tag), c.Tag, c.Kind, c.Tests,
                Analyzer.Resolver.ResolveType(c.Schema), Description: c.Schema.Description)).ToList(),
            schema.Description, HasValueSemantics: owner == "RequestId"));
    }

    private string Name(JsonSchema schema)
    {
        return Analyzer.Document.Source(schema).Owner;
    }
}
