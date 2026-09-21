using System.Collections.Generic;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The catalogue's sprites as textures. The PNGs are read straight off the disk rather
/// than through the importer, so a sprite dropped into the folder a second ago draws at
/// once, and a shell run needs no import pass first.
/// </summary>
internal sealed class SpriteBank
{
	private readonly EconomyStore _store;
	private readonly Catalogue _catalogue;
	private readonly Dictionary<string, ImageTexture?> _sheets = new();
	private readonly Dictionary<(string, int), Texture2D> _cells = new();
	private readonly Dictionary<string, Texture2D?> _files = new();

	public SpriteBank(EconomyStore store, Catalogue catalogue)
	{
		_store = store;
		_catalogue = catalogue;
	}

	public Texture2D? Get(SpriteRef? sprite)
	{
		if (sprite == null) return null;
		if (!string.IsNullOrEmpty(sprite.File))
		{
			if (!_files.TryGetValue(sprite.File, out Texture2D? own)) _files[sprite.File] = own = Load(sprite.File);
			return own;
		}
		if (sprite.Atlas == null || sprite.Index == null) return null;
		return Cell(sprite.Atlas, sprite.Index.Value);
	}

	public Texture2D? Cell(string atlasId, int index)
	{
		if (_cells.TryGetValue((atlasId, index), out Texture2D? cell)) return cell;
		AtlasDef? atlas = _catalogue.Atlas(atlasId);
		ImageTexture? sheet = Sheet(atlasId);
		if (atlas == null || sheet == null || index < 0 || index >= CellCount(atlasId)) return null;
		var texture = new AtlasTexture
		{
			Atlas = sheet,
			Region = new Rect2(index % atlas.Columns * atlas.Cell, index / atlas.Columns * atlas.Cell, atlas.Cell, atlas.Cell),
			FilterClip = true,
		};
		return _cells[(atlasId, index)] = texture;
	}

	/// <summary>How many cells the sheet has room for, drawn or blank.</summary>
	public int CellCount(string atlasId)
	{
		AtlasDef? atlas = _catalogue.Atlas(atlasId);
		ImageTexture? sheet = Sheet(atlasId);
		if (atlas == null || sheet == null || atlas.Cell <= 0) return 0;
		return atlas.Columns * (sheet.GetHeight() / atlas.Cell);
	}

	/// <summary>Drops a file's cached texture, after the file has been replaced.</summary>
	public void Forget(string file) => _files.Remove(file);

	private ImageTexture? Sheet(string atlasId)
	{
		if (_sheets.TryGetValue(atlasId, out ImageTexture? sheet)) return sheet;
		AtlasDef? atlas = _catalogue.Atlas(atlasId);
		return _sheets[atlasId] = atlas == null ? null : Load(atlas.File);
	}

	private ImageTexture? Load(string relative)
	{
		string path = _store.Resolve(relative);
		if (!System.IO.File.Exists(path))
		{
			GD.PushWarning($"Economy lab: no sprite at {path}");
			return null;
		}
		Image image = Image.LoadFromFile(path);
		return image == null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
	}
}
