using Newtonsoft.Json.Linq;
using ZYC.Codex.Protocol.Generator.Generation;
using ZYC.Codex.Protocol.Generator.Model;
using ZYC.Codex.Protocol.Generator.Naming;
using ZYC.Codex.Protocol.Generator.Schema;
using ZYC.CoreToolkit;

namespace ZYC.Codex.Protocol.Generator.Tests;

public class GeneratorTests
{
    private static string SolutionPath => IOTools.GetSlnFolderPath()
        ?? throw new InvalidOperationException("Cannot locate the solution directory for generator tests.");

    private static string GeneratorProjectPath => Path.Combine(SolutionPath, "ZYC.Codex.Protocol.Generator");
    private static string TestProjectPath => Path.Combine(SolutionPath, "ZYC.Codex.Protocol.Generator.Tests");
    private static string ProtocolProjectPath => Path.Combine(SolutionPath, "ZYC.Codex.Protocol");

    private static string ProtocolSchemasJsonPath =>
        Path.Combine(GeneratorProjectPath, "json-schema", "codex_app_server_protocol.schemas.json");

    private static string ProtocolV2SchemasJsonPath =>
        Path.Combine(GeneratorProjectPath, "json-schema", "codex_app_server_protocol.v2.schemas.json");

    private static string BasicFixtureJsonPath => Path.Combine(TestProjectPath, "Fixtures", "basic.json");
    private static string GeneratedFixturesPath => Path.Combine(TestProjectPath, "GeneratedFixtures");

    private static async Task<IReadOnlyList<TypeModel>> Analyze(string json)
    {
        return new SchemaAnalyzer(await SchemaLoader.ParseAsync(json)).Analyze();
    }

    [Fact]
    public async Task FullBundleIsDeterministicAndCheckedInFilesAreCurrent()
    {
        var first = new CSharpGenerator().Generate(new SchemaAnalyzer(await SchemaLoader.LoadAsync(ProtocolSchemasJsonPath)).Analyze());
        var second = new CSharpGenerator().Generate(new SchemaAnalyzer(await SchemaLoader.LoadAsync(ProtocolSchemasJsonPath)).Analyze());
        Assert.Equal(first.Keys, second.Keys);
        var output = ProtocolProjectPath;
        Assert.Equal(first.Count, Directory.GetFiles(output, "*.g.cs").Length);
        foreach (var (file, content) in first)
        {
            Assert.Equal(content, second[file]);
            Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(output, file)));
        }

        Assert.Contains("public abstract class ClientRequest", first["ClientRequest.g.cs"]);
        Assert.Contains("public RequestId Id { get; set; }", first["InitializeRequest.g.cs"]);
        Assert.Contains("public string Token { get; set; }", first["AttestationGenerateResponse.g.cs"]);
        Assert.DoesNotContain("V2RequestId.g.cs", first.Keys);
    }

    [Fact]
    public async Task StandaloneV2BundleAlsoGenerates()
    {
        var schema = await SchemaLoader.LoadAsync(ProtocolV2SchemasJsonPath);
        var files = new CSharpGenerator().Generate(new SchemaAnalyzer(schema).Analyze());
        Assert.Contains("ThreadStartParams.g.cs", files.Keys);
        Assert.Contains("InitializeParams.g.cs", files.Keys);
    }

    [Fact]
    public async Task BasicFixtureGeneratedFilesAreCurrent()
    {
        var schema = await SchemaLoader.LoadAsync(BasicFixtureJsonPath);
        var files = new CSharpGenerator().Generate(new SchemaAnalyzer(schema).Analyze());
        foreach (var (file, content) in files)
        {
            Assert.Equal(content,
                await File.ReadAllTextAsync(Path.Combine(GeneratedFixturesPath, file)));
        }
    }

    [Theory]
    [InlineData("{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"integer\"}]}")]
    [InlineData("{\"oneOf\":[{\"type\":\"boolean\"},{\"type\":\"integer\"}]}")]
    [InlineData("{\"type\":\"string\",\"format\":\"new-format\"}")]
    [InlineData("{\"type\":\"object\",\"patternProperties\":{\"x\":{\"type\":\"integer\"}}}")]
    [InlineData("{\"type\":\"array\",\"items\":[{\"type\":\"string\"},{\"type\":\"boolean\"}]}")]
    [InlineData("{\"type\":\"array\"}")]
    [InlineData("{\"type\":[\"integer\",\"boolean\"]}")]
    [InlineData("{\"type\":\"integer\",\"enum\":[1,2]}")]
    [InlineData("{\"type\":\"string\",\"enum\":[\"a\",\"a\"]}")]
    [InlineData("{\"if\":{\"type\":\"string\"},\"then\":{\"minLength\":3}}")]
    [InlineData("{\"futureKeyword\":true}")]
    [InlineData("{\"type\":\"string\",\"allOf\":[{\"type\":\"string\"}]}")]
    [InlineData("false")]
    [InlineData("{\"$ref\":\"#/definitions/MissingType\"}")]
    public async Task UnsupportedPropertiesReportTypePropertyPathAndOriginalJson(string property)
    {
        var json = "{\"definitions\":{\"Broken\":{\"type\":\"object\",\"properties\":{\"value\":" + property + "}}}}";
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => Analyze(json));
        Assert.Contains("Broken.value", exception.Message);
        Assert.Contains("#/definitions/Broken/properties/value", exception.Message);
        Assert.Contains("Schema:", exception.Message);
        Assert.Contains(JToken.Parse(property).ToString(), exception.Message);
    }

    [Fact]
    public async Task DictionariesWithTypedPropertiesFailAndAnyIsExplicit()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Analyze(
                """{"type":"object","properties":{"known":{"type":"string"}},"additionalProperties":{"type":"integer"}}"""));
        var model = Assert.IsType<ObjectTypeModel>(Assert.Single(await Analyze(
            """{"title":"AnyHolder","type":"object","properties":{"first":true,"second":{},"map":{"type":"object","additionalProperties":true}}}""")));
        Assert.Equal(TypeKind.Any, model.Properties.Single(p => p.JsonName == "first").Type.Kind);
        Assert.Equal(TypeKind.Any, model.Properties.Single(p => p.JsonName == "second").Type.Kind);
        Assert.Equal(TypeKind.Dictionary, model.Properties.Single(p => p.JsonName == "map").Type.Kind);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Analyze("""{"definitions":{"Allowed":{"type":"string"}},"not":{}}"""));
    }

    [Fact]
    public async Task AllOfFlattensInheritedPropertiesAndRejectsDuplicatesAndCycles()
    {
        var models = await Analyze(
            """{"definitions":{"Base":{"type":"object","properties":{"a":{"type":"string"}},"required":["a"]},"Child":{"allOf":[{"$ref":"#/definitions/Base"},{"type":"object","properties":{"b":{"type":"integer"}},"required":["b"]}]}}}""");
        var child = Assert.IsType<ObjectTypeModel>(models.Single(m => m.Name == "Child"));
        Assert.Equal(["a", "b"], child.Properties.Select(p => p.JsonName));
        Assert.All(child.Properties, p => Assert.True(p.Required));
        Assert.Null(child.BaseType);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Analyze(
                """{"title":"Duplicate","allOf":[{"type":"object","properties":{"a":{"type":"string"}}},{"type":"object","properties":{"a":{"type":"integer"}}}]}"""));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Analyze("""{"definitions":{"Loop":{"type":"object","allOf":[{"$ref":"#/definitions/Loop"}]}}}"""));
    }

    [Fact]
    public async Task NullableUnionsDoNotCreateUnionModels()
    {
        var models = await Analyze(
            """{"definitions":{"Foo":{"type":"object"},"Holder":{"type":"object","properties":{"first":{"anyOf":[{"$ref":"#/definitions/Foo"},{"type":"null"}]},"second":{"oneOf":[{"type":"null"},{"$ref":"#/definitions/Foo"}]}},"required":["first"]}}}""");
        var holder = Assert.IsType<ObjectTypeModel>(models.Single(m => m.Name == "Holder"));
        Assert.DoesNotContain(models, m => m is UnionTypeModel);
        Assert.All(holder.Properties, p =>
        {
            Assert.Equal(TypeKind.Nullable, p.Type.Kind);
            Assert.Equal("Foo", p.Type.Element!.Name);
        });
        Assert.True(holder.Properties.Single(p => p.JsonName == "first").Required);
        Assert.False(holder.Properties.Single(p => p.JsonName == "second").Required);
    }

    [Fact]
    public async Task OptionalPropertiesPreserveSchemaNullabilityAsync()
    {
        var models = await Analyze(
            """{"title":"Presence","type":"object","properties":{"requiredText":{"type":"string"},"requiredNullableText":{"type":["string","null"]},"optionalText":{"type":"string"},"optionalNullableText":{"type":["string","null"]},"optionalCount":{"type":"integer"},"optionalNullableCount":{"type":["integer","null"]},"optionalLiteral":{"type":"string","enum":["only"]}},"required":["requiredText","requiredNullableText"]}""");
        var model = Assert.IsType<ObjectTypeModel>(Assert.Single(models));
        Assert.Equal(TypeKind.Primitive, model.Properties.Single(p => p.JsonName == "optionalText").Type.Kind);
        Assert.Equal(TypeKind.Nullable, model.Properties.Single(p => p.JsonName == "optionalNullableText").Type.Kind);
        Assert.Null(model.Properties.Single(p => p.JsonName == "optionalLiteral").Constant);

        var code = new CSharpGenerator().Generate(models)["Presence.g.cs"];
        Assert.Contains("public string RequiredText { get; set; } = default!;", code);
        Assert.Contains("public string? RequiredNullableText { get; set; } = default!;", code);
        Assert.Contains("public Optional<string> OptionalText { get; set; }", code);
        Assert.Contains("public Optional<string?> OptionalNullableText { get; set; }", code);
        Assert.Contains("public Optional<long> OptionalCount { get; set; }", code);
        Assert.Contains("public Optional<long?> OptionalNullableCount { get; set; }", code);
        Assert.Contains("public Optional<string> OptionalLiteral { get; set; }", code);
        Assert.Contains("JsonIgnoreCondition.WhenWritingDefault", code);
        Assert.DoesNotContain("JsonIgnoreCondition.WhenWritingNull", code);
    }

    [Fact]
    public async Task OptionalSupportTypeNamesAreReservedAsync()
    {
        var models = await Analyze(
            """{"definitions":{"Optional":{"type":"object"},"OptionalJsonConverterFactory":{"type":"object"},"Holder":{"type":"object","properties":{"value":{"$ref":"#/definitions/Optional"}}}}}""");
        var files = new CSharpGenerator().Generate(models);
        Assert.DoesNotContain("Optional.g.cs", files.Keys);
        Assert.DoesNotContain("OptionalJsonConverterFactory.g.cs", files.Keys);
        Assert.Contains("public Optional<Optional2> Value { get; set; }", files["Holder.g.cs"]);
    }

    [Fact]
    public async Task ConstantPropertiesGenerateRequiredValidationAndReserveAttributeNamesAsync()
    {
        var models = await Analyze(
            """{"definitions":{"JsonInclude":{"type":"object"},"JsonRequired":{"type":"object"},"Fixed":{"type":"object","properties":{"kind":{"type":"string","enum":["fixed"]}},"required":["kind"]}}}""");
        var files = new CSharpGenerator().Generate(models);
        Assert.DoesNotContain("JsonInclude.g.cs", files.Keys);
        Assert.DoesNotContain("JsonRequired.g.cs", files.Keys);
        var code = files["Fixed.g.cs"];
        Assert.Contains("using System.Text.Json;", code);
        Assert.Contains("[JsonInclude]\n    [JsonRequired]\n    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]", code);
        Assert.Contains("get => \"fixed\";", code);
        Assert.Contains("private set", code);
        Assert.Contains("if (value != \"fixed\")", code);
        Assert.Contains("throw new JsonException(", code);
    }

    [Theory]
    [InlineData("Thread/startRequest", "ThreadStartRequest")]
    [InlineData("Thread/name/setRequest", "ThreadNameSetRequest")]
    [InlineData("McpServer/oauth/loginRequest", "McpServerOauthLoginRequest")]
    [InlineData("plugin/share/updateTargets", "PluginShareUpdateTargets")]
    [InlineData("123.foo-bar_baz qux", "_123FooBarBazQux")]
    [InlineData("namespace", "Namespace")]
    public void NamesAreLegalAndStable(string input, string expected)
    {
        Assert.Equal(expected, CSharpNaming.TypeName(input));
    }

    [Fact]
    public async Task DefinitionAndConverterNamesCannotCollideIncludingCaseOnlyDifferences()
    {
        var models = await Analyze(
            """{"definitions":{"Foo":{"type":"object","properties":{"foo":{"type":"string"},"a_b":{"type":"string"},"a-b":{"type":"string"},"Clone":{"type":"string"}}},"foo":{"type":"object"},"FooJsonConverter":{"enum":["a","b"],"type":"string"},"123/bad":{"type":"object"}}}""");
        var files = new CSharpGenerator().Generate(models);
        Assert.Equal(files.Count, files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var foo = Assert.IsType<ObjectTypeModel>(models.Single(m => m.Name == "Foo"));
        Assert.Equal(foo.Properties.Count,
            foo.Properties.Select(p => p.CSharpName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(foo.Properties, p => p.CSharpName is "Foo" or "Clone");
    }

    [Fact]
    public async Task TaggedBranchAndDefinitionOrderingIsStable()
    {
        var json = JObject.Parse(
            """{"definitions":{"Message":{"oneOf":[{"title":"a/b","type":"object","properties":{"method":{"type":"string","enum":["b"]}},"required":["method"]},{"title":"a-b","type":"object","properties":{"method":{"type":"string","enum":["a"]}},"required":["method"]}]}}}""");
        var first = new CSharpGenerator().Generate(await Analyze(json.ToString()));
        var branches = (JArray)json["definitions"]!["Message"]!["oneOf"]!;
        json["definitions"]!["Message"]!["oneOf"] = new JArray(branches.Reverse());
        var second = new CSharpGenerator().Generate(await Analyze(json.ToString()));
        Assert.Equal(first.ToArray(), second.ToArray());
        var model = Assert.IsType<UnionTypeModel>((await Analyze(json.ToString())).Single(m => m.Name == "Message"));
        Assert.Equal(["a", "b"], model.Members.Select(m => m.DiscriminatorValue));
        Assert.Equal("method", model.DiscriminatorProperty);
        json["definitions"]!["Message"]!["oneOf"]![0]!["properties"]!["method"]!["enum"] = new JArray("b");
        await Assert.ThrowsAsync<NotSupportedException>(() => Analyze(json.ToString()));
    }

    [Theory]
    [InlineData("integer", "int32", "int")]
    [InlineData("integer", "int64", "long")]
    [InlineData("integer", "uint16", "ushort")]
    [InlineData("integer", "uint32", "uint")]
    [InlineData("integer", "uint64", "ulong")]
    [InlineData("integer", "uint", "ulong")]
    [InlineData("number", "float", "float")]
    [InlineData("number", "double", "double")]
    [InlineData("string", "uuid", "Guid")]
    [InlineData("string", "date-time", "DateTimeOffset")]
    public async Task FormatsPreservePrimitiveWidthAndMeaning(string type, string format, string expected)
    {
        var models =
            await Analyze(
                """{"title":"Formatted","type":"object","properties":{"value":{"type":"TYPE","format":"FORMAT"}},"required":["value"]}"""
                    .Replace("TYPE", type).Replace("FORMAT", format));
        Assert.Equal(expected, Assert.IsType<ObjectTypeModel>(Assert.Single(models)).Properties.Single().Type.Name);
    }

    [Fact]
    public async Task GenerationOnlyCleansTopLevelGeneratedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codex-generator-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Old.g.cs"), "stale");
            await File.WriteAllTextAsync(Path.Combine(directory, "Manual.cs"), "keep");
            await File.WriteAllTextAsync(Path.Combine(directory, "nested", "Keep.g.cs"), "keep");
            new CSharpGenerator().Write(await Analyze("""{"title":"Fresh","type":"object"}"""), directory);
            Assert.False(File.Exists(Path.Combine(directory, "Old.g.cs")));
            Assert.True(File.Exists(Path.Combine(directory, "Fresh.g.cs")));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(directory, "Manual.cs")));
            Assert.True(File.Exists(Path.Combine(directory, "nested", "Keep.g.cs")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
