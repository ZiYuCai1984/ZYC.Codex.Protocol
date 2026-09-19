using System.Collections.Concurrent;
using System.Text.Json;
using ZYC.Codex.Protocol;

namespace ZYC.Codex.Protocol.Generator.Tests;

public class RequestIdTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("\"\"")]
    [InlineData("\"001\"")]
    [InlineData("\"request-abc\"")]
    public void DeserializedIdsHaveValueEqualityAndMatchingHashes(string json)
    {
        var first = JsonSerializer.Deserialize<RequestId>(json)!;
        var second = JsonSerializer.Deserialize<RequestId>(JsonSerializer.Serialize(first))!;

        Assert.NotSame(first, second);
        Assert.True(first == second);
        Assert.True(second == first);
        Assert.False(first != second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(((IEquatable<RequestId>)first).Equals(second));
        Assert.True(EqualityComparer<RequestId>.Default.Equals(first, second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<RequestId> { first, second });
        Assert.Equal(json, JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("\"abc\"", "\"ABC\"")]
    [InlineData("\"abc\"", "\"abc \"")]
    [InlineData("\"001\"", "\"1\"")]
    [InlineData("123", "\"123\"")]
    public void DifferentValuesAndTokenKindsAreNotEqual(string firstJson, string secondJson)
    {
        var first = JsonSerializer.Deserialize<RequestId>(firstJson)!;
        var second = JsonSerializer.Deserialize<RequestId>(secondJson)!;

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.False(first.Equals(second));
        Assert.False(second.Equals(first));
        Assert.False(first.Equals((object)second));
        Assert.Equal(2, new HashSet<RequestId> { first, second }.Count);
    }

    [Fact]
    public void EqualityHandlesNullReferencesAndUnrelatedObjects()
    {
        RequestId value = "abc";
        RequestId sameReference = value;
        RequestId? absent = null;
        RequestId? alsoAbsent = null;

        Assert.True(value == sameReference);
        Assert.True(value.Equals(sameReference));
        Assert.True(absent == alsoAbsent);
        Assert.False(absent != alsoAbsent);
        Assert.False(value == absent);
        Assert.False(absent == value);
        Assert.True(value != absent);
        Assert.True(absent != value);
        Assert.False(value.Equals(absent));
        Assert.False(value.Equals((object?)null));
        Assert.False(value.Equals((object)"abc"));
    }

    [Fact]
    public void ImplicitConversionsAndConcreteConstructorsUseTheSameValues()
    {
        RequestId integer = 123L;
        RequestId text = "123";

        Assert.True(integer == new RequestIdInteger(123L));
        Assert.True(text == new RequestIdString("123"));
        Assert.Equal("123", JsonSerializer.Serialize(new RequestIdInteger(123L)));
        Assert.Equal("\"123\"", JsonSerializer.Serialize(new RequestIdString("123")));
        Assert.True(integer.Equals(JsonSerializer.Deserialize<RequestIdInteger>("123")));
        Assert.True(text.Equals(JsonSerializer.Deserialize<RequestIdString>("\"123\"")));
        Assert.Throws<ArgumentNullException>(() => new RequestIdString(null!));
        Assert.Throws<ArgumentNullException>(() => (RequestId)(string)null!);
    }

    [Fact]
    public void ResponsesAndErrorsMatchPendingRequestsByIdValue()
    {
        RequestId integer = 123L;
        RequestId text = "123";
        var pending = new ConcurrentDictionary<RequestId, string>();
        pending[integer] = "integer request";
        pending[text] = "string request";

        var response = Assert.IsType<JSONRPCMessageJSONRPCResponse>(
            JsonSerializer.Deserialize<JSONRPCMessage>("""{"id":"123","result":{}}"""));

        Assert.NotSame(text, response.Value.Id);
        Assert.True(pending.TryRemove(response.Value.Id, out var completed));
        Assert.Equal("string request", completed);
        Assert.Single(pending);
        Assert.True(pending.ContainsKey(integer));

        var error = Assert.IsType<JSONRPCMessageJSONRPCError>(
            JsonSerializer.Deserialize<JSONRPCMessage>("""{"id":123,"error":{"code":-1,"message":"failed"}}"""));

        Assert.NotSame(integer, error.Value.Id);
        Assert.True(pending.TryRemove(error.Value.Id, out var failed));
        Assert.Equal("integer request", failed);
        Assert.Empty(pending);
    }
}
