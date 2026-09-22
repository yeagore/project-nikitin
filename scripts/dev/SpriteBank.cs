using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;
using Economy = ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The sprites as textures. Which sheet an atlas id means is asked of the open web's palette
/// each time, since every web names its own; the PNGs themselves are shared files, read once
/// and kept by path. They are read straight off the disk rather than through the importer,
/// so a sprite dropped into the folder a second ago draws at once, and a shell run needs no
/// import pass first.
/// </summary>
internal sealed class SpriteBank
{
	private readonly EconomyStore _store;
	private readonly Func<Palette> _palette;
	private readonly Dictionary<string, ImageTexture?> _images = new();
	private readonly Dictionary<(string File, int Cell, int Index), Texture2D> _cells = new();
	private readonly Dictionary<string, Godot.Image?> _pixels = new();
	private readonly Dictionary<string, Texture2D> _composed = new();

	public SpriteBank(EconomyStore store, Func<Palette> palette)
	{
		_store = store;
		_palette = palette;
	}

	public Texture2D? Get(SpriteRef? sprite)
	{
		if (sprite == null) return null;
		if (!string.IsNullOrEmpty(sprite.File)) return Image(sprite.File);
		if (sprite.Atlas == null || sprite.Index == null) return null;
		return Cell(sprite.Atlas, sprite.Index.Value);
	}

	public Texture2D? Cell(string atlasId, int index)
	{
		AtlasDef? atlas = _palette().Atlas(atlasId);
		if (atlas == null || index < 0 || index >= CellCount(atlasId)) return null;
		if (_cells.TryGetValue((atlas.File, atlas.Cell, index), out Texture2D? cell)) return cell;
		var texture = new AtlasTexture
		{
			Atlas = Image(atlas.File),
			Region = new Rect2(index % atlas.Columns * atlas.Cell, index / atlas.Columns * atlas.Cell, atlas.Cell, atlas.Cell),
			FilterClip = true,
		};
		return _cells[(atlas.File, atlas.Cell, index)] = texture;
	}

	/// <summary>An element's icon off the elements sheet, which belongs to the system and not to any palette.</summary>
	public Texture2D? Element(string? id)
	{
		Element? element = Economy.Element.Find(id);
		if (element == null) return null;
		if (_cells.TryGetValue((Economy.Element.Sheet, 16, element.Cell), out Texture2D? cell)) return cell;
		ImageTexture? sheet = Image(Economy.Element.Sheet);
		if (sheet == null) return null;
		return _cells[(Economy.Element.Sheet, 16, element.Cell)] = new AtlasTexture { Atlas = sheet, Region = new Rect2(element.Cell * 16, 0, 16, 16), FilterClip = true };
	}

	/// <summary>How many cells the sheet has room for, drawn or blank.</summary>
	public int CellCount(string atlasId)
	{
		AtlasDef? atlas = _palette().Atlas(atlasId);
		ImageTexture? sheet = atlas == null ? null : Image(atlas.File);
		if (atlas == null || sheet == null || atlas.Cell <= 0) return 0;
		return atlas.Columns * (sheet.GetHeight() / atlas.Cell);
	}

	/// <summary>Drops a file's cached texture, after the file has been replaced.</summary>
	public void Forget(string file)
	{
		_images.Remove(file);
		_pixels.Remove(file);
		_composed.Clear();
	}

	// ---- varieties on an icon ----------------------------------------------------

	/// <summary>The most tags an icon shows as pips down its right edge.</summary>
	public const int MaxPips = 4;

	/// <summary>
	/// A good's icon as a unit carrying <paramref name="tags"/> shows it: the shape is the good's,
	/// the hue the variety's. Each of the good's <see cref="Good.Layers"/> tints its mask (or the
	/// whole icon) with the colour of the tag it answers to; a good with no layers gives the whole
	/// icon to its first coloured variety tag. Every other coloured tag, variety or property, is a
	/// pip on the right edge. No tags, the plain icon: a stack not split by a namespace simply
	/// leaves that namespace's tags out. Null when the good has no icon.
	/// </summary>
	public Texture2D? Compose(Good good, IEnumerable<string> tags)
	{
		Palette palette = _palette();
		List<string> coloured = tags.Where(t => TryColour(palette.ColourOf(t), out _)).ToList();
		if (coloured.Count == 0) return Get(good.Icon);
		Godot.Image? icon = Pixels(good.Icon);
		if (icon == null) return Get(good.Icon);

		var tints = new List<(Godot.Image? Mask, Color Hue, string Key)>();
		var pips = new List<Color>();
		var spent = new HashSet<string>(StringComparer.Ordinal);
		if (good.Layers != null)
			foreach (IconLayer layer in good.Layers)
			{
				string? tag = coloured.FirstOrDefault(t => !spent.Contains(t) && layer.Answers(t));
				if (tag == null) continue;
				Godot.Image? mask = layer.Mask == null ? null : Pixels(layer.Mask);
				if (layer.Mask != null && mask == null) continue; // a mask that does not load: the tag falls to a pip
				spent.Add(tag);
				TryColour(palette.ColourOf(tag), out Color hue);
				tints.Add((mask, hue, $"{RefKey(layer.Mask)}={hue.ToHtml(false)}"));
			}
		else
		{
			string first = coloured.FirstOrDefault(palette.IsVariety) ?? coloured[0];
			spent.Add(first);
			TryColour(palette.ColourOf(first), out Color hue);
			tints.Add((null, hue, "*=" + hue.ToHtml(false)));
		}
		foreach (string tag in coloured.Where(t => !spent.Contains(t)).Take(MaxPips))
		{
			TryColour(palette.ColourOf(tag), out Color hue);
			pips.Add(hue);
		}

		string key = RefKey(good.Icon) + "|" + string.Join(",", tints.Select(t => t.Key)) + "|" + string.Join(",", pips.Select(c => c.ToHtml(false)));
		if (_composed.TryGetValue(key, out Texture2D? known)) return known;

		var canvas = (Godot.Image)icon.Duplicate();
		canvas.Convert(Godot.Image.Format.Rgba8);
		int w = canvas.GetWidth(), h = canvas.GetHeight();
		foreach ((Godot.Image? mask, Color hue, _) in tints)
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					if (mask != null && (x >= mask.GetWidth() || y >= mask.GetHeight() || mask.GetPixel(x, y).A < 0.5f)) continue;
					Color px = canvas.GetPixel(x, y);
					if (px.A > 0.05f) canvas.SetPixel(x, y, Tinted(px, hue));
				}

		var outline = new Color(0.08f, 0.08f, 0.1f);
		for (int i = 0; i < pips.Count; i++)
		{
			int left = w - 4, top = i * 4;
			for (int y = 0; y < 4; y++)
				for (int x = 0; x < 4; x++)
				{
					bool edge = x == 0 || y == 0 || x == 3 || y == 3;
					if (left + x < w && top + y < h) canvas.SetPixel(left + x, top + y, edge ? outline : pips[i]);
				}
		}
		return _composed[key] = ImageTexture.CreateFromImage(canvas);
	}

	/// <summary>Reads a tag's <c>#RRGGBB</c>; false, and white, for none or for nonsense.</summary>
	public static bool TryColour(string? html, out Color colour)
	{
		colour = Colors.White;
		if (string.IsNullOrEmpty(html) || !Color.HtmlIsValid(html)) return false;
		colour = Color.FromHtml(html);
		return true;
	}

	/// <summary>The pixel's shading kept, its colour swapped for the hue; the dark outline is left alone.</summary>
	private static Color Tinted(Color px, Color hue)
	{
		float light = px.Luminance;
		if (light < 0.14f) return px;
		Color c = light < 0.5f ? Colors.Black.Lerp(hue, 0.25f + 0.75f * light / 0.5f) : hue.Lerp(Colors.White, (light - 0.5f) / 0.5f * 0.6f);
		c.A = px.A;
		return c;
	}

	private static string RefKey(SpriteRef? sprite) => sprite == null ? "-" : !string.IsNullOrEmpty(sprite.File) ? sprite.File : $"{sprite.Atlas}#{sprite.Index}";

	/// <summary>A sprite's own pixels, cut out of its sheet: what composing works on.</summary>
	private Godot.Image? Pixels(SpriteRef? sprite)
	{
		if (sprite == null) return null;
		if (!string.IsNullOrEmpty(sprite.File)) return Sheet(sprite.File);
		AtlasDef? atlas = sprite.Atlas == null ? null : _palette().Atlas(sprite.Atlas);
		if (atlas == null || sprite.Index == null || sprite.Index < 0 || sprite.Index >= CellCount(atlas.Id)) return null;
		Godot.Image? sheet = Sheet(atlas.File);
		if (sheet == null) return null;
		int i = sprite.Index.Value;
		return sheet.GetRegion(new Rect2I(i % atlas.Columns * atlas.Cell, i / atlas.Columns * atlas.Cell, atlas.Cell, atlas.Cell));
	}

	private Godot.Image? Sheet(string relative)
	{
		if (_pixels.TryGetValue(relative, out Godot.Image? known)) return known;
		string path = _store.Resolve(relative);
		Godot.Image? image = System.IO.File.Exists(path) ? Godot.Image.LoadFromFile(path) : null;
		if (image != null && image.IsEmpty()) image = null;
		image?.Convert(Godot.Image.Format.Rgba8);
		return _pixels[relative] = image;
	}

	private ImageTexture? Image(string relative)
	{
		if (_images.TryGetValue(relative, out ImageTexture? known)) return known;
		string path = _store.Resolve(relative);
		if (!System.IO.File.Exists(path))
		{
			GD.PushWarning($"Economy lab: no sprite at {path}");
			return _images[relative] = null;
		}
		Godot.Image image = Godot.Image.LoadFromFile(path);
		return _images[relative] = image == null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
	}
}
