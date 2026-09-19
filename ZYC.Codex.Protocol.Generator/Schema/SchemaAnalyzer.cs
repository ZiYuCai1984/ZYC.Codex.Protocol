using Newtonsoft.Json.Linq;
using NJsonSchema;
using ZYC.Codex.Protocol.Generator.Model;
using ZYC.Codex.Protocol.Generator.Naming;

namespace ZYC.Codex.Protocol.Generator.Schema;

public class SchemaAnalyzer
{
    public SchemaAnalyzer(LoadedSchema document)
    {
        Document = document;
        Resolver = new SchemaTypeResolver(this);
    }

    private Dictionary<string, TypeModel> Models { get; } = new(StringComparer.Ordinal);

    private Dictionary<string, string> PathNames { get; } = new(StringComparer.Ordinal);

    private HashSet<string> UsedNames { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "List", "Dictionary", "Guid", "DateTimeOffset", "JsonElement", "JsonSerializer",
        "JsonConverter", "JsonPropertyName", "JsonIgnore", "JsonInclude", "JsonRequired", "JsonException", "JsonDocument",
        "JsonSerializerOptions", "Utf8JsonReader", "Utf8JsonWriter", "JsonTokenType", "JsonValueKind",
        "JsonExtensionData", "JsonUnmappedMemberHandling", "JsonIgnoreCondition", "JsonStringEnumMemberName",
        "IEquatable", "EqualityComparer", "ArgumentNullException", "Optional", "OptionalJsonConverterFactory"
    };

    private string? NullTypeName { get; set; }

    internal LoadedSchema Document { get; }
    internal SchemaTypeResolver Resolver { get; }
    internal Dictionary<JsonSchema, string> Names { get; } = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<TypeModel> Analyze()
    {
        Resolver.Check(Document.Root);
        var first = new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
        foreach (var d in Document.Definitions.OrderBy(x => x.Group, StringComparer.Ordinal)
                     .ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            if (first.TryGetValue(d.Name, out var previous) &&
                JToken.DeepEquals(Shape(Document.Source(previous.Schema).Json), Shape(Document.Source(d.Schema).Json)))
            {
                Names[d.Schema] = Names[previous.Schema];
            }
            else if (!Names.ContainsKey(d.Schema))
            {
                Names[d.Schema] = Allocate((first.ContainsKey(d.Name) ? d.Group : "") + d.Name);
            }

            first.TryAdd(d.Name, d);
            PathNames[Document.Source(d.Schema).Path] = Names[d.Schema];
        }

        foreach (var d in Document.Definitions.OrderBy(x => Names[x.Schema], StringComparer.Ordinal))
        {
            Resolver.ResolveType(d.Schema);
        }

        if (Document.Definitions.Count == 0 ||
            Document.Root.Type is not (JsonObjectType.None or JsonObjectType.Object) ||
            Document.Root.Properties.Count > 0 ||
            Document.Root.OneOf.Count + Document.Root.AnyOf.Count + Document.Root.AllOf.Count > 0)
        {
            Resolver.ResolveType(Document.Root);
        }

        return Models.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    }

    private static JToken Shape(JToken token)
    {
        return token switch
        {
            JObject obj => new JObject(obj.Properties()
                .Where(p => p.Name is not ("title" or "description" or "$schema"))
                .OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Shape(p.Value)))),
            JArray array => new JArray(array.Select(Shape)),
            _ => token.DeepClone()
        };
    }

    internal string Allocate(string hint)
    {
        var name = CSharpNaming.TypeName(hint);
        for (var i = 2; UsedNames.Contains(name) || UsedNames.Contains(name + "JsonConverter"); i++)
        {
            name = CSharpNaming.TypeName(hint) + i;
        }

        UsedNames.Add(name);
        UsedNames.Add(name + "JsonConverter");
        return name;
    }

    internal string Name(JsonSchema schema)
    {
        if (Names.TryGetValue(schema, out var name))
        {
            return name;
        }

        var source = Document.Source(schema);
        if (!PathNames.TryGetValue(source.Path, out name))
        {
            PathNames[source.Path] = name = Allocate(schema.Title ?? source.Owner);
        }

        return Names[schema] = name;
    }

    internal TypeReference Declare(JsonSchema schema, Func<string, TypeModel> create)
    {
        var name = Name(schema);
        if (!Models.ContainsKey(name))
        {
            // Reserve before descending so recursive DTO references terminate.
            Models[name] = new ObjectTypeModel(name, []);
            Models[name] = create(name);
        }

        return new TypeReference(TypeKind.Named, name);
    }

    internal void Add(TypeModel model)
    {
        Models.Add(model.Name, model);
    }

    internal TypeReference Null()
    {
        if (NullTypeName == null)
        {
            NullTypeName = Allocate("ProtocolNull");
            Add(new NullTypeModel(NullTypeName));
        }

        return new TypeReference(TypeKind.Named, NullTypeName);
    }

    internal EnumTypeModel Enum(string name, IEnumerable<string> values, string? description)
    {
        var scope = CSharpNaming.MemberScope(name);
        scope.Add("value__");
        return new EnumTypeModel(name, values.Order(StringComparer.Ordinal).Select(value => new EnumValueModel(
            CSharpNaming.Unique(CSharpNaming.EnumMemberName(value), scope), value)).ToList(), description);
    }

    internal ObjectTypeModel Object(JsonSchema schema, string name, string? baseType = null, JsonSchema? common = null)
    {
        var properties = Properties(schema).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (common != null)
        {
            foreach (var p in common.Properties)
            {
                if (!properties.TryAdd(p.Key, p.Value))
                {
                    throw Document.Unsupported(schema, $"Duplicate common property {p.Key} in {name}.");
                }
            }
        }

        var required = Required(schema);
        if (common != null)
        {
            required.UnionWith(common.RequiredProperties);
        }

        var scope = CSharpNaming.MemberScope(name);
        var result = new List<PropertyModel>();
        foreach (var (jsonName, p) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            TypeReference type;
            try
            {
                type = Resolver.ResolveType(p);
            }
            catch (NotSupportedException exception)
            {
                throw new NotSupportedException($"While generating {name}.{jsonName}:\n{exception.Message}", exception);
            }

            var isRequired = required.Contains(jsonName);
            var constant = isRequired && type.Kind != TypeKind.Nullable &&
                           UnionAnalyzer.StringValues(p.ActualSchema) is [var only] ? only : null;
            result.Add(new PropertyModel(jsonName, CSharpNaming.Unique(CSharpNaming.PropertyName(jsonName), scope),
                type, isRequired, p.Description, constant));
        }

        if (required.Except(properties.Keys).FirstOrDefault() is { } missing)
        {
            throw Document.Unsupported(schema, $"Required property {missing} has no declared schema in {name}.");
        }

        if (schema.AdditionalPropertiesSchema != null && properties.Count > 0)
        {
            throw Document.Unsupported(schema, "Typed additionalProperties alongside named properties is unsupported.");
        }

        var source = Document.Source(schema);
        var preserveShapeExtras = source.Owner is "JSONRPCRequest" or "JSONRPCNotification" or "JSONRPCResponse"
            or "JSONRPCError" or "ResourceContent";
        var extension = schema.AllowAdditionalProperties &&
                        (source.Json["additionalProperties"]?.Type == JTokenType.Boolean || preserveShapeExtras)
            ? CSharpNaming.Unique("AdditionalProperties", scope)
            : null;
        return new ObjectTypeModel(name, result, schema.Description, baseType, extension,
            !schema.AllowAdditionalProperties);
    }

    internal IReadOnlyDictionary<string, JsonSchemaProperty> Properties(JsonSchema schema)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Check(JsonSchema node, HashSet<JsonSchema> visiting)
        {
            node = node.ActualSchema;
            if (!visiting.Add(node))
            {
                throw Document.Unsupported(schema, "Cyclic allOf.");
            }

            Resolver.Check(node);
            if (node.AdditionalPropertiesSchema != null)
            {
                throw Document.Unsupported(node, "Typed dictionary constraints inside allOf.");
            }

            if (node.OneOf.Count + node.AnyOf.Count > 0 ||
                node.Type is not (JsonObjectType.Object or JsonObjectType.None))
            {
                throw Document.Unsupported(node, "Only object allOf flattening is supported.");
            }

            foreach (var property in node.Properties.Keys)
            {
                if (!seen.Add(property))
                {
                    throw Document.Unsupported(schema, $"Duplicate allOf property {property}.");
                }
            }

            foreach (var branch in node.AllOf)
            {
                Check(branch, visiting);
            }

            visiting.Remove(node);
        }

        // oneOf branches have already been selected; common properties are merged separately.
        if (schema.AllOf.Count == 0)
        {
            return schema.ActualProperties;
        }

        Check(schema, new HashSet<JsonSchema>(ReferenceEqualityComparer.Instance));
        var result = schema.ActualProperties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        // ActualProperties excludes NJsonSchema's selected InheritedSchema; flatten that as well.
        if (schema.InheritedSchema is { } inherited)
        {
            foreach (var p in Properties(inherited))
            {
                result.Add(p.Key, p.Value);
            }
        }

        return result;
    }

    private static HashSet<string> Required(JsonSchema schema)
    {
        var result = new HashSet<string>(schema.RequiredProperties, StringComparer.Ordinal);
        foreach (var part in schema.AllOf)
        {
            result.UnionWith(Required(part.ActualSchema));
        }

        return result;
    }
}
