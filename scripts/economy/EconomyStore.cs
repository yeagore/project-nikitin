using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// The economy folder on disk: <c>webs/*.json</c>, each a whole web with its palette, and <c>sprites/</c>.
/// Plain JSON in the repository, so a web saved in the lab is versioned with the code and
/// reaches the other machine by a pull. Files are written whole to a sibling and moved
/// into place, so a crash mid-save leaves the old file rather than half a new one.
/// </summary>
public sealed class EconomyStore
{
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>The newest file format this build writes and fully understands.</summary>
	public const int Format = 2;

	/// <summary>The economy folder, absolute.</summary>
	public string Root { get; }

	public EconomyStore(string root) => Root = root;

	public string WebsDir => Path.Combine(Root, "webs");
	public string WebPath(string id) => Path.Combine(WebsDir, id + ".json");

	/// <summary>A sprite's or an atlas's file, which a palette names relative to the economy folder.</summary>
	public string Resolve(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

	/// <summary>Every web on disk by id, with its name, sorted by name. A file that does not parse is skipped.</summary>
	public List<(string Id, string Name)> ListWebs()
	{
		var webs = new List<(string, string)>();
		if (!Directory.Exists(WebsDir)) return webs;
		foreach (string path in Directory.GetFiles(WebsDir, "*.json"))
		{
			try
			{
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
				{
					CommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true,
				});
				string id = Path.GetFileNameWithoutExtension(path);
				string name = doc.RootElement.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? id : id;
				webs.Add((id, name));
			}
			catch (JsonException)
			{
			}
		}
		return webs.OrderBy(w => w.Item2, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Item1, StringComparer.Ordinal).ToList();
	}

	public bool HasWeb(string id) => File.Exists(WebPath(id));

	/// <summary>The web in that file. The file's name wins over an id written inside it, so a copied file is its own web.</summary>
	public EconomyWeb LoadWeb(string id)
	{
		EconomyWeb web = FromJson<EconomyWeb>(File.ReadAllText(WebPath(id)));
		web.Id = id;
		web.Format = Format;
		MigrateSupply(web);
		return web;
	}

	/// <summary>
	/// A web saved before 2026-09-23 may give a raw good a supply, units a day from the land with no
	/// recipe behind it. Every good comes out of something now, so each such rate becomes the
	/// extraction it stood for: a recipe that makes one unit a run with that many at work, standing
	/// nowhere yet (the issue list asks for a site). The old field is not written again.
	/// </summary>
	public static void MigrateSupply(EconomyWeb web)
	{
		if (web.Extra == null || !web.Extra.Remove("supply", out JsonElement supply) || supply.ValueKind != JsonValueKind.Object) return;
		foreach (JsonProperty entry in supply.EnumerateObject().OrderBy(e => e.Name, StringComparer.Ordinal))
		{
			if (entry.Value.ValueKind != JsonValueKind.Number || entry.Value.GetDouble() <= 0 || !web.Holds(entry.Name)) continue;
			string id = "r.land." + entry.Name;
			if (web.Recipe(id) != null) continue;
			web.Recipes.Add(new Recipe
			{
				Id = id,
				Name = "The land",
				Note = "Was a supply of so many a day with no recipe behind it. Give it a site, and an amount a run.",
				Limit = entry.Value.GetDouble(),
				Outputs = { new RecipeOutput { Good = entry.Name } },
			});
		}
		if (web.Extra.Count == 0) web.Extra = null;
	}

	public void SaveWeb(EconomyWeb web)
	{
		Directory.CreateDirectory(WebsDir);
		WriteWhole(WebPath(web.Id), ToJson(web));
	}

	public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Options).Replace("\r\n", "\n") + "\n";

	public static T FromJson<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("The file holds no " + typeof(T).Name + ".");

	/// <summary>A deep copy by way of the JSON, which is also what an undo step keeps.</summary>
	public static T Clone<T>(T value) => FromJson<T>(ToJson(value));

	private static void WriteWhole(string path, string text)
	{
		string dir = Path.GetDirectoryName(path)!;
		Directory.CreateDirectory(dir);
		string temp = path + ".tmp";
		File.WriteAllText(temp, text, new UTF8Encoding(false));
		File.Move(temp, path, overwrite: true);
	}
}
