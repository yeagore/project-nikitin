# Dev scenes: the lab, the audit, the checksum

Three scenes under `scenes/dev/` drive the generator without the game. All three
load the same preset, `resources/island_default.tres`, so the audit measures the
island the lab shows. Edit the `.tres` in the Inspector to change it durably; use
the lab's panel (or the Remote tab of the Scene dock) for a throwaway experiment.

Godot is not on `PATH`; from a shell use the console build and quote the path:

```
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

Headless runs print `GD.Print` output and need the C# assembly built first
(`dotnet build "Project Nikitin.csproj"`). Two gotchas: headless Godot sometimes
does not exit on `--quit-after`, so run it under a timeout; and this machine's
locale prints decimals with a comma, which breaks patterns looking for `\.`.

## The island lab — `island_lab.tscn`

Open the project in the .NET editor, build C#, open the scene and press **F6**
(it is not the main scene, so F5 will not run it). The control panel down the
left is the interface; **Tab** hides it. Every control is also a key, and both
write the same `Params`:

| Key | Does |
|---|---|
| **N** / **R** / **F** | new seed / rebuild / frame the island |
| **C** / **V** / **G** | cycle view / character / arrangement |
| **H** / **M** / **L** | hilliness / mix / plateau rungs |
| **U** | toggle the newer shapes in Auto's pool |
| **T** / **Y** | entry Gate kind / bridge ease |
| **B J K O P X** | overlays: bridge sites, Gate landings, ferry berths, fords, roads, compass |
| **I** | liquid on or off: water, goo and falls; off shows the beds |
| **F2** | screenshot |

Camera: **WASD** pan, **Q/E** or middle-drag yaw, middle-drag or **Up/Down**
tilt, wheel zoom, **Shift** faster. (Fords are on **O** because **D** is the
strafe, and liquid on **I** because **W** is forward.)

Views: `height`, `landform`, `region`, `walk` (what connects on foot), `reach`
(what connects once you build; red is out of reach whatever you build),
`shelves`, `surface` (stone, scree, snow, sand, silt, grass, meadow, heath, dust;
an overhang's lip is drawn as stone), `anchors` (what the content layer attaches
to: coast, cliff brink, cliff foot, a ledge where a cell is both, bank, river
bed, lake bed, goo bed, overhang lip, beach, ford, Gate landing, ferry quay,
summit) and the five habitat axes as
ramps: `moisture`, `warmth`, `rugged`, `exposure`, `rim`. Water is coloured by
kind (ford, stream, navigable reach, lake) and goo is violet in every view. The
legend shows each view's actual colours as swatches, from the one palette
(`DevPalette`) the audit's PNGs also use. The lighting is tuned so a top face
reads at about the legend's colour: a steep white sun over a neutral ambient,
linear tonemapping, no specular.

In the `anchors` view a column is coloured per span: only the lip of an
overhang is magenta, and the ground under it is whatever it is — a river bed,
a cliff foot. Turn the liquid off (**I**) to see the beds.

Overlays: **B** bridge sites; **J** each Gate's 1 × 3 landing strip; **K** ferry
berths; **O** fords; **P** the roads between the Gates (pale yellow walk; red
stair, gold bridge, cyan ferry); **X** the compass, each Gate's landward vector,
the Domain's wind — a run of orange arrows standing off the upwind edge with
its name, whether or not there are dunes, plus its grain along each dune field —
and two bounding boxes: the faint cube of the Domain (Size cells across and
Size slabs tall, standing on the keel's lowest point; nothing the generator
builds may hang outside it, and its shape never changes between seeds) and a
gold box tight round the landmass.

The readout at the top right says what the view means, then what the island
turned out to be: name, arrangement, the landforms it got, the ladder, walk and
reach shares, shelves, berths, rivers, the wind, Gates, and what each road out
costs.
`ROUGH GOING` means a road climbs five elevators in fifteen cells; `COAST WOULD
NOT` means a Gate you asked for is not the Gate you got.

If the window is 1152 × 648 and will not stretch, the editor is embedding the
game: Editor Settings → Run → Window Placement → Game Embed Mode: Disabled.

## The audit — `generation_audit.tscn`

```
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
```

Runs the real generator over 60 seeds at 128² and prints the measured
guarantees: the step grammar, patches, landforms, lakes and goo, rivers, ferries,
surfaces and habitat, roads, Gates, crossings, continuity. Run it after any
change to the generator. It ends by diffing thirty headline numbers against
`docs/audit-baseline.json`; that is a diff, not a test — set `AcceptBaseline` to
accept the current numbers as the new reference.

The opt-in sweeps are `[Export]` flags on `GenerationAudit` (each documented on
its property): silhouettes and waterways as ASCII, close-ups of the sculpted
landforms, every arrangement × character, every Gate request, the four-hanging-
Gates matrix, the knob sweeps (how you check a slider does anything), land share
per arrangement, the guarantee set at all five sizes, the newest shapes at every
footprint, where re-rolls cluster, and PNG portraits and field maps written to a
directory. Every flag can be given on the command line after `--`:

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Knobs Sizes Portraits=C:/tmp/portraits
```

Appearance still needs a human at the editor, or **F2** in the lab.

## The checksum — `generation_checksum.tscn`

```
godot --path . --headless scenes/dev/generation_checksum.tscn
```

Hashes every field of `IslandData` for 440 islands — 60 default seeds, every
arrangement × character at 64², all five sizes, every Gate request, every bridge
ease, both ends of every knob — and diffs the hashes against
`docs/checksum-baseline.txt`. A change meant to leave generation alone must
report `0 of 440 islands moved`; a change meant to alter it re-baselines with
`-- accept` on the command line and says so in its commit. This is the
bit-for-bit gate; the audit is the readable one.
