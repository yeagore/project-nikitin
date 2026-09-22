using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One version of the economy, whole in one file under <c>webs/</c>: its own palette of goods
/// and tags, which of those goods are on the canvas, the recipes that join them, the consumers
/// they lead to, and where each node sits. Nothing is shared between webs. Raw inputs are simply
/// the goods nothing here makes, and final goods the ones nothing here uses; neither is stored.
/// </summary>
public sealed class EconomyWeb
{
	public int Format { get; set; } = EconomyStore.Format;

	/// <summary>Lowercase with hyphens; the file is <c>webs/&lt;id&gt;.json</c>.</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary>
	/// A reference copy: the lab opens it to look at, to cut from and to import from, and refuses to
	/// change it or bin it. To work on it, make a copy. Unlocking is an edit to the file, on purpose.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Locked { get; set; }

	/// <summary>The web's own goods, tags and sprite sheets.</summary>
	public Palette Palette { get; set; } = new();

	/// <summary>Ids of the palette goods that are on the canvas.</summary>
	public List<string> Goods { get; set; } = new();

	public List<Recipe> Recipes { get; set; } = new();
	public List<Consumer> Consumers { get; set; } = new();

	/// <summary>How many people the consumers speak for; null means none, and then nothing is wanted.</summary>
	public double? Heads { get; set; }

	/// <summary>
	/// What the land gives without a recipe, in units a day by good: the rate at a source (grain
	/// from the farms, ore from the mine). Null when nothing is set. Read through <see cref="SupplyOf"/>;
	/// set through <see cref="EconomyEdit.SetSupply"/>, which keeps the map tidy.
	/// </summary>
	[JsonConverter(typeof(SupplyConverter))]
	public Dictionary<string, double>? Supply { get; set; }

	/// <summary>Units a day of the good the land gives; 0 for a good with no supply set.</summary>
	public double SupplyOf(string goodId) => Supply != null && Supply.TryGetValue(goodId, out double rate) ? rate : 0;

	/// <summary>Canvas position by node key: a good's, a recipe's or a consumer's id, which never collide.</summary>
	[JsonConverter(typeof(LayoutConverter))]
	public Dictionary<string, Spot> Layout { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Holds(string goodId) => Goods.Contains(goodId);

	public Recipe? Recipe(string id) => Recipes.FirstOrDefault(r => r.Id == id);

	public Consumer? Consumer(string id) => Consumers.FirstOrDefault(c => c.Id == id);

	/// <summary>Written sorted by good, so the same web reads the same and a changed rate is a one-line diff.</summary>
	private sealed class SupplyConverter : JsonConverter<Dictionary<string, double>>
	{
		public override Dictionary<string, double> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
		{
			var supply = new Dictionary<string, double>(StringComparer.Ordinal);
			if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A supply is an object of units a day by good.");
			while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
			{
				string key = reader.GetString()!;
				reader.Read();
				supply[key] = reader.GetDouble();
			}
			return supply;
		}

		public override void Write(Utf8JsonWriter writer, Dictionary<string, double> value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			foreach (string key in value.Keys.OrderBy(k => k, StringComparer.Ordinal)) writer.WriteNumber(key, value[key]);
			writer.WriteEndObject();
		}
	}

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
