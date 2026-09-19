using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZYC.Codex.Protocol;

public class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(valueType))!;
    }

    private class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public OptionalJsonConverter()
        {
        }

        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new Optional<T>(JsonSerializer.Deserialize<T>(ref reader, options)!);
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.IsSpecified)
            {
                throw new JsonException("An unspecified optional value must be omitted by its containing property.");
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
