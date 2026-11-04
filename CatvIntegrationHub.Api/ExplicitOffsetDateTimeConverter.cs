using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CatvIntegrationHub.Api;

public sealed class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Authorization dates must be ISO 8601 strings with Z or an explicit timezone offset.");

        string? value = reader.GetString();
        if (value is null || !Regex.IsMatch(value, @"(?:Z|[+-]\d{2}:\d{2})$") ||
            !reader.TryGetDateTimeOffset(out var date))
            throw new JsonException("Authorization dates must be valid ISO 8601 timestamps with Z or an explicit timezone offset.");

        return date;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
