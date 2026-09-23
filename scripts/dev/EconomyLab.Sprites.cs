using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// <c>-- sprites</c>: a headless measuring pass over one web's icons and the varieties the lab
/// composes from them. Puts every icon of the web on contact sheets, and every good that varies
/// with up to eleven of its composed varieties and its masks, plus the masks and tags atlases for
/// reference; then prints what is wrong with them: missing or blank icons, two goods that read as
/// the same shape, icons too thin or faint to read, masks that cover nothing or swamp the icon,
/// varieties that compose indistinguishably close, tag colours too close within a namespace, and
/// variety tags that never reach the shape and fall back to a pip. Nothing here is drawn by hand
/// and nothing here is written under <c>resources/</c>: it reads a web and reports on the sprites
/// the palette already has, exactly as <see cref="SpriteBank"/> composes them for the lab.
/// </summary>
public partial class EconomyLab
{
	private const int SpriteCell = 16;
	private const int SpriteZoom = 4;
	private const int SpritePx = SpriteCell * SpriteZoom; // 64: every tile shows a sprite at four times its size.

	partial void SpriteReview(string[] args)
	{
		string id = args.FirstOrDefault(a => a.StartsWith("web=", StringComparison.Ordinal))?[4..] ?? "variety-ledger";
		string outDir = (args.FirstOrDefault(a => a.StartsWith("out=", StringComparison.Ordinal))?[4..]
			?? ProjectSettings.GlobalizePath("user://sprite_review")).TrimEnd('/', '\\');
		if (!Store.HasWeb(id))
		{
			GD.PrintErr($"Economy lab: no web \"{id}\".");
			return;
		}
		EconomyWeb web = Store.LoadWeb(id);
		var bank = new SpriteBank(Store, () => web.Palette);
		WebAnalysis analysis = WebAnalysis.Of(web);
		DirAccess.MakeDirRecursiveAbsolute(outDir);

		List<Good> goods = web.Goods.Select(gid => web.Palette.Find(gid)).Where(g => g != null).Select(g => g!).ToList();
		// A good on the canvas but missing from the palette is WebAnalysis's issue to flag, not this tool's; skipped quietly.
		List<(Good Good, VarietySet Set)> varying = goods
			.Select(g => (Good: g, Set: analysis.VarietiesOf(g.Id)))
			.Where(t => (t.Good.Layers?.Count ?? 0) > 0 || t.Good.VarietyList.Count > 0 || t.Set.Sets.Count > 1)
			.ToList();

		var written = new List<(string Path, int W, int H)>();
		written.AddRange(WriteIconPages(web, bank, goods, outDir));
		written.AddRange(WriteVarietyPages(web, bank, varying, outDir));
		written.Add(WriteReferenceSheet(web, bank, outDir));

		GD.Print($"Economy lab: sprite review of {web.Name} ({id}) written to {outDir}:");
		foreach ((string path, int w, int h) in written) GD.Print($"  {path} ({w}x{h})");
		GD.Print("");

		Report(web, bank, goods, varying);
	}

	// ---- the pages --------------------------------------------------------------

	/// <summary>Every good on the web's canvas, its plain icon at ×4, its name under it; 8×8 to a page.</summary>
	private static List<(string, int, int)> WriteIconPages(EconomyWeb web, SpriteBank bank, List<Good> goods, string outDir)
	{
		const int Cols = 8, Rows = 8, TileW = 80, TileH = 84, Pad = 16;
		int perPage = Cols * Rows;
		int pages = Math.Max(1, (goods.Count + perPage - 1) / perPage);
		int titleH = TinyFont.Height(2) + 10;
		int width = Pad * 2 + Cols * TileW;
		int height = Pad + titleH + Rows * TileH + Pad;

		var written = new List<(string, int, int)>();
		for (int p = 0; p < pages; p++)
		{
			var page = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			page.Fill(LabLook.Back);
			TinyFont.Draw(page, $"ICONS OF {web.Name}, PAGE {p + 1} OF {pages}", Pad, Pad, 2, LabLook.Ink);

			for (int i = 0; i < perPage; i++)
			{
				int gi = p * perPage + i;
				if (gi >= goods.Count) break;
				Good good = goods[gi];
				int col = i % Cols, row = i / Cols;
				DrawTile(page, bank.PixelsOf(good.Icon), good.Name, Pad + col * TileW, Pad + titleH + row * TileH, TileW, TileH);
			}
			string path = $"{outDir}/icons-{p + 1}.png";
			page.SavePng(path);
			written.Add((path, width, height));
		}
		return written;
	}

	/// <summary>
	/// One row per good that can vary: its plain icon, up to eleven of its composed varieties (the
	/// first eleven of <see cref="WebAnalysis.VarietiesOf"/>, in the same order that list is built in,
	/// so the same web reads the same page), then one tile per distinct mask its layers use. ~10 rows a page.
	/// </summary>
	private static List<(string, int, int)> WriteVarietyPages(EconomyWeb web, SpriteBank bank, List<(Good Good, VarietySet Set)> varying, string outDir)
	{
		var written = new List<(string, int, int)>();
		if (varying.Count == 0) return written;

		const int TileW = 72, TileH = 84, Gutter = 150, RowsPerPage = 10, Pad = 16, MaxVarietyTiles = 11;
		int maxCols = 1; // the plain icon, at least.
		foreach ((Good good, VarietySet set) in varying)
			maxCols = Math.Max(maxCols, 1 + Math.Min(MaxVarietyTiles, set.Sets.Count) + DistinctMasks(good).Count);

		int titleH = TinyFont.Height(2) + 10;
		int width = Pad + Gutter + maxCols * TileW + Pad;
		int pages = (varying.Count + RowsPerPage - 1) / RowsPerPage;
		int height = Pad + titleH + RowsPerPage * TileH + Pad;

		for (int p = 0; p < pages; p++)
		{
			var page = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			page.Fill(LabLook.Back);
			TinyFont.Draw(page, $"VARIETIES OF {web.Name}, PAGE {p + 1} OF {pages}", Pad, Pad, 2, LabLook.Ink);

			for (int r = 0; r < RowsPerPage; r++)
			{
				int gi = p * RowsPerPage + r;
				if (gi >= varying.Count) break;
				(Good good, VarietySet set) = varying[gi];
				int rowTop = Pad + titleH + r * TileH;
				TinyFont.Draw(page, Fit(good.Name, Gutter - 8, 2), Pad, rowTop + (TileH - TinyFont.Height(2)) / 2, 2, LabLook.Ink);

				int col = 0;
				Godot.Image? plain = bank.PixelsOf(good.Icon);
				DrawTile(page, plain, "plain", Pad + Gutter + col * TileW, rowTop, TileW, TileH);
				col++;

				int take = Math.Min(MaxVarietyTiles, set.Sets.Count);
				for (int v = 0; v < take; v++)
				{
					IReadOnlyList<string> tags = set.Sets[v];
					DrawTile(page, bank.ComposeImage(good, tags), LabLook.VarietyName(tags), Pad + Gutter + col * TileW, rowTop, TileW, TileH);
					col++;
				}

				foreach (IconLayer layer in DistinctMasks(good))
				{
					DrawMaskTile(page, plain, bank.PixelsOf(layer.Mask), Pad + Gutter + col * TileW, rowTop, TileW, TileH);
					col++;
				}
			}
			string path = $"{outDir}/varieties-{p + 1}.png";
			page.SavePng(path);
			written.Add((path, width, height));
		}
		return written;
	}

	/// <summary>The masks sheet and the tags sheet at ×4, each cell numbered, for reference.</summary>
	private static (string, int, int) WriteReferenceSheet(EconomyWeb web, SpriteBank bank, string outDir)
	{
		const int Pad = 16, Gap = 20;
		Image masks = DrawAtlasPanel(bank, web.Palette.Atlas("masks"), "MASKS");
		Image tags = DrawAtlasPanel(bank, web.Palette.Atlas("tags"), "TAGS");
		int titleH = TinyFont.Height(2) + 10;
		int width = Pad * 2 + Math.Max(masks.GetWidth(), tags.GetWidth());
		int height = Pad + titleH + masks.GetHeight() + Gap + tags.GetHeight() + Pad;

		var page = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		page.Fill(LabLook.Back);
		TinyFont.Draw(page, $"SPRITE SHEETS OF {web.Name}", Pad, Pad, 2, LabLook.Ink);
		int y = Pad + titleH;
		page.BlendRect(masks, new Rect2I(0, 0, masks.GetWidth(), masks.GetHeight()), new Vector2I(Pad, y));
		y += masks.GetHeight() + Gap;
		page.BlendRect(tags, new Rect2I(0, 0, tags.GetWidth(), tags.GetHeight()), new Vector2I(Pad, y));

		string path = $"{outDir}/sheets.png";
		page.SavePng(path);
		return (path, width, height);
	}

	/// <summary>One atlas, every cell at ×4 in its own grid, the cell's index in the corner.</summary>
	private static Image DrawAtlasPanel(SpriteBank bank, AtlasDef? atlas, string title)
	{
		int labelH = TinyFont.Height(2) + 6;
		if (atlas == null)
		{
			var missing = Image.CreateEmpty(Math.Max(200, TinyFont.Width($"{title}: NOT ON THIS PALETTE", 2)), labelH, false, Image.Format.Rgba8);
			missing.Fill(LabLook.Back);
			TinyFont.Draw(missing, $"{title}: NOT ON THIS PALETTE", 0, 0, 2, LabLook.Error);
			return missing;
		}

		int cellCount = bank.CellCount(atlas.Id);
		int cols = Math.Max(1, atlas.Columns);
		int rows = Math.Max(1, (cellCount + cols - 1) / cols);
		int cellPx = atlas.Cell * SpriteZoom;
		var panel = Image.CreateEmpty(cols * cellPx, labelH + rows * cellPx, false, Image.Format.Rgba8);
		panel.Fill(LabLook.Back);
		TinyFont.Draw(panel, title, 0, 0, 2, LabLook.Ink);

		for (int i = 0; i < cellCount; i++)
		{
			int tx = i % cols * cellPx, ty = labelH + i / cols * cellPx;
			Fill(panel, tx, ty, cellPx, cellPx, LabLook.Body);
			Godot.Image? cell = bank.PixelsOf(SpriteRef.Cell(atlas.Id, i));
			if (cell != null)
			{
				var big = (Image)cell.Duplicate();
				big.Convert(Image.Format.Rgba8);
				big.Resize(cellPx, cellPx, Image.Interpolation.Nearest);
				panel.BlendRect(big, new Rect2I(0, 0, cellPx, cellPx), new Vector2I(tx, ty));
			}
			Frame(panel, tx, ty, cellPx, cellPx, LabLook.Edge);
			TinyFont.Draw(panel, i.ToString(), tx + 2, ty + 2, 1, LabLook.Accent);
		}
		return panel;
	}

	// ---- drawing a tile -----------------------------------------------------------

	/// <summary>A tile on the node's own background: the sprite centred at ×4, a short label under it. Null draws a marked-out box.</summary>
	private static void DrawTile(Image page, Godot.Image? sprite, string label, int tx, int ty, int tileW, int tileH)
	{
		Fill(page, tx, ty, tileW, tileH, LabLook.Body);
		Frame(page, tx, ty, tileW, tileH, LabLook.Edge);
		int ix = tx + (tileW - SpritePx) / 2, iy = ty + 4;
		if (sprite == null)
		{
			Frame(page, ix, iy, SpritePx, SpritePx, LabLook.Error);
			TinyFont.DrawCentered(page, "NONE", tx + tileW / 2, iy + SpritePx / 2 - TinyFont.Height(1) / 2, 1, LabLook.Error);
		}
		else
		{
			var big = (Image)sprite.Duplicate(); // PixelsOf/ComposeImage may hand back a shared image: never draw on it directly.
			big.Convert(Image.Format.Rgba8);
			big.Resize(SpritePx, SpritePx, Image.Interpolation.Nearest);
			page.BlendRect(big, new Rect2I(0, 0, SpritePx, SpritePx), new Vector2I(ix, iy));
		}
		TinyFont.DrawCentered(page, Fit(label, tileW - 8, 1), tx + tileW / 2, iy + SpritePx + 4, 1, LabLook.Ink);
	}

	/// <summary>A tile of the plain icon with a mask's opaque pixels painted magenta over it, labelled "mask".</summary>
	private static void DrawMaskTile(Image page, Godot.Image? icon, Godot.Image? mask, int tx, int ty, int tileW, int tileH)
	{
		Fill(page, tx, ty, tileW, tileH, LabLook.Body);
		Frame(page, tx, ty, tileW, tileH, LabLook.Edge);
		int ix = tx + (tileW - SpritePx) / 2, iy = ty + 4;
		if (icon != null)
		{
			var big = (Image)icon.Duplicate();
			big.Convert(Image.Format.Rgba8);
			big.Resize(SpritePx, SpritePx, Image.Interpolation.Nearest);
			page.BlendRect(big, new Rect2I(0, 0, SpritePx, SpritePx), new Vector2I(ix, iy));
		}
		if (mask != null)
			for (int y = 0; y < SpriteCell; y++)
			for (int x = 0; x < SpriteCell; x++)
			{
				if (x >= mask.GetWidth() || y >= mask.GetHeight() || mask.GetPixel(x, y).A < 0.5f) continue;
				Fill(page, ix + x * SpriteZoom, iy + y * SpriteZoom, SpriteZoom, SpriteZoom, Colors.Magenta);
			}
		TinyFont.DrawCentered(page, "MASK", tx + tileW / 2, iy + SpritePx + 4, 1, LabLook.Ink);
	}

	/// <summary>Cuts <paramref name="text"/> down until it is no wider than <paramref name="maxWidth"/>; no ellipsis, TinyFont has none to spare.</summary>
	private static string Fit(string text, int maxWidth, int scale)
	{
		if (TinyFont.Width(text, scale) <= maxWidth) return text;
		for (int n = text.Length - 1; n > 0; n--)
		{
			string cut = text[..n];
			if (TinyFont.Width(cut, scale) <= maxWidth) return cut;
		}
		return "";
	}

	private static void Fill(Image img, int x, int y, int w, int h, Color c)
	{
		for (int i = 0; i < w; i++)
		for (int j = 0; j < h; j++)
		{
			int px = x + i, py = y + j;
			if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight()) continue;
			img.SetPixel(px, py, c);
		}
	}

	private static void Frame(Image img, int x, int y, int w, int h, Color c)
	{
		Fill(img, x, y, w, 1, c);
		Fill(img, x, y + h - 1, w, 1, c);
		Fill(img, x, y, 1, h, c);
		Fill(img, x + w - 1, y, 1, h, c);
	}

	/// <summary>The layers of a good that carry a mask, each distinct mask once, in layer order.</summary>
	private static List<IconLayer> DistinctMasks(Good good)
	{
		var found = new List<IconLayer>();
		if (good.Layers == null) return found;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (IconLayer layer in good.Layers)
			if (layer.Mask != null && seen.Add(MaskKey(layer.Mask)))
				found.Add(layer);
		return found;
	}

	private static string MaskKey(SpriteRef mask) => !string.IsNullOrEmpty(mask.File) ? mask.File : $"{mask.Atlas}#{mask.Index}";

	// ---- the report ---------------------------------------------------------------

	/// <summary>The seven measures, a. through g., each printed as its own section, then one line of counts.</summary>
	private static void Report(EconomyWeb web, SpriteBank bank, List<Good> goods, List<(Good Good, VarietySet Set)> varying)
	{
		GD.Print($"SPRITE REVIEW of {web.Name} ({web.Id}): {goods.Count} goods on the canvas, {varying.Count} of them vary.");
		GD.Print("");

		(List<string> missing, List<string> thin) = CollectIconFindings(bank, goods);
		PrintSection('a', "Missing or blank icons", missing);
		List<string> shared = CollectSharedShapes(bank, goods);
		PrintSection('b', "Shared shapes", shared);
		PrintSection('c', "Thin or faint icons", thin);
		(List<string> maskLines, int broken, int small, int swamp, int total) = CollectMaskCoverage(bank, goods);
		PrintSection('d', "Masks that miss", maskLines);
		(List<string> varietyLines, int flaggedPairs) = CollectVarietyLikeness(bank, varying);
		PrintSection('e', "Varieties that look alike", varietyLines);
		(List<string> colourLines, int closePairs, int uncoloured) = CollectTagColours(web.Palette);
		PrintSection('f', "Tag colours too close", colourLines);
		(List<string> pipLines, int pipGoods) = CollectPipOnly(web.Palette, bank, varying);
		PrintSection('g', "Pip-only varieties", pipLines);

		GD.Print($"SUMMARY: a {missing.Count} missing/blank, b {shared.Count} shared-shape groups, c {thin.Count} thin/faint, "
			+ $"d {broken} broken masks/{small} too small/{swamp} swamping of {total}, "
			+ $"e {flaggedPairs} indistinguishable pairs across {varying.Count} varying goods, "
			+ $"f {closePairs} close tag-colour pairs/{uncoloured} uncoloured variety tags, "
			+ $"g {pipGoods} goods with a pip-only variety.");
	}

	private static void PrintSection(char letter, string title, IReadOnlyList<string> lines)
	{
		GD.Print($"{letter}. {title} ({lines.Count}):");
		if (lines.Count == 0) GD.Print("  none.");
		foreach (string line in lines) GD.Print("  " + line);
		GD.Print("");
	}

	/// <summary>a and c share one pass over every icon: missing/blank, and thin/faint against the tile background.</summary>
	private static (List<string> Missing, List<string> Thin) CollectIconFindings(SpriteBank bank, List<Good> goods)
	{
		var missing = new List<string>();
		var thin = new List<string>();
		foreach (Good good in goods)
		{
			Godot.Image? icon = bank.PixelsOf(good.Icon);
			if (icon == null)
			{
				missing.Add($"{good.Name}: no icon reads (a null reference, a cell out of range, or the sheet does not load).");
				continue;
			}
			(int opaque, int differ) = IconStats(icon, LabLook.Body);
			if (opaque == 0)
			{
				missing.Add($"{good.Name}: not one pixel above 5% alpha.");
				continue;
			}
			double frac = differ / (double)opaque;
			bool wasThin = opaque < 24, wasFaint = frac < 0.40;
			if (!wasThin && !wasFaint) continue;
			string what = wasThin && wasFaint ? "thin and faint" : wasThin ? "thin" : "faint";
			thin.Add($"{good.Name}: {opaque} opaque px, {WebBalance.Pct(frac)} of them read against the tile ({what}).");
		}
		return (missing, thin);
	}

	private static (int Opaque, int Differ) IconStats(Image icon, Color background)
	{
		int opaque = 0, differ = 0;
		float bgLuminance = background.Luminance;
		int w = icon.GetWidth(), h = icon.GetHeight();
		for (int y = 0; y < h; y++)
		for (int x = 0; x < w; x++)
		{
			Color px = icon.GetPixel(x, y);
			if (px.A <= 0.05f) continue;
			opaque++;
			if (Math.Abs(px.Luminance - bgLuminance) >= 0.12f) differ++;
		}
		return (opaque, differ);
	}

	/// <summary>b: goods whose plain icon is byte-for-byte the same picture, whatever sprite it is a reference to.</summary>
	private static List<string> CollectSharedShapes(SpriteBank bank, List<Good> goods)
	{
		var byPixels = new Dictionary<string, List<(string Id, string Name)>>(StringComparer.Ordinal);
		foreach (Good good in goods)
		{
			Godot.Image? icon = bank.PixelsOf(good.Icon);
			if (icon == null) continue;
			string key = Convert.ToBase64String(icon.GetData());
			if (!byPixels.TryGetValue(key, out List<(string, string)>? list)) byPixels[key] = list = new List<(string, string)>();
			list.Add((good.Id, good.Name));
		}
		List<List<(string Id, string Name)>> groups = byPixels.Values.Where(l => l.Count > 1).ToList();
		groups.Sort((a, b) => string.CompareOrdinal(a[0].Id, b[0].Id));
		return groups.Select(g => string.Join(", ", g.Select(x => x.Name)) + ": identical icon pixels.").ToList();
	}

	/// <summary>d: for every layer with a mask, how much of the icon's tintable area it actually covers.</summary>
	private static (List<string> Lines, int Broken, int Small, int Swamp, int Total) CollectMaskCoverage(SpriteBank bank, List<Good> goods)
	{
		var lines = new List<string>();
		int broken = 0, small = 0, swamp = 0, total = 0;
		foreach (Good good in goods)
		{
			if (good.Layers == null || good.Layers.Count == 0) continue;
			Godot.Image? icon = bank.PixelsOf(good.Icon);
			if (icon == null) continue; // already reported under (a).
			int w = icon.GetWidth(), h = icon.GetHeight();
			var tintable = new bool[w, h];
			int tintableCount = 0;
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color px = icon.GetPixel(x, y);
				bool ok = px.A > 0.05f && px.Luminance >= 0.14f; // the threshold SpriteBank.Tinted uses to leave dark ink alone.
				tintable[x, y] = ok;
				if (ok) tintableCount++;
			}

			foreach (IconLayer layer in good.Layers)
			{
				if (layer.Mask == null) continue;
				total++;
				Godot.Image? mask = bank.PixelsOf(layer.Mask);
				int covered = 0;
				if (mask != null)
					for (int y = 0; y < h; y++)
					for (int x = 0; x < w; x++)
					{
						if (!tintable[x, y]) continue;
						if (x < mask.GetWidth() && y < mask.GetHeight() && mask.GetPixel(x, y).A >= 0.5f) covered++;
					}
				double frac = tintableCount > 0 ? (double)covered / tintableCount : 0;
				string flag = mask == null ? " BROKEN: the mask sprite does not load."
					: covered == 0 ? " BROKEN: covers nothing."
					: frac < 0.08 ? " too small to read."
					: frac > 0.90 ? " swamps the shape."
					: ".";
				if (mask == null || covered == 0) broken++;
				else if (frac < 0.08) small++;
				else if (frac > 0.90) swamp++;
				lines.Add($"{good.Name} {LabLook.Short(layer.Match)}: {covered}/{tintableCount} tintable px ({WebBalance.Pct(frac)}){flag}");
			}
		}
		return (lines, broken, small, swamp, total);
	}

	/// <summary>e: up to 64 composed varieties a good, every pair's mean colour distance, flagged under 0.035.</summary>
	private static (List<string> Lines, int FlaggedPairs) CollectVarietyLikeness(SpriteBank bank, List<(Good Good, VarietySet Set)> varying)
	{
		const int SampleCap = 64;
		const double Threshold = 0.035;
		var lines = new List<string>();
		int totalFlagged = 0;

		foreach ((Good good, VarietySet set) in varying)
		{
			List<IReadOnlyList<string>> sample = set.Sets.Take(SampleCap).ToList();
			var images = new List<Godot.Image>();
			var labelOf = new List<IReadOnlyList<string>>();
			foreach (IReadOnlyList<string> tags in sample)
			{
				Godot.Image? composed = bank.ComposeImage(good, tags);
				if (composed == null) continue; // no icon at all: already reported under (a).
				images.Add(composed); // read-only from here: never drawn on.
				labelOf.Add(tags);
			}

			var distinct = new HashSet<string>(StringComparer.Ordinal);
			foreach (Godot.Image img in images) distinct.Add(Convert.ToBase64String(img.GetData()));

			int flagged = 0;
			double worst = double.MaxValue;
			int worstI = -1, worstJ = -1;
			for (int i = 0; i < images.Count; i++)
			for (int j = i + 1; j < images.Count; j++)
			{
				double dist = MeanColourDistance(images[i], images[j]);
				if (dist < worst) { worst = dist; worstI = i; worstJ = j; }
				if (dist < Threshold) flagged++;
			}
			totalFlagged += flagged;

			string countLabel = (set.Capped ? "~" : "") + set.Count.ToString("N0");
			string worstLabel = worstI >= 0
				? $"worst {LabLook.VarietyName(labelOf[worstI])} vs {LabLook.VarietyName(labelOf[worstJ])} at {Num3(worst)}"
				: "only one composed image";
			lines.Add($"{good.Name}: {images.Count} composed of {countLabel} varieties, {distinct.Count} distinct images, "
				+ $"{flagged} pairs under {Num3(Threshold)}; {worstLabel}.");
		}
		return (lines, totalFlagged);
	}

	private static double MeanColourDistance(Godot.Image a, Godot.Image b)
	{
		int w = a.GetWidth(), h = a.GetHeight();
		double sum = 0;
		int n = 0;
		for (int y = 0; y < h; y++)
		for (int x = 0; x < w; x++)
		{
			Color pa = a.GetPixel(x, y), pb = b.GetPixel(x, y);
			if (pa.A <= 0.05f && pb.A <= 0.05f) continue;
			double dr = pa.R - pb.R, dg = pa.G - pb.G, db = pa.B - pb.B;
			sum += Math.Sqrt(dr * dr + dg * dg + db * db);
			n++;
		}
		return n > 0 ? sum / n : 0;
	}

	/// <summary>f: within every variety or property namespace, tag colours closer than CIE76 ΔE 12; variety tags with no colour at all.</summary>
	private static (List<string> Lines, int ClosePairs, int Uncoloured) CollectTagColours(Palette palette)
	{
		var byNamespace = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
		foreach ((string tag, int _) in palette.TagsInUse())
		{
			TagNamespace? def = palette.Namespace(Palette.NamespaceOf(tag));
			if (def == null || (def.Role != TagNamespace.Variety && def.Role != TagNamespace.Property)) continue;
			string ns = def.Id;
			if (!byNamespace.TryGetValue(ns, out List<string>? list)) byNamespace[ns] = list = new List<string>();
			list.Add(tag);
		}

		var lines = new List<string>();
		var uncoloured = new List<string>();
		int closePairs = 0;
		foreach ((string ns, List<string> tags) in byNamespace)
		{
			TagNamespace nsDef = palette.Namespace(ns)!;
			if (nsDef.Role == TagNamespace.Variety)
				foreach (string tag in tags)
					if (!SpriteBank.TryColour(palette.ColourOf(tag), out _)) uncoloured.Add(tag);

			var coloured = new List<(string Tag, Color Colour)>();
			foreach (string tag in tags)
				if (SpriteBank.TryColour(palette.ColourOf(tag), out Color colour)) coloured.Add((tag, colour));

			for (int i = 0; i < coloured.Count; i++)
			for (int j = i + 1; j < coloured.Count; j++)
			{
				double de = DeltaE76(coloured[i].Colour, coloured[j].Colour);
				if (de >= 12) continue;
				closePairs++;
				lines.Add($"{coloured[i].Tag} ({palette.ColourOf(coloured[i].Tag)}) and {coloured[j].Tag} ({palette.ColourOf(coloured[j].Tag)}): dE {Num1(de)}.");
			}
		}
		if (uncoloured.Count > 0) lines.Add("No colour at all, so cannot show on an icon: " + string.Join(", ", uncoloured) + ".");
		return (lines, closePairs, uncoloured.Count);
	}

	private static double DeltaE76(Color a, Color b)
	{
		(double l1, double a1, double b1) = ToLab(a);
		(double l2, double a2, double b2) = ToLab(b);
		double dl = l1 - l2, da = a1 - a2, db = b1 - b2;
		return Math.Sqrt(dl * dl + da * da + db * db);
	}

	/// <summary>sRGB (0..1, gamma-encoded) to CIE Lab under the D65 illuminant, by way of linear RGB and XYZ.</summary>
	private static (double L, double A, double B) ToLab(Color c)
	{
		double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
		double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
		double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
		double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
		double fx = LabF(x), fy = LabF(y), fz = LabF(z);
		return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
	}

	private static double Linear(float c) => c <= 0.04045f ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

	private static double LabF(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : 841.0 / 108.0 * t + 4.0 / 29.0;

	/// <summary>g: variety tags whose colour never reaches the shape (no layer answers it, or it is not the good's one tinted tag) and so shows only as a pip.</summary>
	private static (List<string> Lines, int Goods) CollectPipOnly(Palette palette, SpriteBank bank, List<(Good Good, VarietySet Set)> varying)
	{
		const int SampleCap = 64;
		var lines = new List<string>();
		int affectedGoods = 0;
		foreach ((Good good, VarietySet set) in varying)
		{
			List<IReadOnlyList<string>> sample = set.Sets.Take(SampleCap).ToList();
			var pipTags = new SortedSet<string>(StringComparer.Ordinal);
			int affectedSamples = 0;
			foreach (IReadOnlyList<string> tags in sample)
			{
				List<string> pips = PipOnlyTags(good, tags, palette, bank);
				if (pips.Count == 0) continue;
				affectedSamples++;
				foreach (string tag in pips) pipTags.Add(tag);
			}
			if (pipTags.Count == 0) continue;
			affectedGoods++;
			lines.Add($"{good.Name}: {affectedSamples} of {sample.Count} sampled varieties show a pip-only tag: "
				+ string.Join(", ", pipTags.Select(LabLook.Short)) + ".");
		}
		return (lines, affectedGoods);
	}

	/// <summary>Mirrors SpriteBank.Composed's own spending of coloured tags onto layers (or the whole icon), tag-only and pixel-free.</summary>
	private static List<string> PipOnlyTags(Good good, IReadOnlyList<string> tags, Palette palette, SpriteBank bank)
	{
		var coloured = new List<string>();
		foreach (string tag in tags)
			if (SpriteBank.TryColour(palette.ColourOf(tag), out _)) coloured.Add(tag);

		var spent = new HashSet<string>(StringComparer.Ordinal);
		if (good.Layers is { Count: > 0 })
		{
			foreach (IconLayer layer in good.Layers)
			{
				string? tag = coloured.FirstOrDefault(t => !spent.Contains(t) && layer.Answers(t));
				if (tag == null) continue;
				if (layer.Mask != null && bank.PixelsOf(layer.Mask) == null) continue; // a mask that fails to load falls to a pip too.
				spent.Add(tag);
			}
		}
		else
		{
			string? first = coloured.FirstOrDefault(palette.IsVariety) ?? coloured.FirstOrDefault();
			if (first != null) spent.Add(first);
		}
		return coloured.Where(t => !spent.Contains(t)).ToList();
	}

	private static string Num3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

	private static string Num1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
