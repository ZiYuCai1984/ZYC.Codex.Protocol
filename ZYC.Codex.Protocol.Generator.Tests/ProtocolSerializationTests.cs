using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ZYC.Codex.Protocol.Generator.Tests;

public class ProtocolSerializationTests
{
    [Fact]
    public void ProtocolAssemblyTargetsNetStandard20()
    {
        var framework = typeof(ClientRequest).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Assert.NotNull(framework);
        Assert.Equal(".NETStandard,Version=v2.0", framework.FrameworkName);
    }

    private static T RoundTrip<T>(string json, JsonSerializerOptions? options = null)
    {
        var value = JsonSerializer.Deserialize<T>(json, options)!;
        var serialized = JsonSerializer.Serialize(value, options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(serialized)), serialized);
        return value;
    }

    [Fact]
    public void ClientRequestsSelectConcreteTypesAndPreserveWireValues()
    {
        var start = Assert.IsType<ThreadStartRequest>(
            RoundTrip<ClientRequest>(
                """{"id":1,"method":"thread/start","params":{"model":"test","ephemeral":false}}"""));
        Assert.Equal(1, Assert.IsType<RequestIdInteger>(start.Id).Value);
        Assert.Equal("thread/start", start.Method);
        Assert.True(start.Params.Ephemeral.IsSpecified);
        Assert.Equal(false, start.Params.Ephemeral.Value);
        Assert.IsType<ThreadResumeRequest>(
            RoundTrip<ClientRequest>("""{"id":"resume-1","method":"thread/resume","params":{"threadId":"t1"}}"""));
        Assert.IsType<InitializeRequest>(RoundTrip<ClientRequest>(
            """{"id":2,"method":"initialize","params":{"clientInfo":{"name":"test","version":"1.0"}}}"""));
        RoundTrip<ThreadStartRequest>("""{"id":3,"method":"thread/start","params":{}}""");
        ClientRequest created = new ThreadStartRequest { Id = 4L, Params = new ThreadStartParams() };
        Assert.Equal("""{"id":4,"method":"thread/start","params":{}}""", JsonSerializer.Serialize(created));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"method\":123}")]
    [InlineData("{\"method\":null}")]
    [InlineData("{\"method\":\"future/method\",\"id\":1,\"params\":{}}")]
    [InlineData("{\"method\":\"thread/start\",\"params\":{}}")]
    [InlineData("{\"method\":\"thread/start\",\"id\":1}")]
    public void InvalidRequestsFail(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientRequest>(json));
    }

    [Theory]
    [InlineData("{\"id\":1,\"params\":{}}")]
    [InlineData("{\"id\":1,\"method\":null,\"params\":{}}")]
    [InlineData("{\"id\":1,\"method\":123,\"params\":{}}")]
    [InlineData("{\"id\":1,\"method\":{},\"params\":{}}")]
    [InlineData("{\"id\":1,\"method\":\"Thread/Start\",\"params\":{}}")]
    [InlineData("{\"id\":1,\"method\":\"thread/resume\",\"params\":{\"threadId\":\"t1\"}}")]
    public void ConcreteRequestsRejectMissingOrIncorrectMethods(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ThreadStartRequest>(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"method\":null}")]
    [InlineData("{\"method\":false}")]
    [InlineData("{\"method\":\"initialize\"}")]
    public void ConcreteNotificationsRequireTheirMethod(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InitializedNotification>(json));
    }

    [Theory]
    [InlineData("{\"text\":\"hello\"}")]
    [InlineData("{\"type\":null,\"text\":\"hello\"}")]
    [InlineData("{\"type\":123,\"text\":\"hello\"}")]
    [InlineData("{\"type\":\"image\",\"text\":\"hello\"}")]
    public void ConcretePayloadsRequireTheirType(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TextUserInput>(json));
    }

    [Fact]
    public void ConstantFieldsRoundTripThroughConcreteAndBaseTypesWithCustomOptions()
    {
        var options = new JsonSerializerOptions
        {
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };
        const string request = """{"id":1,"method":"thread/start","params":{}}""";
        RoundTrip<ThreadStartRequest>(request, options);
        Assert.IsType<ThreadStartRequest>(RoundTrip<ClientRequest>(request, options));
        RoundTrip<InitializedNotification>("""{"method":"initialized"}""", options);
        Assert.IsType<InitializedNotification>(RoundTrip<ClientNotification>("""{"method":"initialized"}""", options));
        RoundTrip<TextUserInput>("""{"type":"text","text":"hello"}""", options);
        Assert.IsType<TextUserInput>(RoundTrip<UserInput>("""{"type":"text","text":"hello"}""", options));

        ClientRequest created = new ThreadStartRequest { Id = 2L, Params = new ThreadStartParams() };
        Assert.Equal("""{"id":2,"method":"thread/start","params":{}}""", JsonSerializer.Serialize(created, options));
    }

    [Fact]
    public void ApprovalDtosAndAttestationAreStronglyTyped()
    {
        var response = RoundTrip<ApplyPatchApprovalResponse>("""{"decision":"approved"}""");
        Assert.IsType<ReviewDecisionApproved>(response.Decision);
        RoundTrip<ApplyPatchApprovalResponse>("""{"decision":{"denied":{"rejection":"no"}}}""");
        RoundTrip<ApplyPatchApprovalParams>(
            """{"callId":"c1","conversationId":"t1","fileChanges":{"a.txt":{"type":"add","content":"hello"}}}""");
        Assert.Equal("token", RoundTrip<AttestationGenerateResponse>("""{"token":"token"}""").Token);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AttestationGenerateResponse>("{}"));
    }

    [Fact]
    public void NullableAndOptionalFieldsHaveIndependentPresenceRules()
    {
        const string json =
            """{"name":"test","requiredNull":null,"enabled":false,"labels":["thread/start","foo-bar","snake_case"]}""";
        var value = RoundTrip<FixturePayload>(json,
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
        Assert.Null(value.RequiredNull);
        Assert.False(value.OptionalCount.IsSpecified);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<FixturePayload>("""{"name":"test","enabled":false,"labels":[]}"""));
        RoundTrip<FixturePayload>(
            """{"name":"test","requiredNull":12,"enabled":true,"labels":[],"child":{"value":"a"},"otherChild":{"value":"b"},"map":{"x":{"value":"v"}},"unknowns":{"k":{"nested":true}},"any":[1,"s"]}""");
        RoundTrip<FixtureAllOf>("""{"base":"b","count":null}""");
        RoundTrip<FixtureRecursive>("""{"next":{"next":{}}}""");
    }

    [Theory]
    [InlineData("thread/start")]
    [InlineData("foo-bar")]
    [InlineData("snake_case")]
    public void EnumStringsAreExact(string value)
    {
        RoundTrip<FixtureEnum>(JsonSerializer.Serialize(value));
    }

    [Fact]
    public void EnumsRejectUnknownAndNumericValuesAndSupportDictionaryKeys()
    {
        foreach (var json in new[] { "1", "\"1\"", "\"Thread/Start\"", "\"unknown\"" })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixtureEnum>(json));
        }

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((FixtureEnum)999));
        RoundTrip<Dictionary<FixtureEnum, int>>("""{"thread/start":1,"foo-bar":2}""");
        foreach (var value in new[] { "a-b", "a_b", "A-b", "123", "value__" })
        {
            RoundTrip<FixtureCollisionEnum>(JsonSerializer.Serialize(value));
        }

        RoundTrip<FixtureNaming>(
            """{"type":"a","event":"b","params":"c","namespace":"d","123":1,"foo-bar":"e","foo_bar":"f","FooBar":"g","fixtureNaming":"h","Clone":true}""");
    }

    [Theory]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("\"001\"")]
    public void RequestIdsKeepTheirJsonTokenKind(string json)
    {
        RoundTrip<RequestId>(json);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("{}")]
    public void RequestIdsRejectOtherShapes(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequestId>(json));
    }

    [Fact]
    public void SpecializedUnionsSerializeBothThroughBaseAndConcreteTypes()
    {
        RoundTrip<RequestIdString>("\"001\"");
        RoundTrip<RequestIdInteger>("1");
        RoundTrip<ReviewDecisionApproved>("\"approved\"");
        RoundTrip<AskForApproval>("\"on-request\"");
        RoundTrip<SessionSource>("""{"custom":"integration"}""");
        RoundTrip<FunctionCallOutputBody>("\"ok\"");
        RoundTrip<FunctionCallOutputBody>("[]");
        RoundTrip<ForcedChatgptWorkspaceIds>("[\"a\",\"b\"]");
        RoundTrip<ThreadListCwdFilter>("\"C:/work\"");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewDecision>("\"future\""));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ReviewDecision>("""{"denied":{"rejection":"no"},"unexpected":1}"""));
        Assert.IsType<InitializedNotification>(RoundTrip<ClientNotification>("""{"method":"initialized"}"""));
    }

    [Theory]
    [InlineData("{\"id\":1,\"method\":\"test\",\"params\":{}}")]
    [InlineData("{\"method\":\"test\"}")]
    [InlineData("{\"id\":1,\"result\":null}")]
    [InlineData("{\"id\":1,\"error\":{\"code\":-1,\"message\":\"bad\"}}")]
    [InlineData("{\"id\":1,\"method\":\"test\",\"result\":1}")]
    public void JsonRpcMessagesPreserveWireShapeAndOverlappingAdditionalFields(string json)
    {
        RoundTrip<JSONRPCMessage>(json);
    }

    [Theory]
    [InlineData("{\"type\":\"boolean\",\"default\":true}")]
    [InlineData("{\"type\":\"integer\",\"minimum\":0}")]
    [InlineData("{\"type\":\"string\",\"format\":\"date-time\"}")]
    [InlineData("{\"type\":\"string\",\"enum\":[\"a\"]}")]
    [InlineData("{\"type\":\"string\",\"enum\":[\"a\"],\"enumNames\":[\"A\"]}")]
    [InlineData("{\"type\":\"string\",\"oneOf\":[{\"const\":\"a\",\"title\":\"A\"}]}")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"a\"]}}")]
    [InlineData("{\"type\":\"array\",\"items\":{\"anyOf\":[{\"const\":\"a\",\"title\":\"A\"}]}}")]
    public void McpElicitationShapesRoundTrip(string json)
    {
        RoundTrip<McpElicitationPrimitiveSchema>(json);
    }

    [Fact]
    public void McpCommonFieldsResourceOverlapAndExtensionDataSurvive()
    {
        RoundTrip<McpServerElicitationRequestParams>(
            """{"serverName":"s","threadId":"t","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"enabled":{"type":"boolean"}}}}""");
        RoundTrip<McpServerElicitationRequestParams>(
            """{"serverName":"s","threadId":"t","mode":"openai/form","message":"m","requestedSchema":{"arbitrary":1}}""");
        RoundTrip<ResourceContent>("""{"uri":"test:a","text":"hello","blob":"aGVsbG8="}""");
        RoundTrip<AnalyticsConfig>("""{"enabled":true,"custom":{"value":1}}""");
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<McpElicitationPrimitiveSchema>("""{"type":"future"}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProtocolNull>("1"));
        Assert.Equal("null", JsonSerializer.Serialize(new ProtocolNull()));
    }
}
