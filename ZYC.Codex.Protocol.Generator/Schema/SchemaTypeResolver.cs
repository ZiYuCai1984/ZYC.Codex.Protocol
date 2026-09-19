using Newtonsoft.Json.Linq;
using NJsonSchema;
using ZYC.Codex.Protocol.Generator.Model;

namespace ZYC.Codex.Protocol.Generator.Schema;

public class SchemaTypeResolver
{
    private SchemaAnalyzer Analyzer { get; }

    public SchemaTypeResolver(SchemaAnalyzer analyzer)
    {
        Analyzer = analyzer;
    }

    private static HashSet<string> SupportedKeywords { get; } = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "id", "$ref", "title", "description", "default", "examples", "definitions",
        "type", "properties", "required", "additionalProperties", "items", "enum", "oneOf", "anyOf", "allOf",
        "format", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength",
        "minItems", "maxItems", "uniqueItems", "pattern", LoadedSchema.PathKey
    };

    private HashSet<JsonSchema> Resolving { get; }= new(ReferenceEqualityComparer.Instance);

    internal void Check(JsonSchema schema)
    {
        if (Analyzer.Document.Source(schema).Json is JObject raw)
        {
            foreach (var key in raw.Properties().Select(p => p.Name))
            {
                if (!SupportedKeywords.Contains(key))
                {
                    throw Analyzer.Document.Unsupported(schema, $"Keyword '{key}' is not supported.");
                }
            }
        }

        if (schema.Items.Count > 0 || schema.Not != null || schema.PatternProperties.Count > 0 ||
            schema.DiscriminatorObject != null)
        {
            throw Analyzer.Document.Unsupported(schema,
                "Tuple, not, patternProperties or inheritance discriminator is unsupported.");
        }
    }

    public TypeReference ResolveType(JsonSchema schema)
    {
        Check(schema);
        JsonSchema actual;
        try
        {
            actual = schema.ActualSchema;
        }
        catch (InvalidOperationException exception)
        {
            throw Analyzer.Document.Unsupported(schema, exception.Message);
        }

        if (!ReferenceEquals(schema, actual))
        {
            return ResolveType(actual);
        }

        Check(actual);
        if (!Resolving.Add(schema))
        {
            // Recursion is legal only through a named object/union, never through a type alias.
            if (schema.Properties.Count > 0 || schema.OneOf.Count > 0)
            {
                return new TypeReference(TypeKind.Named, Analyzer.Name(schema));
            }

            throw Analyzer.Document.Unsupported(schema, "Recursive alias has no concrete C# type.");
        }

        try
        {
            var branches = schema.OneOf.Count > 0 ? schema.OneOf : schema.AnyOf;
            if (schema.AllOf.Count > 0 && schema.Type is not (JsonObjectType.None or JsonObjectType.Object))
            {
                throw Analyzer.Document.Unsupported(schema, "Only object allOf flattening is supported.");
            }

            if (schema.OneOf.Count > 0 && schema.AnyOf.Count > 0)
            {
                throw Analyzer.Document.Unsupported(schema, "Simultaneous oneOf and anyOf.");
            }

            if (branches.Count == 2 && branches.Count(x => x.ActualSchema.Type == JsonObjectType.Null) == 1)
            {
                if (schema.Properties.Count + schema.AllOf.Count > 0)
                {
                    throw Analyzer.Document.Unsupported(schema, "Nullable union has sibling constraints.");
                }

                return ResolveType(branches.Single(x => x.ActualSchema.Type != JsonObjectType.Null)).AsNullable();
            }

            TypeReference result;
            if (branches.Count > 0)
            {
                result = new UnionAnalyzer(Analyzer).Resolve(schema, branches.ToList());
            }
            else if (UnionAnalyzer.StringValues(schema) is { } values)
            {
                if (values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
                    schema.Properties.Count + schema.AllOf.Count > 0)
                {
                    throw Analyzer.Document.Unsupported(schema, "Duplicate enum values or composed enum constraints.");
                }

                result = values.Length == 1
                    ? new TypeReference(TypeKind.Primitive, "string")
                    : Analyzer.Declare(schema, name => Analyzer.Enum(name, values, schema.Description));
            }
            else
            {
                if (schema.Enumeration.Count > 0)
                {
                    throw Analyzer.Document.Unsupported(schema, "Only string enums are supported.");
                }

                var type = schema.Type & ~JsonObjectType.Null;
                result = type switch
                {
                    JsonObjectType.String => Primitive(schema, "string"),
                    JsonObjectType.Boolean => Primitive(schema, "bool"),
                    JsonObjectType.Integer => Primitive(schema, "long"),
                    JsonObjectType.Number => Primitive(schema, "double"),
                    JsonObjectType.Array when schema.Item != null => new TypeReference(TypeKind.Array,
                        Element: ResolveType(schema.Item)),
                    JsonObjectType.Object => ResolveObject(schema),
                    JsonObjectType.None when schema.AllOf.Count > 0 || schema.Properties.Count > 0 => ResolveObject(
                        schema),
                    JsonObjectType.None when schema.Type == JsonObjectType.Null => Analyzer.Null(),
                    JsonObjectType.None when schema.Type == JsonObjectType.None && IsUnrestricted(schema) =>
                        new TypeReference(TypeKind.Any),
                    _ => throw Analyzer.Document.Unsupported(schema, $"No mapping for type '{schema.Type}'.")
                };
            }

            return schema.Type.HasFlag(JsonObjectType.Null) ? result.AsNullable() : result;
        }
        finally
        {
            Resolving.Remove(schema);
        }
    }

    private bool IsUnrestricted(JsonSchema schema)
    {
        return Analyzer.Document.Source(schema).Json switch
        {
            JValue { Type: JTokenType.Boolean } value => value.Value<bool>(),
            JObject obj => obj.Properties().All(p =>
                p.Name is "$schema" or "$id" or "id" or "title" or "description" or "default" or "examples"
                    or LoadedSchema.PathKey),
            _ => false
        };
    }

    private TypeReference ResolveObject(JsonSchema schema)
    {
        if (schema.OneOf.Count + schema.AnyOf.Count > 0)
        {
            throw Analyzer.Document.Unsupported(schema, "Object composition is unsupported here.");
        }

        var properties = Analyzer.Properties(schema);
        if (properties.Count == 0 && schema.AllOf.Count == 0 &&
            (schema.AdditionalPropertiesSchema != null ||
             Analyzer.Document.Source(schema).Json["additionalProperties"]?.Value<bool>() == true))
        {
            return new TypeReference(TypeKind.Dictionary,
                Element: schema.AdditionalPropertiesSchema is { } value
                    ? ResolveType(value)
                    : new TypeReference(TypeKind.Any));
        }

        return Analyzer.Declare(schema, name => Analyzer.Object(schema, name));
    }

    private TypeReference Primitive(JsonSchema schema, string defaultName)
    {
        var name = (defaultName, schema.Format) switch
        {
            (_, null or "") => defaultName,
            ("long", "int32") => "int", ("long", "int64") => "long",
            ("long", "uint16") => "ushort", ("long", "uint32") => "uint", ("long", "uint64" or "uint") => "ulong",
            ("double", "float") => "float", ("double", "double") => "double",
            ("string", "uuid") => "Guid", ("string", "date-time") => "DateTimeOffset",
            _ => throw Analyzer.Document.Unsupported(schema, $"Unsupported format '{schema.Format}' for {defaultName}.")
        };
        return new TypeReference(TypeKind.Primitive, name);
    }
}
