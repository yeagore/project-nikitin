using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Measures the real generator over many seeds and prints the guarantees of
/// docs/island-generation.md as numbers; headless, since it only reads <see cref="IslandData"/>:
/// <c>godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn</c>.
/// Every export can also be set after <c>--</c> on the command line — a bare name turns a
/// flag on, <c>Portraits=&lt;dir&gt;</c> / <c>FieldMaps=&lt;dir&gt;</c> set the paths and
/// <c>Seeds=&lt;n&gt;</c> (FirstSeed, FeasibilitySeeds, SweepSeeds) the ints, so
/// <c>-- Knobs Portraits=C:/x</c> works.
/// </summary>
public partial class GenerationAudit : Node
{
    [Export] public int Seeds { get; set; } = 60;
    [Export] public int FirstSeed { get; set; } = 5000;
    [Export] public IslandParams Params { get; set; } = null!;

    /// <summary>ASCII silhouette of one island per arrangement: shape is checkable headless where appearance is not.</summary>
    [Export] public bool Silhouettes { get; set; } = false;

    /// <summary>One island's water at full resolution, a character to the cell.</summary>
    [Export] public bool Waterways { get; set; } = false;

    /// <summary>Land per arrangement over SweepSeeds seeds each, thinnest first; Auto rolls give some shapes one island in sixty.</summary>
    [Export] public bool Bulk { get; set; } = false;

    /// <summary>The guarantee set at every supported footprint — 48, 64, 72, 96, 128 — over SweepSeeds seeds each.</summary>
    [Export] public bool Sizes { get; set; } = false;

    /// <summary>Directory for a top-view PNG of two islands per arrangement, or empty for none: how a shape gets looked at headless.</summary>
    [Export] public string Portraits { get; set; } = "";

    /// <summary>Directory for habitat, anchor and surface PNGs of the first few seeds, or empty for none.</summary>
    [Export] public string FieldMaps { get; set; } = "";

    /// <summary>The probation workup: each of the newest arrangements at every footprint and against every character.</summary>
    [Export] public bool Debut { get; set; } = false;

    /// <summary>Every arrangement at 48² / 64² / 128², hardest-pressed first — the shortlist for a future size gate.</summary>
    [Export] public bool Strain { get; set; } = false;

    /// <summary>A digit height map of one patch each of badlands, karst, massif, dunes and sinkholes.</summary>
    [Export] public bool Sculpts { get; set; } = false;

    /// <summary>Every arrangement x character, FeasibilitySeeds each; slower than the rest of the audit put together.</summary>
    [Export] public bool Feasibility { get; set; } = false;

    /// <summary>Seeds per combination in the Feasibility and GateMatrix sweeps.</summary>
    [Export] public int FeasibilitySeeds { get; set; } = 3;

    /// <summary>Ask for each Entry edge and kind, and each Exit count and kind, and report what came out.</summary>
    [Export] public bool GateRequests { get; set; } = false;

    /// <summary>Four hanging Gates — the maximum request — for every arrangement x character, then the reductions.</summary>
    [Export] public bool GateMatrix { get; set; } = false;

    /// <summary>Sweep Lakes, Rivers, Crossings and Valleys with everything else held, so a knob that does nothing shows.</summary>
    [Export] public bool Knobs { get; set; } = false;

    /// <summary>Seeds per setting in the GateRequests, Knobs, Bulk, Sizes, Debut and Strain sweeps.</summary>
    [Export] public int SweepSeeds { get; set; } = 12;

    /// <summary>Write this run's headline numbers to docs/audit-baseline.json as the accepted answer.</summary>
    [Export] public bool AcceptBaseline { get; set; } = false;

    /// <summary>Landform names in <see cref="LandformType"/> order, for the printed buckets.</summary>
    private static readonly string[] TypeName =
    {
        "plain", "hills", "mountain", "mesa", "basin", "badlands", "karst",
        "massif", "dunes", "sinkholes",
    };

    private static readonly int Forms = TypeName.Length;

    public override void _Ready()
    {
        ApplyCommandLine();
        Params ??= new IslandParams();
        var t = new Tally(Params);
        ulong t0 = Time.GetTicksMsec();

        for (int i = 0; i < Seeds; i++)
        {
            int seed = SeedAt(i);
            t.Measure(new Island(seed, IslandGenerator.Generate(seed, Params)));
        }

        ulong ms = Time.GetTicksMsec() - t0;
        GD.Print($"=== generation audit: {Seeds} islands, {Params.Size}², {ms} ms total ===\n");
        PrintSteps(t);
        PrintPatches(t);
        PrintLandforms(t);
        PrintRivers(t);
        PrintFerries(t);
        PrintOverhangs(t);
        PrintSurfaces(t);
        PrintHabitat(t);
        PrintWater(t);
        PrintGorges(t);
        PrintCharacters(t);
        PrintWalkability(t);
        PrintPasses(t);
        PrintShelves(t);
        PrintCrossings(t);
        PrintGates(t);
        PrintRoads(t);
        PrintArrangements(t);
        PrintRerolls(t);
        PrintSweeps();
        PrintContinuity(t);
        Baseline(BaselineNumbers(t));
    }

    /// <summary>Exports named after <c>--</c> on the command line: bare flags, Name=dir paths, Name=n ints.</summary>
    private void ApplyCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            string key = eq < 0 ? arg : arg[..eq];
            string value = eq < 0 ? "" : arg[(eq + 1)..];
            switch (key)
            {
                case nameof(Silhouettes): Silhouettes = true; break;
                case nameof(Waterways): Waterways = true; break;
                case nameof(Bulk): Bulk = true; break;
                case nameof(Sizes): Sizes = true; break;
                case nameof(Debut): Debut = true; break;
                case nameof(Strain): Strain = true; break;
                case nameof(Sculpts): Sculpts = true; break;
                case nameof(Feasibility): Feasibility = true; break;
                case nameof(GateRequests): GateRequests = true; break;
                case nameof(GateMatrix): GateMatrix = true; break;
                case nameof(Knobs): Knobs = true; break;
                case nameof(AcceptBaseline): AcceptBaseline = true; break;
                case nameof(Portraits): Portraits = value; break;
                case nameof(FieldMaps): FieldMaps = value; break;
                case nameof(Seeds): Seeds = int.Parse(value); break;
                case nameof(FirstSeed): FirstSeed = int.Parse(value); break;
                case nameof(FeasibilitySeeds): FeasibilitySeeds = int.Parse(value); break;
                case nameof(SweepSeeds): SweepSeeds = int.Parse(value); break;
                default: GD.Print($"audit: unknown argument '{arg}'"); break;
            }
        }
    }

    /// <summary>The opt-in printers, in the order they have always run.</summary>
    private void PrintSweeps()
    {
        if (Silhouettes) PrintSilhouettes();
        if (Waterways) PrintWaterways();
        if (Sculpts) PrintSculpts();
        if (Feasibility) PrintFeasibility();
        if (GateRequests) PrintGateRequests();
        if (GateMatrix) PrintGateMatrix();
        if (Knobs) PrintKnobs();
        if (Bulk) PrintBulk();
        if (Sizes) PrintSizes();
        if (Debut) PrintDebut();
        if (Strain) PrintStrain();
        if (Portraits.Length > 0) WritePortraits();
        if (FieldMaps.Length > 0) WriteFieldMaps();
    }

    /// <summary>The last accepted headline numbers — a diff, not a test; AcceptBaseline rewrites it.</summary>
    private const string BaselinePath = "res://docs/audit-baseline.json";

    /// <summary>Prints every headline number that moved since the accepted run, or accepts this run as the baseline.</summary>
    private void Baseline(Godot.Collections.Dictionary<string, Variant> now)
    {
        string json = Json.Stringify(now, "  ", sortKeys: true);

        if (AcceptBaseline)
        {
            using var write = FileAccess.Open(BaselinePath, FileAccess.ModeFlags.Write);
            if (write == null)
            {
                GD.Print($"\nbaseline: could not write {BaselinePath} "
                    + $"({FileAccess.GetOpenError()})");
                return;
            }
            write.StoreString(json + "\n");
            GD.Print($"\nbaseline: accepted this run as {BaselinePath}");
            return;
        }

        using var read = FileAccess.Open(BaselinePath, FileAccess.ModeFlags.Read);
        if (read == null)
        {
            GD.Print($"\nbaseline: none yet — run with AcceptBaseline to write "
                + $"{BaselinePath}");
            return;
        }

        var was = Json.ParseString(read.GetAsText()).AsGodotDictionary();
        var moved = new List<string>();
        foreach (var (key, value) in now)
        {
            if (!was.ContainsKey(key)) { moved.Add($"    {key,-22} new  {value}"); continue; }
            double before = was[key].AsDouble(), after = value.AsDouble();
            if (Mathf.IsEqualApprox((float)before, (float)after)) continue;
            double delta = after - before;
            moved.Add($"    {key,-22} {before,12:0.##} -> {after,-12:0.##} ({delta:+0.##;-0.##})");
        }

        GD.Print($"\nbaseline: {moved.Count} of {now.Count} numbers moved since the last "
            + "accepted run");
        foreach (string line in moved) GD.Print(line);
        if (moved.Count > 0)
            GD.Print("    (a diff, not a failure — decide whether you meant it, then "
                + "re-run with AcceptBaseline)");
    }
}
