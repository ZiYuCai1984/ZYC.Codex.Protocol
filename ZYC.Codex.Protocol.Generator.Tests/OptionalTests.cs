using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZYC.Codex.Protocol;

namespace ZYC.Codex.Protocol.Generator.Tests;

public class OptionalTests
{
    private static T RoundTrip<T>(string json, JsonSerializerOptions? options = null)
    {
        var value = JsonSerializer.Deserialize<T>(json, options)!;
        var serialized = JsonSerializer.Serialize(value, options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(serialized)), serialized);
        return value;
    }

    [Fact]
    public void PresenceParticipatesInEqualityAndDefaultValueComparison()
    {
        var unspecified = Optional<string?>.Unspecified;
        Optional<string?> cleared = null;
        Optional<string?> assigned = "main";

        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
        Assert.True(cleared.IsSpecified);
        Assert.Null(cleared.Value);
        Assert.Equal("main", assigned.Value);
        Assert.True(unspecified == default(Optional<string?>));
        Assert.True(unspecified != cleared);
        Assert.Equal(new Optional<string?>(null), cleared);
        Assert.Equal(new Optional<string?>("main"), assigned);
        Assert.Equal(new Optional<string?>("main").GetHashCode(), assigned.GetHashCode());
        Assert.Equal(3, new HashSet<Optional<string?>> { unspecified, cleared, assigned, new("main") }.Count);
        Assert.False(EqualityComparer<Optional<int>>.Default.Equals(default, new Optional<int>(0)));
        Assert.False(EqualityComparer<Optional<bool>>.Default.Equals(default, new Optional<bool>(false)));
    }

    [Theory]
    [InlineData(JsonIgnoreCondition.Never)]
    [InlineData(JsonIgnoreCondition.WhenWritingNull)]
    [InlineData(JsonIgnoreCondition.WhenWritingDefault)]
    public void GitMetadataPreservesAllThreeStatesRegardlessOfGlobalIgnoreCondition(JsonIgnoreCondition condition)
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = condition };
        var omitted = RoundTrip<ThreadMetadataGitInfoUpdateParams>("{}", options);
        var cleared = RoundTrip<ThreadMetadataGitInfoUpdateParams>("""{"branch":null}""", options);
        var assigned = RoundTrip<ThreadMetadataGitInfoUpdateParams>("""{"branch":"main"}""", options);

        Assert.False(omitted.Branch.IsSpecified);
        Assert.True(cleared.Branch.IsSpecified);
        Assert.Null(cleared.Branch.Value);
        Assert.Equal("main", assigned.Branch.Value);
        Assert.False(cleared.OriginUrl.IsSpecified);
        Assert.False(cleared.Sha.IsSpecified);
        RoundTrip<ThreadMetadataGitInfoUpdateParams>("""{"branch":null,"originUrl":null,"sha":null}""", options);
    }

    [Fact]
    public void ObjectInitializersCanClearAssignAndResetFields()
    {
        var patch = new ThreadMetadataGitInfoUpdateParams();
        Assert.Equal("{}", JsonSerializer.Serialize(patch));
        patch.Branch = null;
        Assert.Equal("""{"branch":null}""", JsonSerializer.Serialize(patch));
        patch.Branch = "main";
        Assert.Equal("""{"branch":"main"}""", JsonSerializer.Serialize(patch));
        patch.Branch = Optional<string?>.Unspecified;
        Assert.Equal("{}", JsonSerializer.Serialize(patch));
    }

    [Fact]
    public void NestedPatchesKeepPresenceThroughTheRequestUnion()
    {
        ClientRequest request = new ThreadMetadataUpdateRequest
        {
            Id = 1L,
            Params = new ThreadMetadataUpdateParams
            {
                ThreadId = "t1",
                GitInfo = new ThreadMetadataGitInfoUpdateParams { Branch = null }
            }
        };
        const string expected =
            """{"id":1,"method":"thread/metadata/update","params":{"gitInfo":{"branch":null},"threadId":"t1"}}""";
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(JsonSerializer.Serialize(request))));
        var restored = Assert.IsType<ThreadMetadataUpdateRequest>(RoundTrip<ClientRequest>(expected));
        Assert.True(restored.Params.GitInfo.IsSpecified);
        Assert.True(restored.Params.GitInfo.Value!.Branch.IsSpecified);
        Assert.Null(restored.Params.GitInfo.Value.Branch.Value);
        RoundTrip<ThreadMetadataUpdateParams>("""{"threadId":"t1","gitInfo":null}""");
        RoundTrip<ThreadMetadataUpdateParams>("""{"threadId":"t1","gitInfo":{}}""");
        RoundTrip<ThreadMetadataUpdateParams>("""{"threadId":"t1"}""");
    }

    [Theory]
    [InlineData("{}", false, null)]
    [InlineData("{\"sectionId\":null}", true, null)]
    [InlineData("{\"sectionId\":\"section-1\"}", true, "section-1")]
    public void SectionFiltersDistinguishAllUnsectionedAndOneSection(string json, bool specified, string? sectionId)
    {
        var parameters = RoundTrip<ThreadListParams>(json);
        Assert.Equal(specified, parameters.SectionId.IsSpecified);
        if (specified)
        {
            Assert.Equal(sectionId, parameters.SectionId.Value);
        }
    }

    [Theory]
    [InlineData("{\"name\":\"Work\",\"sectionId\":\"s1\"}")]
    [InlineData("{\"name\":\"Work\",\"sectionId\":\"s1\",\"appearance\":null}")]
    [InlineData("{\"name\":\"Work\",\"sectionId\":\"s1\",\"appearance\":{\"color\":null,\"icon\":\"star\"}}")]
    public void SectionAppearanceCanBePreservedClearedOrReplaced(string json)
    {
        RoundTrip<ThreadSectionUpdateParams>(json);
    }

    [Fact]
    public void SpecifiedDefaultPrimitivesAreWrittenAndNonNullableValueTypesStillRejectNull()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
        var value = new FixtureNaming { _123 = 0L, Clone2 = false };
        Assert.Equal("""{"123":0,"Clone":false}""", JsonSerializer.Serialize(value, options));
        var restored = RoundTrip<FixtureNaming>("""{"123":0,"Clone":false}""", options);
        Assert.True(restored._123.IsSpecified);
        Assert.Equal(0L, restored._123.Value);
        Assert.True(restored.Clone2.IsSpecified);
        Assert.False(restored.Clone2.Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixtureNaming>("""{"123":null}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixtureNaming>("""{"Clone":null}"""));
    }

    [Fact]
    public void NullablePrimitivesCollectionsAndArbitraryJsonKeepExplicitNull()
    {
        RoundTrip<ThreadStartParams>("""{"ephemeral":null}""");
        RoundTrip<ThreadStartParams>("""{"ephemeral":false}""");
        RoundTrip<ThreadStartParams>("""{"config":null}""");
        RoundTrip<ThreadStartParams>("""{"config":{}}""");
        RoundTrip<ThreadListParams>("""{"modelProviders":null}""");
        RoundTrip<ThreadListParams>("""{"modelProviders":[]}""");
        RoundTrip<ThreadListParams>("""{"modelProviders":["provider"]}""");
        const string required = "\"name\":\"test\",\"requiredNull\":null,\"enabled\":false,\"labels\":[]";
        var empty = RoundTrip<FixturePayload>("{" + required + "}");
        Assert.False(empty.Any.IsSpecified);
        Assert.False(empty.Child.IsSpecified);
        var value = RoundTrip<FixturePayload>("{" + required +
            ",\"any\":null,\"child\":null,\"otherChild\":null,\"map\":{},\"unknowns\":{\"key\":null}}");
        Assert.True(value.Any.IsSpecified);
        Assert.Equal(JsonValueKind.Null, value.Any.Value.ValueKind);
        Assert.True(value.Child.IsSpecified);
        Assert.Null(value.Child.Value);
        Assert.True(value.Map.IsSpecified);
        Assert.Empty(value.Map.Value);
    }

    [Fact]
    public void ConverterUsesPayloadConvertersAndSerializerOptions()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        var number = JsonSerializer.Deserialize<Optional<int>>("\"42\"", options);
        Assert.True(number.IsSpecified);
        Assert.Equal(42, number.Value);
        RoundTrip<Optional<FixtureEnum>>("\"thread/start\"");
        RoundTrip<Optional<RequestId>>("\"001\"");
        RoundTrip<Optional<long?>>("null");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Optional<FixtureEnum>>("\"unknown\""));
    }

    [Fact]
    public void UnspecifiedValuesCannotSilentlyBecomeNullOutsideOptionalProperties()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(Optional<string?>.Unspecified));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new[] { Optional<string?>.Unspecified }));
        Assert.Equal("null", JsonSerializer.Serialize(new Optional<string?>(null)));
    }
}
