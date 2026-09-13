# Game Development Constitution (vendored)

A vendored copy of the public **Game Development Constitution** — 143 engine-agnostic,
source-audited game development principles across 24 domains.

- Upstream: https://github.com/adventuresincausality/game-development-constitution
- Site: https://adventuresincausality.com
- Version: public edition **v1.1.0**, corpus generated 2026-07-20,
  `contentSha256` in [manifest.json](manifest.json)
- Licence: CC-BY-4.0 (see [LICENSE](LICENSE), attribution in [CITATION.cff](CITATION.cff))

## Start here

**[INDEX.md](INDEX.md)** — the routing table. All 143 principles by domain, with title,
subdomain, type and confidence. Use it to pick which principles to read; never read all
143.

## How to use it (short version)

1. Translate the problem into a domain (`FEEL`, `SYS`, `LEVEL`, `UX`, `MP`, …) and search
   terms.
2. Pick **1–5 principles** from [INDEX.md](INDEX.md), or grep `principles/` for terms.
3. Read each selected file **completely**. The statement alone is not enough — the
   conditions live in `Applies when`, `Does not apply / Exceptions`, `Disagreement`, and
   `depends_on` / `conflicts_with`.
4. Apply the principle's scope before recommending anything, then cite the principle ID.

Full retrieval procedure: [AI_START_HERE.md](AI_START_HERE.md).
Citation and answer rules: [AI-USAGE.md](AI-USAGE.md).

## What it is and isn't

- It is **decision support**, not an oracle. `type: objective` means well-evidenced,
  `contextual` means it depends on the game, `stylistic` means taste. `confidence` is
  evidence strength (1–5), not certainty.
- Project evidence — what our own playtests, profiler and players show — **overrides** the
  corpus. When they conflict, say so explicitly rather than deferring to the principle.
- It is engine-agnostic. Upstream also ships 30 Unreal Engine 5.8 skills; those are
  **deliberately not vendored** — SpaceGame is Unity, and our own engine-specific
  knowledge lives in [docs/architecture/](../architecture/) and `.claude/skills/`.

## Contents

| Path | What |
| --- | --- |
| [INDEX.md](INDEX.md) | Routing table over all 143 principles (generated) |
| [principles/](principles/) | The 143 canonical Markdown records with YAML front matter |
| [sources.json](sources.json) | Resolves `S-*` source keys cited in principles |
| [manifest.json](manifest.json) | Version, counts, schema, integrity hash |
| [AI_START_HERE.md](AI_START_HERE.md) | Upstream retrieval procedure (verbatim) |
| [AI-USAGE.md](AI-USAGE.md) | Upstream usage and citation rules (verbatim) |

`AI_START_HERE.md` and `AI-USAGE.md` are upstream files kept verbatim, so some paths they
mention (`public/data/*`, `skills/`, `UNREAL_AI_START_HERE.md`) do not exist in this
vendored subset. Use `principles/` and `INDEX.md` instead.

## Updating

Re-download the corpus and regenerate the index:

```sh
curl -sL -o /tmp/gdc.tar.gz \
  https://codeload.github.com/adventuresincausality/game-development-constitution/tar.gz/refs/heads/main
tar xzf /tmp/gdc.tar.gz -C /tmp game-development-constitution-main/content \
  game-development-constitution-main/AI-USAGE.md \
  game-development-constitution-main/AI_START_HERE.md \
  game-development-constitution-main/LICENSE \
  game-development-constitution-main/CITATION.cff
rm -rf docs/game-development-constitution/principles
cp -R /tmp/game-development-constitution-main/content/principles docs/game-development-constitution/
cp /tmp/game-development-constitution-main/content/sources.json \
   /tmp/game-development-constitution-main/{AI-USAGE.md,AI_START_HERE.md,LICENSE,CITATION.cff} \
   docs/game-development-constitution/
curl -sL -o docs/game-development-constitution/manifest.json \
  https://adventuresincausality.com/data/manifest.json
python3 docs/game-development-constitution/make_index.py
```
