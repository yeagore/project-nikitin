using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Prints one hash per generated island over a broad matrix of parameters, and a
/// hash of the whole run. Two runs that print the same lines built the same
/// islands bit for bit — the regression check for any change that is meant to
/// leave generation untouched.
/// </summary>
public partial class GenerationChecksum : Node
{
    [Export] public IslandParams Params { get; set; } = null!;
    [Export] public int Seeds { get; set; } = 60;
    [Export] public int FirstSeed { get; set; } = 5000;

    /// <summary>Overwrite <see cref="BaselinePath"/> with this run instead of diffing against it.</summary>
    [Export] public bool AcceptBaseline { get; set; } = false;

    private const string BaselinePath = "res://docs/checksum-baseline.txt";

    private ulong _total = Fnv.Offset;
    private int _islands;
    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        Params ??= new IslandParams();
        // `godot --headless scenes/dev/generation_checksum.tscn -- accept` from a shell.
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "accept") >= 0) AcceptBaseline = true;
        ulong t0 = Time.GetTicksMsec();

        for (int i = 0; i < Seeds; i++) Case("default", Seed(i), Params);

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter c in Enum.GetValues<TerrainCharacter>())
            {
                if (c == TerrainCharacter.Auto) continue;
                Case($"{how}x{c}", Seed(0), At(64, p => { p.Arrangement = how; p.Character = c; }));
            }
        }

        foreach (int size in IslandParams.SupportedSizes)
            for (int i = 0; i < 3; i++) Case($"size{size}", Seed(i), At(size));

        foreach (GateKind kind in Enum.GetValues<GateKind>())
        foreach (GateEdge edge in Enum.GetValues<GateEdge>())
            Case($"entry{kind}{edge}", Seed(1), At(64, p => { p.EntryGate = kind; p.EntryEdge = edge; }));
        foreach (GateKind kind in Enum.GetValues<GateKind>())
        for (int exits = 0; exits <= 3; exits++)
            Case($"exits{exits}{kind}", Seed(2), At(64, p => { p.ExitGate = kind; p.ExitGates = exits; }));
        foreach (BridgeEase ease in Enum.GetValues<BridgeEase>())
            for (int i = 0; i < 2; i++) Case($"crossings{ease}", Seed(i), At(64, p => p.Crossings = ease));

        var floats = new (string Name, Action<IslandParams, float> Set)[]
        {
            ("Lakes", (p, v) => p.Lakes = v), ("Rivers", (p, v) => p.Rivers = v),
            ("Valleys", (p, v) => p.Valleys = v), ("Hilliness", (p, v) => p.Hilliness = v),
            ("Relief", (p, v) => p.Relief = v), ("LandformMix", (p, v) => p.LandformMix = v),
            ("Coverage", (p, v) => p.Coverage = v), ("Irregularity", (p, v) => p.Irregularity = v),
            ("KeelRoughness", (p, v) => p.KeelRoughness = v), ("OverhangDensity", (p, v) => p.OverhangDensity = v),
            ("Radius", (p, v) => p.Radius = v * 24f),
        };
        foreach (var (name, set) in floats)
            foreach (float v in new[] { 0f, 1f })
                for (int i = 0; i < 2; i++) Case($"{name}{v}", Seed(i + 3), At(64, p => set(p, v)));

        var ints = new (string Name, int Lo, int Hi, Action<IslandParams, int> Set)[]
        {
            ("PlateauLevels", 1, 4, (p, v) => p.PlateauLevels = v), ("CliffHeight", 2, 6, (p, v) => p.CliffHeight = v),
            ("RegionScale", 8, 24, (p, v) => p.RegionScale = v), ("MountainHeight", 20, 60, (p, v) => p.MountainHeight = v),
            ("MesaHeight", 3, 8, (p, v) => p.MesaHeight = v), ("BasinDepth", 3, 8, (p, v) => p.BasinDepth = v),
            ("EdgeThickness", 1, 5, (p, v) => p.EdgeThickness = v), ("KeelDepth", 10, 50, (p, v) => p.KeelDepth = v),
            ("OverhangDepth", 1, 3, (p, v) => p.OverhangDepth = v), ("ArchSpan", 2, 6, (p, v) => p.ArchSpan = v),
        };
        foreach (var (name, lo, hi, set) in ints)
            foreach (int v in new[] { lo, hi })
                for (int i = 0; i < 2; i++) Case($"{name}{v}", Seed(i + 5), At(64, p => set(p, v)));

        for (int i = 0; i < 4; i++) Case("oldArrangements", Seed(i + 7), At(96, p => p.NewArrangements = false));
        for (int i = 0; i < 4; i++) Case("oldLandforms", Seed(i + 7), At(96, p => p.NewLandforms = false));

        GD.Print($"checksum: {_islands} islands, {_total:x16}, {Time.GetTicksMsec() - t0} ms");
        Baseline();
        GetTree().Quit();
    }

    /// <summary>
    /// Diffs this run against the accepted baseline, island by island, or
    /// overwrites the baseline when <see cref="AcceptBaseline"/> is set.
    /// </summary>
    private void Baseline()
    {
        if (AcceptBaseline)
        {
            using var w = FileAccess.Open(BaselinePath, FileAccess.ModeFlags.Write);
            foreach (string line in _lines) w.StoreLine(line);
            GD.Print($"baseline: accepted {_lines.Count} islands into {BaselinePath}");
            return;
        }
        if (!FileAccess.FileExists(BaselinePath))
        {
            GD.Print("baseline: none on disk (set AcceptBaseline to write one)");
            return;
        }
        var before = new Dictionary<string, string>();
        foreach (string line in FileAccess.GetFileAsString(BaselinePath).Split('\n'))
        {
            int cut = line.LastIndexOf('\t');
            if (cut > 0) before[line[..cut]] = line[(cut + 1)..].Trim();
        }
        var moved = new List<string>();
        int missing = 0;
        foreach (string line in _lines)
        {
            int cut = line.LastIndexOf('\t');
            string key = line[..cut], hash = line[(cut + 1)..];
            if (!before.TryGetValue(key, out string? was)) missing++;
            else if (was != hash) moved.Add(key.Replace('\t', ' '));
        }
        GD.Print($"baseline: {moved.Count} of {_lines.Count} islands moved since the accepted run"
                 + (missing > 0 ? $", {missing} not in the baseline" : ""));
        for (int i = 0; i < Math.Min(moved.Count, 40); i++) GD.Print($"  moved  {moved[i]}");
        if (moved.Count > 40) GD.Print($"  ... and {moved.Count - 40} more");
    }

    private int Seed(int i) => FirstSeed + i * 6151;

    private IslandParams At(int size, Action<IslandParams>? set = null)
    {
        var p = (IslandParams)Params.Duplicate();
        p.Size = size;
        set?.Invoke(p);
        return p;
    }

    private void Case(string label, int seed, IslandParams p)
    {
        IslandData d = new IslandGenerator().Generate(seed, p);
        ulong h = Hash(d);
        string line = $"{label}\t{seed}\t{h:x16}";
        GD.Print(line);
        _lines.Add(line);
        _total = Fnv.Mix(_total, h);
        _islands++;
    }

    private static ulong Hash(IslandData d)
    {
        var h = new Fnv();
        int n = d.Size;
        h.Add(n);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            h.Add(d.Land[x, z]); h.Add(d.Region[x, z]); h.Add(d.Material[x, z]);
            h.Add(d.Landform[x, z]); h.Add(d.WaterLevel[x, z]); h.Add(d.Fluid[x, z]);
            h.Add(d.Canyon[x, z]); h.Add(d.Pass[x, z]); h.Add(d.Landings[x, z]);
            h.Add(d.WaterBody[x, z]); h.Add(d.Ferry[x, z]); h.Add(d.Beach[x, z]);
            h.Add(d.Ford[x, z]); h.Add(d.River[x, z]); h.Add(d.Navigable[x, z]);
            h.Add(d.Flow[x, z]); h.Add(d.Walk[x, z]); h.Add(d.Reach[x, z]);
            h.Add(d.ShelfId[x, z]); h.Add(d.Moisture[x, z]); h.Add(d.Warmth[x, z]);
            h.Add(d.Ruggedness[x, z]); h.Add(d.Exposure[x, z]); h.Add(d.RimDistance[x, z]);
            Span[] spans = d.Spans[x, z];
            h.Add(spans?.Length ?? -1);
            if (spans != null) foreach (Span s in spans) { h.Add(s.Bottom); h.Add(s.Top); }
        }
        foreach (var list in new[] { d.CoastCells, d.CliffCells, d.CliffFootCells, d.BankCells,
                                     d.Summits, d.Passes, d.Overhangs })
        {
            h.Add(list.Count);
            foreach (Vector2I c in list) h.Add(c);
        }
        h.AddAll(d.Geysers); h.AddAll(d.Bridges); h.AddAll(d.Berths); h.AddAll(d.Falls);
        h.AddAll(d.Areas); h.AddAll(d.Reaches); h.AddAll(d.Shelves); h.AddAll(d.Gates);
        h.Add(d.Passages.Count);
        foreach (Passage p in d.Passages)
        {
            h.Add(p.Exit); h.Add(p.From); h.Add(p.To); h.Add(p.Cost); h.Add(p.Flights);
            h.Add(p.Path.Count);
            foreach (Vector2I c in p.Path) h.Add(c);
            h.AddAll(p.Built);
        }
        h.Add(d.Name);
        h.AddAll(d.Districts); h.AddAll(d.WaterNames);
        h.Add(d.DuneGrain); h.Add(d.BridgeSpan); h.Add(d.WaterBodies); h.Add(d.BerthSites);
        h.Add(d.Mainland); h.Add(d.Heartland); h.Add((int)d.Style); h.Add((int)d.Arrangement);
        h.Add((int)d.Character); h.Add(d.Attempts); h.Add(d.Unmet); h.Add(d.Rough);
        return h.Value;
    }

    /// <summary>FNV-1a, 64 bit, over a stream of primitives.</summary>
    private struct Fnv
    {
        public const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _h;
        public ulong Value => _h == 0 ? Offset : _h;

        public static ulong Mix(ulong a, ulong b) => (a ^ b) * Prime;

        private void Byte(byte b) { _h = ((_h == 0 ? Offset : _h) ^ b) * Prime; }
        public void Add(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void Add(byte v) => Byte(v);
        public void Add(short v) { Byte((byte)v); Byte((byte)(v >> 8)); }
        public void Add(int v) { for (int i = 0; i < 4; i++) Byte((byte)(v >> (8 * i))); }
        public void Add(Vector2I v) { Add(v.X); Add(v.Y); }
        public void Add(string s) { Add(s.Length); foreach (byte b in Encoding.UTF8.GetBytes(s)) Byte(b); }
        public void AddAll<T>(IReadOnlyList<T> items)
        {
            Add(items.Count);
            foreach (T item in items) Add(item?.ToString() ?? "");
        }
    }
}
