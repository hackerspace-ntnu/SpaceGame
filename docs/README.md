# SpaceGame documentation

Two audiences, two folders, same knowledge. Pick by who is reading.

| You are | Go to |
| --- | --- |
| A person who wants to **understand** the game | [Human/](Human/) — narrative chapters, read in order or dip in |
| An AI agent about to **change code** | [AI/INDEX.md](AI/INDEX.md) — routing layer over 33 system references |
| Deciding what the game *should do* to the player | [game-development-constitution/](game-development-constitution/) |

## Human/

Written for a new teammate, an artist, a designer, or a returning contributor. Prose, concepts,
and the *why*. Deliberately almost free of filenames and type names — it explains the shape of
the game, not its API.

- [Human/the-systems.md](Human/the-systems.md) — **every system in a few sentences each.** Look
  up a name you heard, get the gist and the one thing worth knowing about it.
- Ten narrative chapters, in order, starting at [Human/01-the-game.md](Human/01-the-game.md).

Each chapter ends with pointers into `AI/systems/` for anyone who then wants the technical detail.

## AI/

A retrieval-optimised reference corpus. One doc per subsystem, all the same shape, every claim
read off current source, every type and file linked. Built to be routed into cheaply rather
than read end to end.

| File | Purpose |
| --- | --- |
| [AI/INDEX.md](AI/INDEX.md) | The map. Read this, pick one or two docs, read those in full. Generated. |
| [AI/ROUTING.md](AI/ROUTING.md) | Symptom → doc and path → doc lookup tables. **Grep, do not read.** Generated. |
| [AI/INVARIANTS.md](AI/INVARIANTS.md) | The rules that hold across every subsystem. Read once, before touching anything. |
| [AI/GLOSSARY.md](AI/GLOSSARY.md) | Project vocabulary → what it is → which doc. Resolves ambiguous nouns in one lookup. |
| [AI/DEFECTS.md](AI/DEFECTS.md) | Known-broken things, verified and unfixed. Check before debugging. |
| [AI/CONTRIBUTING.md](AI/CONTRIBUTING.md) | How to update and extend this corpus. |
| [AI/systems/](AI/systems/) | The 33 subsystem references plus 6 redirect stubs. |

## Keeping it true

Documentation changes ship in the same commit as the code they describe. That rule is in
[CLAUDE.md](../CLAUDE.md) and the details are in [AI/CONTRIBUTING.md](AI/CONTRIBUTING.md).

`INDEX.md` and `ROUTING.md` are generated from the YAML frontmatter of the system docs — edit
frontmatter, never those two files:

```bash
python3 tools/docs_check.py             # validate: frontmatter, links, sections, line budget
python3 tools/docs_check.py --index      # regenerate INDEX.md + ROUTING.md, then validate
python3 tools/docs_check.py --stale 90   # list docs untouched for 90+ days
```

## Also here

- [superpowers/](superpowers/) — historical design specs and implementation plans, kept as a
  record of decisions. They are **not** maintained; where one disagrees with `AI/systems/`,
  the system doc is right.
- [game-development-constitution/](game-development-constitution/) — a vendored corpus of 143
  engine-agnostic game design principles, consulted for design decisions.
