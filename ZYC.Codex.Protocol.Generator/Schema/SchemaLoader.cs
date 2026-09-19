using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace ZYC.Codex.Protocol.Generator.Schema;

public record SchemaDefinition(string Name, string Group, JsonSchema Schema);

public record SchemaSource(string Path, string Owner, JToken Json);

public record LoadedSchema(
    JsonSchema Root,
    IReadOnlyList<SchemaDefinition> Definitions,
    IReadOnlyDictionary<string, SchemaSource> Sources)
{
    internal const string PathKey = "x-codex-generator-path";

    public SchemaSource Source(JsonSchema schema)
    {
        return schema.ExtensionData?.TryGetValue(PathKey, out var path) == true && path != null &&
               Sources.TryGetValue(path.ToString()!, out var source)
            ? source
            : new SchemaSource("<resolved external schema>", schema.Title ?? "<anonymous>",
                JToken.Parse(schema.ToJson()));
    }

    public NotSupportedException Unsupported(JsonSchema schema, string reason)
    {
        return new NotSupportedException(
            $"Unsupported schema while generating {Source(schema).Owner}: {reason}\n\nPath:\n{Source(schema).Path}\n\nSchema:\n{Source(schema).Json}");
    }
}

public static class SchemaLoader
{
    public static async Task<LoadedSchema> LoadAsync(string path)
    {
        return await ParseAsync(await File.ReadAllTextAsync(path), Path.GetFullPath(path));
    }

    public static async Task<LoadedSchema> ParseAsync(string json, string? documentPath = null)
    {
        var sources = new Dictionary<string, SchemaSource>(StringComparer.Ordinal);
        var entries = new List<(string Key, string Name, string Group)>();
        var input = JToken.Parse(json);

        string Escape(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        JToken Prepare(JToken token, string path, string owner)
        {
            sources.Add(path, new SchemaSource(path, owner, token.DeepClone()));
            var obj = token switch
            {
                JObject value => value,
                JValue { Type: JTokenType.Boolean } value => value.Value<bool>()
                    ? new JObject()
                    : new JObject { ["not"] = new JObject() },
                _ => throw new NotSupportedException(
                    $"Unsupported schema while generating {owner}\nPath: {path}\nSchema: {token}")
            };
            if (obj.ContainsKey(LoadedSchema.PathKey))
            {
                throw new NotSupportedException($"Reserved schema annotation at {path}: {token}");
            }

            foreach (var key in new[] { "properties", "patternProperties", "definitions", "$defs" })
            {
                if (obj[key] is JObject map)
                {
                    foreach (var property in map.Properties().ToList())
                    {
                        var childPath = path + "/" + key + "/" + Escape(property.Name);
                        // Codex's general bundle has a raw namespace dictionary at definitions.v2.
                        if (path == "#" && key == "definitions" && property.Name == "v2" &&
                            property.Value is JObject group)
                        {
                            foreach (var definition in group.Properties().ToList())
                            {
                                var member = (JObject)Prepare(definition.Value,
                                    childPath + "/" + Escape(definition.Name), definition.Name);
                                // NJsonSchema recognizes schemas in extension data by type/properties.
                                // An empty properties keyword is neutral, including for non-object unions.
                                if (member["type"] == null && member["properties"] == null && member["$ref"] == null)
                                {
                                    member["properties"] = new JObject();
                                }

                                definition.Value = member;
                            }
                        }
                        else
                        {
                            property.Value = Prepare(property.Value, childPath,
                                key is "definitions" or "$defs" ? property.Name : owner + "." + property.Name);
                        }
                    }
                }
            }

            foreach (var key in new[] { "oneOf", "anyOf", "allOf" })
            {
                if (obj[key] is JArray branches)
                {
                    for (var i = 0; i < branches.Count; i++)
                    {
                        branches[i] = Prepare(branches[i], path + "/" + key + "/" + i, owner);
                    }
                }
            }

            foreach (var key in new[] { "items", "additionalProperties", "not" })
            {
                if (obj[key] is { } child && (child is JObject || (key == "items" && child is JValue)))
                {
                    obj[key] = Prepare(child, path + "/" + key, owner);
                }
            }

            if (obj["$ref"] == null)
            {
                obj[LoadedSchema.PathKey] = path;
            }

            return obj;
        }

        var prepared = (JObject)Prepare(input, "#",
            input is JObject rootObject ? (string?)rootObject["title"] ?? "Root" : "Root");
        if (prepared["definitions"] is JObject definitions)
        {
            foreach (var d in definitions.Properties().Where(d => d.Name != "v2"))
            {
                entries.Add((d.Name, d.Name, ""));
            }

            if (definitions["v2"] is JObject group)
            {
                foreach (var d in group.Properties().OrderBy(d => d.Name, StringComparer.Ordinal))
                {
                    var proxy = "__codex_v2_" + d.Name;
                    if (definitions.ContainsKey(proxy))
                    {
                        throw new NotSupportedException($"Reserved definition {proxy}.");
                    }

                    // Give NJsonSchema an entry point for every v2 definition, including unused ones.
                    // Original $refs and target locations are untouched; resolution is entirely NJsonSchema's.
                    definitions.Add(proxy, new JObject { ["$ref"] = "#/definitions/v2/" + Escape(d.Name) });
                    entries.Add((proxy, d.Name, "V2"));
                }
            }
        }

        JsonSchema root;
        try
        {
            root = await JsonSchema.FromJsonAsync(prepared.ToString(Formatting.None), documentPath);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
                                              or NotSupportedException)
        {
            var source = sources.Values.FirstOrDefault(s => s.Json is JObject obj && obj["$ref"] is { } reference &&
                                                            exception.ToString().Contains(reference.ToString(),
                                                                StringComparison.Ordinal)) ?? sources["#"];
            throw new NotSupportedException(
                $"Unsupported schema while generating {source.Owner}: {exception.Message}\n\nPath:\n{source.Path}\n\nSchema:\n{source.Json}",
                exception);
        }

        void Index(JsonSchema node, string path)
        {
            if (sources.ContainsKey(path))
            {
                (node.ExtensionData ??= new Dictionary<string, object?>())[LoadedSchema.PathKey] = path;
            }

            foreach (var p in node.Properties)
            {
                Index(p.Value, path + "/properties/" + Escape(p.Key));
            }

            foreach (var d in node.Definitions)
            {
                Index(d.Value, path + "/definitions/" + Escape(d.Key));
            }

            foreach (var (key, children) in new[]
                         { ("oneOf", node.OneOf), ("anyOf", node.AnyOf), ("allOf", node.AllOf) })
            foreach (var (child, i) in children.Select((child, i) => (child, i)))
            {
                Index(child, path + "/" + key + "/" + i);
            }

            if (node.Item != null)
            {
                Index(node.Item, path + "/items");
            }

            if (node.AdditionalPropertiesSchema != null)
            {
                Index(node.AdditionalPropertiesSchema, path + "/additionalProperties");
            }
        }

        Index(root, "#");
        foreach (var entry in entries.Where(e => e.Group == "V2"))
        {
            Index(root.Definitions[entry.Key].ActualSchema, "#/definitions/v2/" + Escape(entry.Name));
        }

        return new LoadedSchema(root,
            entries.Select(d => new SchemaDefinition(d.Name, d.Group, root.Definitions[d.Key].ActualSchema)).ToList(),
            sources);
    }
}