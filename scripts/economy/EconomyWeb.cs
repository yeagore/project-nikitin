using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One version of the economy: which goods of the catalogue are in it, the recipes that
/// join them, the consumers they lead to, and where each node sits on the canvas. One
/// file per web under <c>webs/</c>. Raw inputs are simply the goods nothing here makes,
/// and final goods the ones nothing here uses; neither is stored.
/// </summary>
public sealed class EconomyWeb
{
	public int Format { get; set; } = 1;

	/// <summary>Lowercase with hyphens; the file is <c>webs/&lt;id&gt;.json</c>.</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary>Ids of the catalogue goods in this web.</summary>
	public List<string> Goods { get; set; } = new();

	public List<Recipe> Recipes { get; set; } = new();
	public List<Consumer> Consumers { get; set; } = new();

	/// <summary>Canvas position by node key: a good's, a recipe's or a consumer's id, which never collide.</summary>
	[JsonConverter(typeof(LayoutConverter))]
	public Dictionary<string, Spot> Layout { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Holds(string goodId) => Goods.Contains(goodId);

	public Recipe? Recipe(string id) => Recipes.FirstOrDefault(r => r.Id == id);

	public Consumer? Consumer(string id) => Consumers.FirstOrDefault(c => c.Id == id);

	/// <summary>Sorted by key and one node to a line, so a moved node is a one-line diff.</summary>
	private sealed class LayoutConverter : JsonConverter<Dictionary<string, Spot>>
	{
		public override Dictionary<string, Spot> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
		{
			var layout = new Dictionary<string, Spot>(StringComparer.Ordinal);
			if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A layout is an object of [x, y] by node.");
			while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
			{
				string key = reader.GetString()!;
				reader.Read();
				layout[key] = Spot.Read(ref reader);
			}
			return layout;
		}

		public override void Write(Utf8JsonWriter writer, Dictionary<string, Spot> value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			foreach (string key in value.Keys.OrderBy(k => k, StringComparer.Ordinal))
			{
				writer.WritePropertyName(key);
				writer.WriteRawValue(value[key].ToJson());
			}
			writer.WriteEndObject();
		}
	}
}
