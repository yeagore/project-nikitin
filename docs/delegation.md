# Delegating crude work to sibling models

*A guide for a newbie. Set up 2026-09-17; the two peers since 2026-09-23.*

## The idea in one paragraph

The Claude you talk to in this repo runs on one of two models, each able to
drive a session on its own: **Fable 5.1** or **Opus 5.5**. It can spawn
**subagents**: fresh copies of Claude that get a task, do it in their own
context, and hand back a report. A subagent can run on a cheaper sibling:
**Haiku** (fast, cheap, good at following a precise instruction) or **Sonnet**
(the middle: solid on bounded work with a clear brief). Or it can run on the
*other* peer, as a **second opinion**: Opus asks Fable, Fable asks Opus. The
catch is that a subagent that is not told which model to use inherits the
parent's, so every delegation names its model.

How much goes out depends on who is in the chair. Fable's use is capped at half
of the plan, so a Fable session offloads a good share (35 to 45% of a task) and
hands whole components to Opus. An Opus session sends out only the chores that
would waste it: runs, sweeps and lookups to Haiku, bounded scripts to Sonnet.
Sprites are the exception to all of it: only Fable or Opus draws them.

Two reasons to do it. Cost: a 458-island checksum run on Haiku costs a
fraction of the same run on Fable. Cleanliness: the checksum's transcript
lands in the subagent's context, not the main conversation, which stays about
the work.

## The three pieces, and where they live

**1. The standing rule: `CLAUDE.md`, under Engine & tooling → Delegating.**
This is what the main model reads at the start of every session. It holds the
two peers and what each does in the chair (Fable, on its capped share, aims for
35 to 45% of a task done by others, a direction and not a hard limit; Opus sends
out only what would waste it), the second opinion each asks of the other, the
rule that only the peers draw sprites, a table of four tiers (mechanical →
Haiku, bounded → Sonnet, the peer → a second opinion, or from Fable a whole
component to a written spec; design, the core and the review → the main model), and
what makes a delegation work: a brief that stands alone, a way for the delegate
to check itself, files of its own, no more than two or three at once, and small
enough that an interruption does not leave half-written work. It is checked in, so it follows the repo to the Windows box.
If you ever want the same rule in every project on one machine, copy that
section into `~/.claude/CLAUDE.md` (create the file); the project file is read
as well, so both apply.

**2. Named agents: `.claude/agents/runner.md` and `.claude/agents/scout.md`.**
One file per agent. The top of the file, between the `---` lines, is the
frontmatter: `name`, `description`, `tools`, `model`. The rest of the file is
the agent's system prompt, the standing instructions it gets every time. The
`description` is not decoration: it is how the main model decides when to use
the agent. Both files are checked in, so they are on the Windows box too.

- `runner` (Haiku): builds, runs the checksum, the audit or a bench under a
  timeout, and reports the verdict and the numbers. It has no Edit or Write
  tool, so it cannot change a file even if it wanted to.
- `scout` (Haiku): answers a question about the code (where is X, which
  files do Y) with file:line references. Changes nothing.

**3. A memory note.** In the model's memory folder on the Mac
(`~/.claude/projects/…/memory/delegate-crude-work.md`), not in git. It is the
model's own reminder of your preference, for a session where it works in this
repo without the CLAUDE.md section in front of it. Nothing to maintain.

## What happens in a session

You ask for things the way you always have. When a chore matches a tier, the
main model writes a brief, spawns the subagent with the model named, waits for
the report, and tells you what mattered. The subagent shows up as a task in
the app's tasks pane while it runs.

You can also ask by name: "have the runner do the checksum", or "scout: where
is the ford spacing computed?". In an interactive terminal, `@runner` and
`@scout` autocomplete, and `/agents` lists the agent files it found. (Slash
commands that open a terminal dialog do not work inside the desktop app's
chat.)

Two things worth knowing about what a subagent knows. It reads `CLAUDE.md`,
its own file and the brief. It does not see the conversation, so anything the
task depends on that was said in chat has to be restated in the brief. And
the main model gets back only the subagent's final report, not its tool calls,
so the report is the whole product. A delegate that returns "done" with no
evidence gets asked again.

## Which chore goes where

| You want | Who does it | Why |
|---|---|---|
| The checksum, the audit, the mesh bench or `dotnet build` after a change | `runner` (Haiku) | Run a command, read a number. |
| "Which files mention `Ferry`?", "Where is the estuary's width chosen?" | `scout` (Haiku) | Grep and read; no judgement in the answer. |
| Rename a type across the tree to a spec already settled | Haiku, then `runner` for the checksum | Mechanical if the spec is exact; the checksum polices it. |
| A first draft of the plain-language paragraph for a change already made | Sonnet, then the main model edits | Bounded and checkable, but the register needs a review. |
| A read-through of `Lakes.cs` to answer "what decides a great lake?" | Sonnet | Reading with some judgement; the answer is checkable. |
| What a great lake *should* do | The main model | Design. |
| Anything touching hash salts, `Noise` offsets, sort or scan order | The main model | Determinism. A delegate cannot tell "same" from "same by luck". |
| Reading a delegate's diff before it is trusted | The main model | Review is not crude work. |
| "Is this balance rule sound? Here is what I chose and why" | The other peer (Fable from Opus, Opus from Fable) | A second perspective on a choice that will last; weighed, not obeyed. |
| Drawing or redrawing icons, signs, masks | The main model (Fable or Opus) | Maxim's preference: only the most advanced models draw sprites. |

The economy lab (September 2026) is the worked example of the upper tiers: Sonnet
wrote the data converter with its own assertions, the layout algorithm with a
harness, the element icons and the classification of two hundred recipes; Opus
wrote the inspector dock from a written spec and checked it by screenshot; the
main model kept the data model, the lab's core, the self-test, the briefs and the
review. One lesson from it: two large agents running at once with the main model
also working used the limit up, and both were cut off mid-file. Send fewer at a
time, keep each small, and look at the tree before resuming.

The tiers are a default, not a law. If a Haiku result looks wrong, the main
model is supposed to redo it or send it to Sonnet, and say so.

## Knobs you can turn

- **`model:`** in an agent's frontmatter: `haiku`, `sonnet`, `opus`, `fable`,
  or a full model id (`claude-opus-5-5`, `claude-fable-5-1`). Change `scout` to `sonnet` if its answers are too
  shallow.
- **`tools:`** is an allowlist. Leave Edit and Write off any agent that
  should not change files.
- **Other fields the docs offer:** `permissionMode`; `maxTurns` (a cap on how
  long it may run); `omitClaudeMd: true` (skip the project file; not for these
  two, they lean on it); `memory` (a persistent scratch memory for the agent);
  `isolation: worktree` (its own git worktree, for an agent that edits).
- **The tier table** in `CLAUDE.md`. Move a chore between tiers by editing a
  row.
- **A new agent** when a chore recurs: copy `scout.md`, change the name,
  description, model and body. The description is the part to get right.
- **Per-call override.** The main model can pass a model to any agent,
  including the built-in `Explore`, `Plan` and `general-purpose`, without a
  file. Say "use Sonnet for that" and it will.

## When it goes wrong

- **Vague brief, wrong answer.** A brief needs the paths, the seed, the exact
  command and what done looks like. This is on the main model, and it is the
  main thing to give feedback on.
- **Haiku sounds sure and is wrong.** That is what the review tier is for. If
  you catch one, say so; the memory note gets updated.
- **The delegate did not know something you said earlier.** It cannot. Ask
  the main model to restate it in the brief.
- **A headless run outlived its timeout.** The perl alarm is 900 s and the
  tool's own cap is ten minutes; the runner is told to run long jobs in the
  background and read the log. If a run still hangs, that is the Godot side,
  not the delegation.
- **On Windows.** Same files, same behaviour. The runner knows both Godot
  paths and that Windows prints decimals with a comma.

## What it costs

Haiku is the cheapest and fastest, Sonnet the middle, Opus and Fable the most
capable and the costliest per token, and Fable's use is capped on its own; the current numbers are on
Anthropic's pricing page. The saving is in the runs and sweeps, which are most
of the tokens a session spends on tool output, not in the thinking, which
stays where it was.
