import os, re, json

"""Regenerate INDEX.md from the vendored principles/ front matter. See README.md."""

D = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(D, "principles")

DOMAIN_NAMES = {
 "ANIM": "Animation", "ARCH": "Software architecture", "AUDIO": "Audio", "BAL": "Balance",
 "CONTENT": "Content & procedural generation", "DESIGN": "Core design", "ECON": "Economy",
 "FEEL": "Game feel", "LEVEL": "Level design", "MON": "Monetisation", "MP": "Multiplayer",
 "NARR": "Narrative", "PERF": "Performance", "PLAYTEST": "Playtesting", "PROD": "Production",
 "PROG": "Programming", "PROTO": "Prototyping", "QA": "QA", "SHIP": "Shipping",
 "SYS": "Systems design", "TEAM": "Team", "TECH": "Tech direction", "UX": "UX & UI",
 "VISION": "Vision",
}


def scalar(fm, key):
    m = re.search(r"^%s:\s*(.+)$" % key, fm, re.M)
    return m.group(1).strip() if m else ""


def listval(fm, key):
    m = re.search(r"^%s:\s*\n((?:\s+-\s+.+\n)+)" % key, fm, re.M)
    if not m:
        return []
    return [line.strip()[2:].strip() for line in m.group(1).splitlines()]


def parse(name):
    raw = open(os.path.join(P, name), encoding="utf-8").read()
    fm = raw.split("---", 2)[1]
    return dict(
        id=scalar(fm, "id"), title=scalar(fm, "title"), domain=scalar(fm, "domain"),
        subdomain=scalar(fm, "subdomain"), type=scalar(fm, "type"),
        confidence=scalar(fm, "confidence"), tags=listval(fm, "tags"), file=name,
    )


names = sorted(n for n in os.listdir(P) if n.endswith(".md"))
recs = list(map(parse, names))

manifest = json.load(open(os.path.join(D, "manifest.json"), encoding="utf-8"))

by_domain = {}
list(map(lambda r: by_domain.setdefault(r["domain"], []).append(r), recs))

out = []
out.append("# Game Development Constitution - routing index\n")
out.append(
    "%d principles across %d domains. Public edition v%s (corpus generated %s)."
    % (len(recs), len(by_domain), manifest["version"], manifest["generatedAt"][:10])
)
out.append("")
out.append(
    "Use this table to pick the **1-5 principles that actually bear on the decision**, then read "
    "those files in full from [`principles/`](principles/). The titles below are lossy: the load-"
    "bearing parts of a principle are its `Applies when`, `Does not apply / Exceptions` and "
    "`Disagreement` sections. Retrieval procedure: [`AI_START_HERE.md`](AI_START_HERE.md). "
    "Citation and usage rules: [`AI-USAGE.md`](AI-USAGE.md)."
)
out.append("")
out.append(
    "`type` is `objective` (well-evidenced), `contextual` (depends on the game) or `stylistic` "
    "(taste). `conf` is evidence strength 1-5, not certainty."
)
out.append("")
out.append("## Domains")
out.append("")
out.append("| Domain | Covers | Count |")
out.append("| --- | --- | --- |")


def domain_row(dom):
    out.append("| `%s` | %s | %d |" % (dom, DOMAIN_NAMES.get(dom, dom), len(by_domain[dom])))


list(map(domain_row, sorted(by_domain, key=lambda d: (-len(by_domain[d]), d))))
out.append("")


def principle_row(r):
    out.append("| [%s](principles/%s) | %s | %s | %s | %s |" % (
        r["id"], r["file"], r["title"].replace("|", "\\|"), r["subdomain"],
        r["type"], r["confidence"]))


def domain_section(dom):
    out.append("## %s - %s" % (dom, DOMAIN_NAMES.get(dom, dom)))
    out.append("")
    out.append("| ID | Title | Subdomain | Type | Conf |")
    out.append("| --- | --- | --- | --- | --- |")
    list(map(principle_row, sorted(by_domain[dom], key=lambda r: r["id"])))
    out.append("")


list(map(domain_section, sorted(by_domain)))

text = "\n".join(out) + "\n"
open(os.path.join(D, "INDEX.md"), "w", encoding="utf-8").write(text)
print("wrote INDEX.md: %d chars, %d records, %d domains" % (len(text), len(recs), len(by_domain)))
