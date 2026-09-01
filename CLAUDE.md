# SpaceGame

Unity project. Documentation lives in [docs/](docs/), split by audience: [docs/Human/](docs/Human/)
explains the game to people, [docs/AI/](docs/AI/) is a retrieval-optimised reference for agents.
The repo-specific skills in [.claude/skills/](.claude/skills/) cover how *this* codebase does things.

## Documentation — read before you change code

**Start every task at [docs/AI/INDEX.md](docs/AI/INDEX.md).** It routes you to the one or two
system docs that cover your task. Do not read the whole corpus.

```bash
grep 'Scripts/Items/Artifacts' docs/AI/ROUTING.md   # which doc governs this code?
grep -i 'client'               docs/AI/ROUTING.md   # which doc explains what I'm seeing?
```

| Read | When |
| --- | --- |
| [docs/AI/INDEX.md](docs/AI/INDEX.md) | Always, first. The map. |
| [docs/AI/ROUTING.md](docs/AI/ROUTING.md) | To find the right doc. **Grep it, never read it whole.** |
| [docs/AI/INVARIANTS.md](docs/AI/INVARIANTS.md) | Once, before your first change. The rules that hold everywhere. |
| [docs/AI/GLOSSARY.md](docs/AI/GLOSSARY.md) | When a noun in the task is ambiguous. |
| [docs/AI/DEFECTS.md](docs/AI/DEFECTS.md) | Before debugging anything. It may already be a known, unfixed defect. |
| [docs/AI/systems/](docs/AI/systems/) | The subsystem reference itself — read your one or two docs **in full**. |

Every system doc has the same shape: **Model → Key types → Flows → Multiplayer → Persistence →
Gotchas → Extending**. `Gotchas` records the silent failures — read it before editing, not after.

### Documenting your change is part of the change

**Every change to behaviour updates its doc in the same commit.** A doc describing code that no
longer exists is worse than no doc, because agents trust it and act on it.

1. Find the governing doc (`grep <path> docs/AI/ROUTING.md`); if there is none, write one.
2. Update the affected rows and **delete what your change made untrue**.
3. Add anything non-obvious that bit you to that doc's `## Gotchas` — or to
   [INVARIANTS.md](docs/AI/INVARIANTS.md) if it applies to three or more subsystems.
4. Add a `symptoms:` frontmatter entry for anything that cost real time to diagnose, phrased as
   what you *saw*. That is how the next agent finds it.
5. Bump `updated:`, then regenerate and validate:
   ```bash
   python3 tools/docs_check.py --index    # regenerates INDEX.md + ROUTING.md, then validates
   ```
   `INDEX.md` and `ROUTING.md` are **generated from frontmatter — never hand-edit them.**
6. A **new** system also needs a short plain-language entry in
   [docs/Human/the-systems.md](docs/Human/the-systems.md) — the validator fails without it.
   Update the ten narrative chapters in [docs/Human/](docs/Human/) only when the *shape* of a
   system changes — a new way to play, a reversed decision, a subsystem that is gone. Not for
   renames.

Full rules: [docs/AI/CONTRIBUTING.md](docs/AI/CONTRIBUTING.md).

## Skills

| Skill | Use it for |
| --- | --- |
| [spacegame-agent](.claude/skills/spacegame-agent/SKILL.md) | Creatures, NPCs, enemies, mounts, turrets: AI behaviour and factions |
| [spacegame-artifact](.claude/skills/spacegame-artifact/SKILL.md) | Usable items: gadgets, spells, weapons, hotbar slots, hold poses |
| [spacegame-multiplayer](.claude/skills/spacegame-multiplayer/SKILL.md) | Netcode: host works, clients broken; ownership, RPCs, prefab registration |
| [spacegame-persistence](.claude/skills/spacegame-persistence/SKILL.md) | Save/load: state resets, entities duplicate, savers missing from JSON |
| [blender-model](.claude/skills/blender-model/SKILL.md) | Any 3D asset — models, props, variants — in the `.blend` library |

## Non-negotiables for every new feature

### 1. It must work in multiplayer

Single-player runs as a host of one, so "it works on my machine" proves nothing. Every feature is
designed for host **and** client from the start — not retrofitted later.

- Put the effect on the right side of the authority split (`Use()` / `Present()`,
  server-authoritative vs. owner-authoritative) — see the `spacegame-multiplayer` skill.
- Register anything spawned at runtime in the network prefab list.
- **Verify on an actual client**, not just the host. A feature that has only been seen working on
  the host is not finished.

### 2. It must survive save/quit/load

Persistence here fails *silently* — nothing throws, the state is just gone. A feature that
introduces runtime state is not done until that state reloads correctly.

- Address saved objects by **identity, never by scene** — see the `spacegame-persistence` skill.
- Runtime-spawned things need a registered prefab id, or they vanish on load.
- **Verify by reloading**, and check the value actually appears in the save JSON.

If a feature genuinely holds no state worth persisting, say so explicitly rather than skipping the
question.

### 3. No code smells

Match the surrounding code and leave nothing for someone else to clean up:

- No dead code, commented-out blocks, or leftover debug logs.
- No copy-paste — if it exists in the codebase, reuse it; if it now exists twice, extract it.
- No god classes or catch-all managers; keep the module boundaries the codebase already has.
- No magic numbers — serialize tunables so they can be tuned in the Inspector.
- No empty or silent `catch`. Fail loudly or handle it properly.
- Names say what the thing *is*; no `Manager2`, `temp`, `doStuff`.

## Game design decisions — consult the Game Development Constitution

A vendored copy of the public **Game Development Constitution** (143 source-audited,
engine-agnostic principles across 24 domains, CC-BY-4.0) lives in
[docs/game-development-constitution/](docs/game-development-constitution/).

**Whenever the work touches a game design domain, consult it before proposing or
implementing the change.** That means any time the question is *what the game should do to
the player*, not just what the code should do — including:

- game feel, controls, responsiveness, camera, juice/polish (`FEEL`, `ANIM`)
- core loop, verbs, progression, difficulty, pacing (`DESIGN`, `SYS`, `BAL`, `PROTO`)
- levels, world layout, encounter and space design (`LEVEL`, `CONTENT`)
- UI, HUD, menus, onboarding, accessibility (`UX`)
- economy, loot, rewards, monetisation (`ECON`, `MON`)
- narrative, quests, dialogue (`NARR`)
- multiplayer design (as opposed to netcode mechanics) (`MP`)
- audio design (`AUDIO`)
- system architecture, performance and tech-direction tradeoffs (`ARCH`, `PROG`, `PERF`, `TECH`)
- playtesting, QA, production and shipping process (`PLAYTEST`, `QA`, `PROD`, `SHIP`, `TEAM`, `VISION`)

Pure plumbing with no design content — fixing a compile error, wiring a prefab reference,
renaming a field — does not need it.

### How to use it

1. Open [docs/game-development-constitution/INDEX.md](docs/game-development-constitution/INDEX.md)
   and pick the **1–5 principles** that bear on the decision (or grep `principles/` for
   terms). Never read all 143.
2. Read each selected file **in full**. The statement alone is not enough — `Applies when`,
   `Does not apply / Exceptions`, `Disagreement`, `depends_on` and `conflicts_with` are
   where the conditions live.
3. Apply the principle's stated scope *before* recommending anything.
4. **Cite the principle IDs** (e.g. `GDC-L1-FEEL-0002`) in the explanation, so the
   reasoning is checkable.
5. Distinguish `objective` / `contextual` / `stylistic` guidance, and surface recorded
   disagreement rather than flattening it into false consensus.
6. It is **decision support, not an oracle**. SpaceGame's own evidence — playtests,
   profiler numbers, what the team has already decided — overrides the corpus. When they
   conflict, say so explicitly instead of deferring to the principle.

Confidence is evidence strength (1–5), not certainty. Upstream and update instructions:
[docs/game-development-constitution/README.md](docs/game-development-constitution/README.md).
