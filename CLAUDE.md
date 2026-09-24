# Project Nikitin

A single-player economic/exploration strategy game built in **Godot 4.7**. The
player is a merchant-pioneer running a trading company across the **Ecumene**, a
tree of floating-island worlds (**Domains**) connected by **Gates**. Think Anno /
early Paradox economy sim, fantasy setting, procedurally generated worlds, an
in-fiction Age of Exploration driven by opening links between Domains.

This file holds only what is true of the whole project. What is particular to one
part lives beside that part, in a `CLAUDE.md` of its own that loads when you read
files there, and in `docs/`.

## Where things are written down

| Working on | Read first | Spec and manual |
|---|---|---|
| The island generator (`scripts/generation/`) | `scripts/generation/CLAUDE.md`: the pipeline, the knobs, the two regression gates, determinism | `docs/island-generation.md`, `docs/island-generation-appendix.md` |
| The terrain renderer (`scripts/terrain/`) | `scripts/terrain/CLAUDE.md`: the mesher, the measurements, many Domains | `docs/island-generation.md` §4 |
| The economy model (`scripts/economy/`) | `scripts/economy/CLAUDE.md`: webs, palettes, slots, tags, varieties | `docs/economy-lab.md` |
| Any dev scene (`scripts/dev/`) | `scripts/dev/CLAUDE.md`: which files are which tool, the economy lab's house rules, shell runs | `docs/dev-scenes.md`, `docs/economy-lab.md` |
| Handing chores to cheaper models | **Delegating**, below | `docs/delegation.md` |

**Documentation split:** the **Notion wiki is the design overview** (premise,
concepts, glossary, decisions); see **Design source of truth** below. **Technical
detail lives in this repo.** `docs/island-generation-plain.md` is a plain-language
retelling for Maxim: not a source for you, but a document you owe an update to
whenever a change alters what the generation spec or the dev-scenes manual say.
When a task needs a design fact that is not written down, ask rather than invent,
and offer to log the answer in the Notion Decision Log.

Keep it this way: a fact about one subsystem goes in that subsystem's file, and
this one stays short.

---

## Engine & tooling

| | |
|---|---|
| Engine | Godot **4.7**, Forward+ renderer, Direct3D 12, **Jolt** physics |
| Scripting | **C#** (Godot .NET). `Project Nikitin.csproj` uses `Godot.NET.Sdk/4.7.2`, `net8.0`, nullable enabled, root namespace `ProjectNikitin`. Needs the .NET ("Mono") build of the editor. |
| Main scene | `res://scenes/main/main.tscn` |
| Platform | Two machines: a Mac (zsh; Godot at `/Applications/Godot_mono.app`) and a Windows box (PowerShell; Godot on `D:`). The checksum reproduces bit-for-bit across both. |

`.godot/` is generated and git-ignored; never edit or commit it. `*.uid` and
`*.import` sidecars are tracked (a headless `--editor --quit-after 3` run writes
the `.uid` files for new scripts, `--import` the `.import` files).

### Building & running

The C# side builds standalone with `dotnet build "Project Nikitin.csproj"`; do
this after editing any `.cs`. Godot is off `PATH` on both machines:

```
/Applications/Godot_mono.app/Contents/MacOS/Godot                          # macOS
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe   # Windows
```

It runs headless, so the dev scenes can be executed from a shell and their output
read without a window; each subsystem's `CLAUDE.md` names the runs that matter
after touching it. Run headless scenes under a timeout (Godot does not always
exit; macOS has no `timeout`, use `perl -e 'alarm 900; exec @ARGV' <godot> ...`).
The Windows machine prints decimals with a comma. A windowed run from a shell
(a `shot`) opens a window for a few seconds on the machine it runs on.

### Delegating

**Two peers in the chair** (Maxim's, 2026-09-23): **Fable 5.1** and **Opus 5.5**
are each an excellent main driver on their own, and each is the other's second
opinion. Sonnet and Haiku take the chores. Expect this to change again as the
models do (Sonnet, Haiku, Fable 5.5).

- **Opus 5.5 in the chair** delegates only what would waste it: mechanical work to
  Haiku, bounded work with a check to Sonnet. No budget share to meet.
- **Fable 5.1 in the chair** is on a capped share of the plan (Fable is held to
  half of the total use on Max plans), so it offloads more: **aim for 35 to 45% of
  a task done by the others**, and a whole component to a written spec goes to
  Opus 5.5. A direction, not a hard constraint (2026-09-22).
- **A second opinion**, either way round (`model: "fable"` from Opus, `"opus"`
  from Fable), where a second perspective pays: a choice with lasting
  consequences (the data model, a balance rule, anything near determinism), a
  large diff before it is committed, a judgement of pictures. The brief gives the
  question, the options and the reasoning so far, and asks for disagreement, not
  assent; the answer is weighed, not obeyed, and the report says what it changed.
  A Fable opinion spends Fable's capped share: ask where it matters, not by habit.
- **Sprites** (sample icons, signs, masks) are drawn only by the most advanced
  models, Fable 5.1 and Opus 5.5 today (Maxim's preference). Sonnet may write the
  tooling around them: packing a sheet, a measurement script.

A subagent (the Agent tool) runs on the `model` it is given, and one given none
inherits the parent's; so work goes out with the model named, and what comes back
is read before it is trusted. Four tiers:

| Tier | Model | What goes there |
|---|---|---|
| Mechanical | `haiku` | Run a command and report the verdict and the numbers (the build, the checksum, the audit, the benches, a self-test); sweep the tree for every site that does something; count, list, tabulate; rename to a spec already settled; look a fact up in documentation or Notion. |
| Bounded | `sonnet` | Work with a clear brief and a check on the result: a one-off data script with its own assertions; a classification or a first draft over a list; a small algorithm with a harness; a doc passage for a change already made; a refactor the checksum will police. |
| Peer | `opus` / `fable` | The other of the two: a second opinion; from Fable, also a whole component to a written spec where judgement about layout or structure is needed and the result can be looked at (a dock of a lab, a dialog, a view), with one file of its own and a way to see its work. |
| The main model | | Design and decisions; the data model and the core others build on; anything that can move the checksum or the audit, or touches the determinism details in `scripts/generation/CLAUDE.md`; the brief itself; the review of what a delegate returns; sprites; anything that needs the conversation, which a subagent does not see. |

What makes a delegation work, learnt the hard way:

- **A brief that stands alone**: the paths, the command, the exact interface to
  build against, the rules of the house, and what done looks like. A subagent sees
  this file, its own file and the brief (the nested `CLAUDE.md` files only if it
  reads there).
- **A way for the delegate to check itself**: assertions in the script, a
  harness, the self-test, a screenshot to read. The pieces that came back right
  first time were the ones that could see their own result.
- **Its own files.** Two agents never edit one file; a delegate never edits the
  core. Independent delegations go out in one message so they run at once, but
  not more than two or three at a time: the limit is shared, and an agent cut off
  mid-run leaves half-written work. After an interruption, look at the tree (does
  it build, what changed) before resuming or redoing.
- **Small enough to survive**: a delegation that would take an hour is two
  delegations.

Two agents under `.claude/agents/` package the common cases and can be asked for
by name: **`runner`** (haiku) builds, runs the dev scenes under a timeout and
reports the verdict, not the transcript; **`scout`** (haiku) answers a question
about the code by reading it, and changes nothing. For other chores, `Explore` or
`general-purpose` with `model` set. `docs/delegation.md` is the guide.

---

## Spatial model (from Notion → "The Ecumene")

- A **Domain** is a 3D landmass or archipelago of terrain units suspended in
  aether: flying islands, coarse scale (one unit's top face is an orchard or a
  housing compound). Gravity points −Y. **There is no sea:** a coast, a beach, a
  fjord or a "sea stack" faces the aether; the water is rivers, lakes and springs,
  and every river pours off the rim. Nothing coastal in the sea's sense (sea salt,
  sea fish, seaweed, shells, beach palms) belongs anywhere. Each Domain sits in an invisible bounding
  cube that keeps vessels in but does not block Gate travel.
- The terrain unit is a **slab**: a square cell 1 wide and **1/4 as tall**
  (`SLAB_HEIGHT = CELL_SIZE / 4`). Terrain Y is an integer slab index. The
  ratio is decided; the Notion wiki still says a tentative "8?".
- **Traversal:** a one-slab step (0.25 u) is free. A face of two or three slabs
  is a **scarp**, which a ladder climbs; four or more is a **cliff**, which a
  stair or an elevator climbs, up to 8 (`Traversal.FreeStep`, `Traversal.CliffFace`).
  Both are walls to walking. Terrain generated under a one-slab slope limit is
  walkable by construction; every scarp and cliff is one some rule put there.
  Walking is by king's moves: a corner is cut unless both cardinal cells beside
  the diagonal are more than a free step off. Works, anchors and water stay cardinal.
- **Three supported footprints: 64², 96², 128²** (128² is the stress target;
  48² and 72² were dropped on 2026-09-05, 48² because the footprint constants
  measured in cells wreck the split shapes there, 72² with the ladder it sat
  on). Altitude is bounded by the same number in slabs, so the bounding
  cube is a real shape, and the landmass takes 55–85% of the grid's extent.
  30–40 Domains per game; up to four side Links per Domain, one Gate per edge.
- **Terrain is stored per column**, not as a voxel array: each `(x, z)` holds a
  short list of `Span(bottom, top)` solid runs. The air gap between two spans is
  an overhang or arch; branching caves are not supported. Never one node per
  slab: a 128² island is tens of thousands of columns, which is why the columnar
  model and a batched mesher exist.
- **Biome features** (forests, herds, coral, vines) are a separate layer that
  sits on, beside or under slab stacks. It does not exist yet.

### Code conventions

| Constant | Value | Meaning |
|---|---|---|
| `Terrain.CellSize` | `1.0` | X/Z size of one cell, in metres. |
| `Terrain.SlabHeight` | `0.25` | Y size of one slab. |
| Grid → world | `Vector3(gx * CellSize, gy * SlabHeight, gz * CellSize)` | `gy` is a slab index. |

Godot axes: **Y up**, right-handed, cameras look down −Z, 1 unit = 1 metre.

**`.tscn` `Transform3D` gotcha:** the text form serialises the basis row-major,
the transpose of the constructor. Do not hand-author rotated bases; use
translation-only transforms and orient cameras and lights in code (`LookAt`).

---

## Repository layout

```
project.godot                  Engine config. run/main_scene points at main.tscn.
Project Nikitin.csproj / .sln   .NET project (Godot.NET.Sdk 4.7.2, net8.0).
scenes/main/main.tscn          The game scene: one generated Domain through IslandRenderer.
scenes/dev/                    Six dev scenes: island_lab, generation_audit, generation_checksum,
                               mesh_bench, domains_bench, economy_lab.
scripts/
  Main.cs, CameraRig.cs        The game scene's script; the strategy camera.
  generation/                  ProjectNikitin.Generation: the island generator.      CLAUDE.md inside.
  terrain/                     ProjectNikitin.Meshing: the terrain renderer.         CLAUDE.md inside.
  economy/                     ProjectNikitin.Economy: webs, palettes, recipes.      CLAUDE.md inside.
  dev/                         ProjectNikitin.Dev: the dev scenes' scripts.          CLAUDE.md inside.
resources/island_default.tres  The IslandParams preset every dev scene and the game scene load.
resources/economy/             webs/*.json (each a whole web with its palette) and sprites/ (icons, signs, masks, tags, elements).
tools/                         One-off data scripts (the economy import, the tagged and variety ledgers, the sprite sheets).
docs/                          Specs, manuals, baselines (see the table above), delegation.md.
.claude/agents/                runner and scout, the two packaged subagents (see Delegating).
```

Namespace `ProjectNikitin.Meshing`, not `.Terrain`: a namespace of that name would
shadow the `Terrain` constants class for every file under `ProjectNikitin`.

Planned, create as needed and keep the tree shallow: `resources/` for biome,
archetype and goods data, `addons/` for plugins.

### Naming

- Scenes, `.tscn`/`.tres` and their folders: `snake_case`.
- C# files `PascalCase`, one type per file, file name = type name; a class split
  across files uses `Name.Part.cs`. Namespace `ProjectNikitin` or a sub-namespace.
- Use the design vocabulary in code: `Domain`, `Slab`, `Gate`, `Link`, `Polity`,
  `Settlement`, `Essence`, not "block", "portal", "faction", "town"; in the
  economy, `Web`, `Palette`, `Good`, `Recipe`, `Slot`, `Consumer`, `Variety`.

---

## Glossary (condensed from Notion)

- **Ecumene**: the whole game world, the tree of Domains. **Domain**: one
  floating landmass or archipelago. The **Home Domain** is where the player starts.
- **Aether**: the space between Domains; hazardous to people.
- **Link**: a fast, safe route through aether joining two Domains. **Gate**: the
  built structure at each end. Links form a tree. A **hanging Gate** floats five
  cells off the rim and is flown through, so the Domain owes it a 1 × 3 landing
  strip running inland; this is the normal case. A **land Gate** is the same
  site with the portal on the strip, walked through. A Link joins two Gates of
  the same kind. One Gate per edge: one Entry, one to three Exits.
- **Slab**: the terrain unit, 1 × 1 × 0.25. **Biome**: a Domain's flora, fauna
  and climate.
- **Free step / Scarp / Cliff**: a face of one slab is walked; of two or three
  slabs, a scarp a ladder climbs; of four or more, a cliff a stair or an
  elevator climbs. Two anchor triplets follow them: cliff brink, foot and ledge;
  scarp brink, foot and ledge.
- **Polity**: an NPC state ruling Domains. **Metropole**: the Polity the player
  answers to. **Cultural Archetype**: a people's template (Steelfolk, Lakefolk,
  Jadefolk), carrying Traits: School of Magicks, Societal Structure, Political
  Situation, Means of Extraction.
- **Class / Role / Prestige**: population stratification; Role gates employment,
  Prestige gates promotion and consumption.
- **Magicks**: the magic system. **Essence**: the refined magical resource,
  provisionally also the currency. **Means of Extraction**: how a Domain refines
  Essence early on.
- **Settlement**: the basic economic unit: market, warehouses, districts,
  facilities and land. **Needs**: Food, Intoxicants, Clothing, Wares, modulated
  by Habits, Sophistication, Pickiness and Fashion.
- **Player Avatar**: the on-map character, a mobile order relay. **Pioneers /
  Aethernaut / Aethership**: expedition crew, scout and vessel. **Aspiration**:
  the run's win condition.
- **Web / Palette / Recipe / Slot / Consumer / Variety**: the economy lab's words.
  A web is one version of the economy with its own palette of goods and tags; a
  recipe's slots accept goods or tags; a consumer marks consumables; a variety is
  a good's particular kind, authored (rye) or derived from what went in (an
  arsenic-hearted golem). `scripts/economy/CLAUDE.md` has them exactly.

---

## Design source of truth — Notion

Wiki database **"🪙 Project Nikitin"** (Notion MCP connector).

| Page | State | Notes |
|---|---|---|
| Premise and Vision | written | What the game is and why. |
| The Ecumene | written | Domains, slabs, Links, Gates, scale. Read before terrain work. |
| Mechanics and Concepts | index | Parent of the mechanics pages. |
| The Gameplay Loop → The First Hour | written | Best description of moment-to-moment play. |
| [ARCHIVED] Economy, Population and Settlements | archived | The old draft: settlements, classes, Needs, money. The Alchemical Economy page sits under it. |
| → [CLAUDE] Alchemical Economy | Claude's, kept current | The economy lab's design as it stands, with examples and pictures: webs, palettes, tags and roles, varieties, properties and stacks, sites and extractions, a web's day (amounts, the balance, loops), elements, signs and icons, the ledgers, open questions. Proposals, with what Maxim has ruled marked. Updated 2026-09-24. |
| Generation → Island Generation | short | Requirements checklist for island generation. |
| Generation → Part 1: Terrain and Climate | written | The generator in plain words, with the audit's sheets. |
| Terrain → [CLAUDE] The Soil Glossary | proposal | The surface materials' plain names, Latinate names, codes and colours; the code uses the plain names and the colours. |
| Terrain, Polities, Magicks, Lore, Content | stubs | Terrain holds biome sketches in prose. |
| Glossary | partial | |
| Decision Log | DB, near-empty | Log firm decisions here, with the why and the alternatives. |
| Open Questions | DB | Unresolved design questions. |
| Production Tasks → Tasks | DB | Roadmap: Prototype 0 → Prototype 1 → Vertical slice → Later. |
| Journal for Thoughts and Bits | DB | Loose ideas not yet promoted. |

Consult the relevant page before non-trivial design work. When a decision gets
made in a session, offer to add it to the Decision Log and to close the matching
Open Question. Two decisions are made but not yet logged there: the slab's 1:4
ratio, and the three supported footprints (the Ecumene page still says 16³–64³).

When writing to Notion, create a new page whose title starts with "[CLAUDE]".
**A `[CLAUDE]` page is Claude's own** (Maxim, 2026-09-24): keep it up to date
whenever what it describes changes, without being asked, and say so in the
report. Today that is **[CLAUDE] Alchemical Economy**, for anything that changes
the economy model, the webs or the lab, and **[CLAUDE] The Soil Glossary**, for
the soils. Edit such a page section by section (`update_content`), never by
replacing it whole: a whole replacement drops the comment threads on it, and
other people comment there. Never edit any other existing page unless I
explicitly ask you to edit or check that specific page.

---

## Roadmap

- **Prototype 0**: dev environment (git, Godot, Claude, VS Code). Done; the repo
  is at `yeagore/project-nikitin`.
- **Render an island**, branch `island-generation`, merged in PRs
  [#1](https://github.com/yeagore/project-nikitin/pull/1),
  [#3](https://github.com/yeagore/project-nikitin/pull/3),
  [#4](https://github.com/yeagore/project-nikitin/pull/4),
  [#5](https://github.com/yeagore/project-nikitin/pull/5) (the magick layer),
  [#7](https://github.com/yeagore/project-nikitin/pull/7) and
  [#8](https://github.com/yeagore/project-nikitin/pull/8) (fjords, estuaries,
  water depth, the keel's root, the soil glossary). Every generation stage is
  done and audited at all three footprints.
- **The mesher**, branch `mesher`, PR
  [#6](https://github.com/yeagore/project-nikitin/pull/6): the chunked
  span-aware renderer with colliders, drawing the main scene and the lab, with
  its two benches and the lab's cursor pick as the colliders' first reader.
  Built, measured and checked; the performance question is answered.
- **The economy lab**, branch `economy-lab`: stage one, the editor of production
  webs and the data model under it: a palette per web, tag slots, tag namespaces
  with roles, varieties that ride from inputs to outputs, property tags they imply
  and units stack by, sites a recipe stands on, icons recoloured by variety.
  Stage two, begun 2026-09-23 on the skeletal web Hearth: amounts and time on
  the recipes, wants at the consumers, a balance per web; every good comes out
  of something (extractions on sites of soils and the terrain's feature
  anchors, with limits), loops with boosts and by-products. Next: buildings and
  labour, sites read against a Domain, the next rungs of the ladder, and
  property tags read by prices and uses.
- What comes after on the terrain side, in rough order, is in `docs/island-generation.md` §6:
  settlement placement, the biome layer above `Material` (which is also where
  the ground gets a look beyond flat colours), and span-aware pathing.

---

## Open questions

Flagged so they are not silently hard-coded:

1. **Essence as currency.** Provisional; expect grades of Essence or per-Polity
   currencies later.
2. **Domains loaded at once.** Whether only the active Domain is simulated and
   rendered, or several. Drives the streaming and LOD approach. The renderer
   does not constrain it: forty 128² Domains in view hold 120 Hz (see
   `scripts/terrain/CLAUDE.md`); generation time and the simulation are what would.
3. **Camera.** `CameraRig` pans, yaws, pitches and wheel-zooms, aimed with
   `LookAt`; it polls physical keys. Undesigned: edge-scroll, orthographic, pan
   bounds, an InputMap.
4. ~~**Domain size ladder.**~~ Decided 2026-09-05: 64 / 96 / 128. Not yet in
   the Notion Decision Log.
