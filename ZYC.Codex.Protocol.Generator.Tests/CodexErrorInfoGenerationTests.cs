using System.Text.Json;
using Newtonsoft.Json.Linq;
using ZYC.Codex.Protocol.Generator.Generation;
using ZYC.Codex.Protocol.Generator.Model;
using ZYC.Codex.Protocol.Generator.Schema;
using ZYC.CoreToolkit;

namespace ZYC.Codex.Protocol.Generator.Tests;

public class CodexErrorInfoGenerationTests
{
    private static async Task<JObject> LoadSchemaAsync()
    {
        var root = IOTools.GetSlnxFolderPath()
                   ?? throw new InvalidOperationException("Cannot locate the solution directory for generator tests.");
        return JObject.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "ZYC.Codex.Protocol.Generator.Tests", "Fixtures", "codex-error-info.json")));
    }

    private static async Task<IReadOnlyList<TypeModel>> AnalyzeAsync(JObject schema)
    {
        return new SchemaAnalyzer(await SchemaLoader.ParseAsync(schema.ToString())).Analyze();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenErrorUnionPreservesKnownTypesAndAddsRawFallbacksAsync(bool groupedDefinitions)
    {
        var schema = await LoadSchemaAsync();
        if (groupedDefinitions)
        {
            schema = new JObject
            {
                ["definitions"] = new JObject { ["v2"] = new JObject { ["CodexErrorInfo"] = schema } }
            };
        }

        var models = await AnalyzeAsync(schema);
        var union = Assert.IsType<UnionTypeModel>(models.Single(m => m.Name == "CodexErrorInfo"));
        Assert.Equal(4, union.Members.Count(m => !m.IsFallback));
        Assert.Equal("badRequest", Assert.Single(union.Members, m => m.TypeName == "CodexErrorInfoBadRequest").Literal);
        Assert.Contains(models, m => m is ObjectTypeModel { Name: "HttpConnectionFailedCodexErrorInfo", BaseType: "CodexErrorInfo" });
        Assert.Collection(union.Members.Where(m => m.IsFallback),
            text =>
            {
                Assert.Equal(JsonValueKind.String, text.Kind);
                Assert.Equal(new TypeReference(TypeKind.Primitive, "string"), text.Payload);
            },
            obj =>
            {
                Assert.Equal(JsonValueKind.Object, obj.Kind);
                Assert.Equal(new TypeReference(TypeKind.Any), obj.Payload);
            });

        var files = new CSharpGenerator().Generate(models);
        var converter = files["CodexErrorInfoJsonConverter.g.cs"];
        Assert.True(converter.IndexOf("new CodexErrorInfoBadRequest()", StringComparison.Ordinal) <
                    converter.IndexOf("new CodexErrorInfoUnknownString", StringComparison.Ordinal));
        Assert.True(converter.IndexOf("root.Deserialize<HttpConnectionFailedCodexErrorInfo>", StringComparison.Ordinal) <
                    converter.IndexOf("new CodexErrorInfoUnknownObject", StringComparison.Ordinal));
        Assert.Contains("catch (JsonException)", converter);
        Assert.Contains("new CodexErrorInfoUnknownObject { Value = root.Clone() }", converter);
        Assert.Contains("concrete.Value.WriteTo(writer);", converter);
        Assert.Contains("if (concrete.Value.ValueKind != JsonValueKind.Object)",
            files["CodexErrorInfoUnknownObjectJsonConverter.g.cs"]);
        Assert.Contains("public JsonElement Value", files["CodexErrorInfoUnknownObject.g.cs"]);
    }

    [Fact]
    public async Task ClosedErrorUnionRetainsStrictDeserializationAsync()
    {
        var schema = await LoadSchemaAsync();
        ((JArray)schema["anyOf"]!).Last!.Remove();
        var models = await AnalyzeAsync(schema);
        var union = Assert.IsType<UnionTypeModel>(models.Single(m => m.Name == "CodexErrorInfo"));
        Assert.DoesNotContain(union.Members, m => m.IsFallback);
        var files = new CSharpGenerator().Generate(models);
        Assert.DoesNotContain("CodexErrorInfoUnknownObject.g.cs", files.Keys);
        Assert.DoesNotContain("catch (JsonException)", files["CodexErrorInfoJsonConverter.g.cs"]);
    }

    [Fact]
    public async Task OpenErrorUnionGenerationIgnoresBranchOrderAsync()
    {
        var schema = await LoadSchemaAsync();
        var first = new CSharpGenerator().Generate(await AnalyzeAsync(schema));
        schema["anyOf"] = new JArray(((JArray)schema["anyOf"]!).Reverse());
        var second = new CSharpGenerator().Generate(await AnalyzeAsync(schema));
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Theory]
    [InlineData("{\"type\":[\"string\",\"object\"],\"additionalProperties\":false}")]
    [InlineData("{\"type\":[\"string\",\"object\"],\"enum\":[\"futureError\"]}")]
    [InlineData("{\"type\":[\"string\",\"object\",\"integer\"]}")]
    public async Task ConstrainedOrDifferentFallbacksRemainUnsupportedAsync(string fallback)
    {
        var schema = await LoadSchemaAsync();
        ((JArray)schema["anyOf"]!).Last!.Replace(JToken.Parse(fallback));
        await Assert.ThrowsAsync<NotSupportedException>(() => AnalyzeAsync(schema));
    }

    [Fact]
    public async Task OverlappingOneOfIsNotTreatedAsAnyOfAsync()
    {
        var schema = await LoadSchemaAsync();
        schema["oneOf"] = schema["anyOf"]!.DeepClone();
        schema.Remove("anyOf");
        await Assert.ThrowsAsync<NotSupportedException>(() => AnalyzeAsync(schema));
    }
}
