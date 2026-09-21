using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>A node's place on the canvas, in whole pixels so a saved web does not churn; written as <c>[x, y]</c>.</summary>
[JsonConverter(typeof(SpotConverter))]
public readonly record struct Spot(int X, int Y)
{
	internal string ToJson() => FormattableString.Invariant($"[{X}, {Y}]");

	internal static Spot Read(ref Utf8JsonReader reader)
	{
		if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A spot is [x, y].");
		reader.Read();
		int x = (int)Math.Round(reader.GetDouble());
		reader.Read();
		int y = (int)Math.Round(reader.GetDouble());
		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("A spot is [x, y].");
		return new Spot(x, y);
	}

	private sealed class SpotConverter : JsonConverter<Spot>
	{
		public override Spot Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Spot.Read(ref reader);

		public override void Write(Utf8JsonWriter writer, Spot value, JsonSerializerOptions options) =>
			writer.WriteRawValue(value.ToJson());
	}
}
