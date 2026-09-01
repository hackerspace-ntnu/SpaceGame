#!/usr/bin/env python3
"""Validate and regenerate the SpaceGame documentation system.

    python3 tools/docs_check.py              # validate; exit 1 on any error
    python3 tools/docs_check.py --index      # regenerate docs/AI/INDEX.md, then validate
    python3 tools/docs_check.py --stale 90   # also warn on docs not updated in N days

Validates docs/AI/systems/*.md:
  - YAML frontmatter present, with the required keys and legal values
  - every Assets/ ProjectSettings/ Packages/ .claude/ tools/ path link resolves on disk
  - every sibling *.md link resolves
  - required section headings present, in order (real docs only, not stubs)
  - line budget (<=150) so docs stay readable in one context window

The frontmatter is the source of truth: INDEX.md is generated from it, never hand-edited.
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import re
import sys
import urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SYSTEMS = os.path.join(ROOT, "docs", "AI", "systems")
INDEX = os.path.join(ROOT, "docs", "AI", "INDEX.md")
ROUTING = os.path.join(ROOT, "docs", "AI", "ROUTING.md")

LAYERS = ["core", "world", "characters", "items", "vehicles", "presentation", "pipeline"]
LAYER_TITLES = {
    "core": "Core — engine glue, netcode, save, config",
    "world": "World — terrain, streaming, scenes, atmosphere",
    "characters": "Characters — player, creatures, locomotion, combat",
    "items": "Items — inventory, gadgets, interaction",
    "vehicles": "Vehicles — mounts, ships, aircraft",
    "presentation": "Presentation — UI, cutscenes, audio, modes",
    "pipeline": "Pipeline — art, editor tooling, tests",
}
REQUIRED = ["system", "layer", "summary", "paths", "symptoms", "reads_with", "updated"]

# The canonical shape. Docs that adopt a section must keep it in this order, but a doc
# whose subject genuinely has no "Key types" (a config or scene inventory, say) may omit
# one — only MANDATORY sections are required of every system doc.
SECTIONS = ["## Model", "## Key types", "## Flows", "## Multiplayer",
            "## Persistence", "## Gotchas", "## Extending"]
MANDATORY = ["## Gotchas", "## Extending"]
LINE_BUDGET = 150  # body only; frontmatter is not counted
LINK_ROOTS = ("Assets/", "ProjectSettings/", "Packages/", ".claude/", "tools/", "docs/")

# Files that are inventories or indexes, not system references.
EXEMPT = {"README.md", "audio-prefab-inventory.md", "CutsceneExamples.md"}


def parse_frontmatter(text: str):
    """Minimal YAML reader for the flat schema this corpus uses."""
    if not text.startswith("---\n"):
        return None, text
    end = text.find("\n---\n", 4)
    if end == -1:
        return None, text
    block, body = text[4:end], text[end + 5:]
    data, key = {}, None
    for raw in block.split("\n"):
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        if raw.startswith("  - ") or raw.startswith("- "):
            if key:
                data.setdefault(key, []).append(raw.split("- ", 1)[1].strip().strip('"\''))
            continue
        if ":" not in raw:
            continue
        key, _, val = raw.partition(":")
        key, val = key.strip(), val.strip()
        if val.startswith("[") and val.endswith("]"):
            inner = val[1:-1].strip()
            data[key] = [v.strip().strip('"\'') for v in inner.split(",") if v.strip()]
        elif val:
            data[key] = val.strip('"\'')
        else:
            data[key] = []
    return data, body


def check() -> tuple[list[str], list[str], dict]:
    errors: list[str] = []
    warnings: list[str] = []
    docs: dict[str, dict] = {}

    if not os.path.isdir(SYSTEMS):
        return [f"missing directory {SYSTEMS}"], [], {}

    names = sorted(f for f in os.listdir(SYSTEMS) if f.endswith(".md"))
    known = {os.path.splitext(n)[0] for n in names}

    # Redirect stubs are link targets, not reading companions: point reads_with past them.
    redirects = {}
    for n in names:
        head = open(os.path.join(SYSTEMS, n), encoding="utf-8").read(1200)
        m = re.search(r"^redirect_to:\s*(\S+)\s*$", head, re.M)
        if m:
            redirects[os.path.splitext(n)[0]] = m.group(1)

    for name in names:
        path = os.path.join(SYSTEMS, name)
        rel = os.path.relpath(path, ROOT)
        text = open(path, encoding="utf-8").read()
        nlines = text.count("\n") + 1

        fm, body = parse_frontmatter(text)
        exempt = name in EXEMPT
        stub = bool(fm and fm.get("redirect_to"))

        if not exempt:
            if fm is None:
                errors.append(f"{rel}: no YAML frontmatter")
            else:
                docs[name] = fm
                for key in REQUIRED:
                    if key not in fm or fm[key] in ("", [], None):
                        errors.append(f"{rel}: frontmatter missing '{key}'")
                if fm.get("layer") not in LAYERS:
                    errors.append(f"{rel}: layer '{fm.get('layer')}' not one of {LAYERS}")
                if len(str(fm.get("summary", ""))) > 110:
                    errors.append(f"{rel}: summary longer than 110 chars")
                for sib in fm.get("reads_with", []):
                    if sib not in known:
                        errors.append(f"{rel}: reads_with '{sib}' is not a doc")
                    elif sib in redirects:
                        errors.append(f"{rel}: reads_with '{sib}' is a redirect stub; "
                                      f"point it at '{redirects[sib]}'")
                for p in fm.get("paths", []):
                    if not os.path.exists(os.path.join(ROOT, p.rstrip("/"))):
                        errors.append(f"{rel}: paths entry does not exist: {p}")
                rt = fm.get("redirect_to")
                if rt and rt not in known:
                    errors.append(f"{rel}: redirect_to '{rt}' is not a doc")

            if not stub:
                blines = body.count("\n") + 1
                if blines > LINE_BUDGET:
                    errors.append(f"{rel}: {blines} body lines, budget is {LINE_BUDGET}")
                pos, prev = -1, None
                for sec in SECTIONS:
                    found = body.find("\n" + sec)
                    if found == -1:
                        if sec in MANDATORY:
                            errors.append(f"{rel}: missing required section '{sec}'")
                        continue
                    if found < pos:
                        errors.append(
                            f"{rel}: section '{sec}' appears before '{prev}'; "
                            f"canonical order is {' -> '.join(SECTIONS)}")
                    pos, prev = found, sec

        for m in re.finditer(r"\]\(([^)\s]+)\)", text):
            target = urllib.parse.unquote(m.group(1).split("#")[0])
            if not target or target.startswith(("http://", "https://", "mailto:")):
                continue
            if target.startswith(LINK_ROOTS) or (target.startswith(".") and "/" not in target):
                probe = os.path.join(ROOT, target.rstrip("/"))  # repo-root-relative, incl. dotfiles
            elif target.endswith(".md") and "/" not in target:
                probe = os.path.join(SYSTEMS, target)
            else:
                probe = os.path.join(SYSTEMS, target)
            if not os.path.exists(probe):
                errors.append(f"{rel}: broken link -> {target}")

    # The human page must describe every system, so neither half silently drifts.
    brief = os.path.join(ROOT, "docs", "Human", "the-systems.md")
    if os.path.isfile(brief):
        page = open(brief, encoding="utf-8").read()
        named = {n.lower() for n in re.findall(r"^### .*\*\((\w+)\)\*\s*$", page, re.M)}
        for n, fm in docs.items():
            if fm.get("redirect_to"):
                continue
            if os.path.splitext(n)[0].lower() not in named:
                errors.append(f"docs/Human/the-systems.md: no entry for '{os.path.splitext(n)[0]}'")

    for name, fm in docs.items():
        try:
            when = dt.date.fromisoformat(str(fm.get("updated", "")))
        except ValueError:
            errors.append(f"docs/AI/systems/{name}: 'updated' is not YYYY-MM-DD")
            continue
        docs[name]["_age"] = (dt.date.today() - when).days

    return errors, warnings, docs


def cell(s: str) -> str:
    """Make a string safe inside a markdown table cell."""
    return str(s).replace("|", "\\|").replace("\n", " ").strip()


def build_index(docs: dict) -> str:
    live = {n: fm for n, fm in docs.items() if not fm.get("redirect_to")}
    stubs = {n: fm for n, fm in docs.items() if fm.get("redirect_to")}

    out: list[str] = []
    w = out.append
    w("<!-- GENERATED by tools/docs_check.py --index. Do not edit by hand. -->")
    w("<!-- Edit the frontmatter in docs/AI/systems/*.md and regenerate. -->")
    w("")
    w("# SpaceGame — AI Documentation Index")
    w("")
    w("Routing layer for the system reference in [systems/](systems/). Read this file, pick the")
    w("one or two docs that match, read those in full. Do not read the whole corpus.")
    w("")
    w("Every doc has the same shape: **Model → Key types → Flows → Multiplayer → Persistence →")
    w("Gotchas → Extending**. `Gotchas` records the silent failures; read it before editing.")
    w("Source links inside the docs are paths relative to the repo root.")
    w("")
    w("Companions: [GLOSSARY.md](GLOSSARY.md) · [INVARIANTS.md](INVARIANTS.md) · "
      "[DEFECTS.md](DEFECTS.md) · [CONTRIBUTING.md](CONTRIBUTING.md)")
    w("")
    w("## Route by symptom")
    w("")
    w("[ROUTING.md](ROUTING.md) maps what you are *seeing* to the doc that explains it.")
    w("**Grep it, do not read it** — it is a long lookup table, not prose:")
    w("")
    w("```")
    w("grep -i 'client' docs/AI/ROUTING.md")
    w("```")
    w("")
    w("## Route by system")
    w("")
    for layer in LAYERS:
        members = sorted((n, fm) for n, fm in live.items() if fm.get("layer") == layer)
        if not members:
            continue
        w(f"### {LAYER_TITLES[layer]}")
        w("")
        w("| Doc | Covers | Read with |")
        w("| --- | --- | --- |")
        for name, fm in members:
            label = os.path.splitext(name)[0]
            rw = ", ".join(f"[{s}](systems/{s}.md)" for s in fm.get("reads_with", [])) or "—"
            w(f"| [{label}](systems/{name}) | {cell(fm.get('summary',''))} | {rw} |")
        w("")
    w("## Route by path")
    w("")
    w("Which doc governs the code you are about to change — also in [ROUTING.md](ROUTING.md),")
    w("grep-shaped, longest match wins:")
    w("")
    w("```")
    w("grep 'Scripts/Core/Multiplayer' docs/AI/ROUTING.md")
    w("```")
    w("")
    if stubs:
        w("## Redirects")
        w("")
        w("Old names kept so existing links resolve. Each points at the doc that absorbed it.")
        w("")
        for name, fm in sorted(stubs.items()):
            rt = fm["redirect_to"]
            w(f"- [{os.path.splitext(name)[0]}](systems/{name}) → [{rt}](systems/{rt}.md)")
        w("")
    w("## Not system references")
    w("")
    w("- [systems/audio-prefab-inventory.md](systems/audio-prefab-inventory.md) — generated audio "
      "slot inventory")
    w("- [systems/CutsceneExamples.md](systems/CutsceneExamples.md) — example prefab list")
    w("")
    w(f"<!-- {len(live)} system docs, {len(stubs)} redirects -->")
    return "\n".join(out) + "\n"


def build_routing(docs: dict) -> str:
    live = {n: fm for n, fm in docs.items() if not fm.get("redirect_to")}
    symptoms, paths = [], []
    for name, fm in live.items():
        label = os.path.splitext(name)[0]
        for s in fm.get("symptoms", []):
            symptoms.append((s, label, name))
        for p in fm.get("paths", []):
            paths.append((p, label, name))

    out: list[str] = []
    w = out.append
    w("<!-- GENERATED by tools/docs_check.py --index. Do not edit by hand. -->")
    w("<!-- Add symptoms/paths to the frontmatter of docs/AI/systems/*.md and regenerate. -->")
    w("")
    w("# Routing tables")
    w("")
    w("**Grep this file, do not read it.** Two lookup tables sized for `grep`, not for a")
    w("context window. Match, then read that one doc in full.")
    w("")
    w("```")
    w("grep -i '<a word from the failure>' docs/AI/ROUTING.md   # what am I seeing?")
    w("grep 'Scripts/Items/Artifacts'      docs/AI/ROUTING.md   # what governs this code?")
    w("```")
    w("")
    w("No match is meaningful: no doc claims it. Fall back to [INDEX.md](INDEX.md), then add")
    w("the symptom once you have the answer — see [CONTRIBUTING.md](CONTRIBUTING.md).")
    w("")
    w("## Symptom → doc")
    w("")
    w("| Symptom | Read |")
    w("| --- | --- |")
    for s, label, name in sorted(symptoms, key=lambda r: r[0].lower()):
        w(f"| {cell(s)} | [{label}](systems/{name}) |")
    w("")
    w("## Path → doc")
    w("")
    w("Longest match wins.")
    w("")
    w("| Path | Read |")
    w("| --- | --- |")
    for p, label, name in sorted(paths, key=lambda r: (-len(r[0]), r[0])):
        w(f"| `{cell(p)}` | [{label}](systems/{name}) |")
    w("")
    w(f"<!-- {len(symptoms)} symptoms, {len(paths)} paths, {len(live)} docs -->")
    return "\n".join(out) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--index", action="store_true", help="regenerate docs/AI/INDEX.md")
    ap.add_argument("--stale", type=int, metavar="DAYS",
                    help="warn on docs whose 'updated' is older than DAYS")
    args = ap.parse_args()

    errors, warnings, docs = check()

    if args.stale:
        for name, fm in sorted(docs.items()):
            if fm.get("_age", 0) > args.stale:
                warnings.append(f"docs/AI/systems/{name}: {fm['_age']} days since 'updated'")

    if args.index:
        if any("frontmatter" in e or "layer" in e for e in errors):
            print("refusing to generate INDEX.md while frontmatter is invalid\n", file=sys.stderr)
        else:
            open(INDEX, "w", encoding="utf-8").write(build_index(docs))
            open(ROUTING, "w", encoding="utf-8").write(build_routing(docs))
            print(f"wrote {os.path.relpath(INDEX, ROOT)}")
            print(f"wrote {os.path.relpath(ROUTING, ROOT)}")

    for wmsg in warnings:
        print(f"warn:  {wmsg}")
    for e in errors:
        print(f"ERROR: {e}", file=sys.stderr)

    live = sum(1 for f in docs.values() if not f.get("redirect_to"))
    print(f"\n{live} system docs, {len(docs) - live} redirects, "
          f"{len(errors)} errors, {len(warnings)} warnings")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
