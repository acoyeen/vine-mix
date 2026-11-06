using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VineMix.Desktop;

// Canonical settings contain a finite repeat count. Earlier unreleased builds
// stored feedback instead; convert it once on reading, including clipboard data.
internal sealed class EchoSettingsJsonConverter : JsonConverter<EchoSettings>
{
    public override EchoSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Echo settings must be an object.");
        var result = new EchoSettings();
        double? feedback = null;
        bool hasRepeats = false;
        foreach (var property in root.EnumerateObject())
        {
            string name = options.PropertyNameCaseInsensitive ? property.Name.ToLowerInvariant() : property.Name;
            switch (name)
            {
                case "timeMs" when !options.PropertyNameCaseInsensitive:
                case "timems" when options.PropertyNameCaseInsensitive:
                    result.TimeMs = Number(property.Value, 20, 2000, "Time"); break;
                case "mix":
                    result.Mix = Number(property.Value, 0, 1, "Mix"); break;
                case "repeats":
                    double count = Number(property.Value, 1, 8, "Repeats");
                    if (count != Math.Truncate(count)) throw new JsonException("Echo Repeats must be a whole number.");
                    result.Repeats = (int)count; hasRepeats = true; break;
                case "feedback":
                    feedback = Number(property.Value, 0, .9, "legacy Feedback"); break;
            }
        }
        if (!hasRepeats && feedback is double value)
        {
            int count = 1;
            for (double level = value; count < 8 && level >= .01; level *= value) count++;
            result.Repeats = count;
        }
        return result;
    }

    private static double Number(JsonElement element, double minimum, double maximum, string name)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value)
            || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new JsonException("Echo " + name + " is outside its supported range.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, EchoSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("timeMs", value.TimeMs);
        writer.WriteNumber("repeats", value.Repeats);
        writer.WriteNumber("mix", value.Mix);
        writer.WriteEndObject();
    }
}
