using System;
using System.Collections.Generic;
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
	public void Forget(string file) => _images.Remove(file);

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
