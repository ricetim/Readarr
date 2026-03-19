using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    public class STJUtcConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                // Empty/null string: return default for non-nullable DateTime, let nullable wrapper handle null
                return default;
            }

            if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt.ToUniversalTime();
            }

            // Unparseable dates (e.g. ancient years like "79-01-01T07:00:00Z") — return default
            return default;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ssZ"));
        }
    }
}
