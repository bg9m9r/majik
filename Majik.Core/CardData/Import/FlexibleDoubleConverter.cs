using System.Text.Json;
using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Import;

/// <summary>
/// Custom JSON converter that handles double values that may be numbers, strings, or null.
/// Used for fields like 'loyalty' and 'cmc' that may have inconsistent formats in Scryfall data.
/// </summary>
public class FlexibleDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(stringValue) || stringValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (double.TryParse(stringValue, out var parsedValue))
            {
                return parsedValue;
            }

            // If we can't parse it, return null instead of throwing
            return null;
        }

        // For any other token type, return null
        return null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
