# Contributing to the AI docs

Every change to SpaceGame updates this corpus in the same commit as the code. A doc that
describes code that no longer exists is worse than no doc — agents trust it and act on it.

## The one-line rule

**If you changed behaviour, you change the doc. Same commit, no exceptions.**

## Which doc?

```bash
grep 'Scripts/Items/Artifacts' docs/AI/ROUTING.md    # path  -> doc
grep -i 'client'               docs/AI/ROUTING.md    # symptom -> doc
```

If nothing matches, the code you touched is undocumented — write a new doc (below).

## When you change existing code

1. Open the doc that governs the path you changed.
2. Update the affected rows. **Delete anything your change made untrue** — stale rows are the
   failure mode this corpus exists to prevent.
3. If you were bitten by something non-obvious, add it to `## Gotchas`. That section is the
   single highest-value part of every doc; it is the reason the next agent does not repeat you.
4. If the trap applies to three or more subsystems, add it to [INVARIANTS.md](INVARIANTS.md)
   instead, and reference it.
5. If you fixed something listed in [DEFECTS.md](DEFECTS.md), remove that row.
6. Bump `updated:` in the frontmatter to today.
7. Add a `symptoms:` entry for anything that cost you real time to diagnose — phrased as what
   you *saw*, not as a topic. That entry is how the next agent finds this doc in one grep.
8. Regenerate and validate:
   ```bash
   python3 tools/docs_check.py --index
   ```

## When you add a new system

1. Create `docs/AI/systems/<System>.md`.
2. Frontmatter first — it is the source of truth the index is generated from:
   ```yaml
   ---
   system: MySystem                       # PascalCase, matches the filename
   layer: items                           # core|world|characters|items|vehicles|presentation|pipeline
   summary: One line, <=110 chars, what this system is
   paths:
     - Assets/Game/Scripts/Items/MySystem/    # must exist on disk
   symptoms:
     - "the thing does nothing for a client"  # what someone SEES, not a topic label
   reads_with: [Inventory, Multiplayer]    # sibling docs, no .md
   updated: 2026-09-01
   ---
   ```
3. Body sections, in this order. `## Gotchas` and `## Extending` are required; omit another
   only when the subject genuinely has none, and say `N/A — <reason>` rather than dropping it
   silently:

   **Model** → **Key types** → **Flows** → **Multiplayer** → **Persistence** → **Gotchas** → **Extending**

4. Add a matching entry to [docs/Human/the-systems.md](../Human/the-systems.md) — a plain-language
   heading `### <Readable name> *(<YourDocName>)*`, two to four sentences, and one
   **Worth knowing** line. **This is enforced**: `docs_check.py` fails if a system doc has no
   entry there.
5. Add the doc to a Human chapter's "Where this lives" list if a person would ever care.
6. Regenerate the index and validate.

## House style

These docs are read by agents under a token budget. Optimise for that.

- **150 body lines, hard cap.** Over budget means the system wants splitting, not smaller type.
- Tables and tight bullets over prose. No ASCII diagrams unless one replaces ten lines.
- **Every claim comes from source you read.** Never from an older doc, a memory, or inference.
  The pre-2026-09 docs in this folder were substantially wrong precisely because they were
  maintained by editing prose instead of re-reading code.
- Link every type and file, **path relative to the repo root** —
  `[NetArg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetArg.cs)`. Sibling docs are bare
  filenames — `[Inventory.md]` followed by `(Inventory.md)`, no directory prefix.
- Write what an agent needs to *change code correctly*: the authority split, the invariants,
  what breaks silently. Not what the code obviously says.
- Record defects plainly where you find them. An honest "this is broken" is useful; a doc that
  describes the intended design as if it worked is a trap.

## Validation

```bash
python3 tools/docs_check.py             # validate; non-zero exit on any error
python3 tools/docs_check.py --index     # regenerate INDEX.md + ROUTING.md, then validate
python3 tools/docs_check.py --stale 90  # also list docs untouched for 90+ days
```

It checks frontmatter completeness and legal values, that every `paths:` entry and every link
resolves on disk, section presence and order, the line budget, that `reads_with` points at live
docs rather than redirect stubs, and that every system has an entry in
[the human systems page](../Human/the-systems.md).

**INDEX.md and ROUTING.md are generated. Never hand-edit them** — edit frontmatter and rerun.

## The human half

[docs/Human/](../Human/) is the same knowledge written for people: narrative, conceptual, almost
no filenames. Two parts:

- **[the-systems.md](../Human/the-systems.md)** — one short entry per system. Every system doc
  must have one, and the validator enforces it. Keep its **Worth knowing** line true; that is the
  line people actually remember.
- **The ten chapters** — thematic and narrative. Not a duplicate to keep in sync line by line.
  Update one when the *shape* of a system changes — a new mode of play, a reversed decision, a
  subsystem that no longer exists — not when a type is renamed.
