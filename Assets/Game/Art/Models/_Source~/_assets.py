"""Make the whole library browsable in Blender's Asset Browser.

    python _assets.py [--blender <path-to-blender.exe>] [--only <relative/path.blend> ...]

COMPONENTS. Every file under `components/` holds one part FAMILY, with its VARIATIONS as
`Coll_<Family>_<Variant>` collections (`Coll_<Family>` alone is a family of one). Each is marked
as an asset in catalogue `<Category>/<Family>` (category = the folder under components/). A
collection named any other way (older hand-made kits such as nomad_settlement/components.blend)
is marked too, in catalogue `<Category>/<File Name>`; `Ref_*` scale references are not.

MODELS. Every file under `models/` (bar SKIP_MODELS: backups, copies, scratch saves) becomes one
asset, `Model_<Name>`, in catalogue `Models/<Category>` — drag it in and you get the whole model.
That collection *contains* the scene's top-level collections and links its loose objects; it is
not linked into the scene, so the file's layout and exports are unchanged. Kit files
(KIT_MODELS) skip the whole-model asset: they are shelves of parts, not one thing.
Each named part of a model — a `Coll_*` collection — is marked as well, in catalogue
`Models/<Category>/<Model Name>`, unless it is:
  - a numbered copy (`.001`), a rig, markers, collision or a `Coll_Ref_*` reference;
  - the model itself (`Coll_DuneBarge` in dune_barge.blend), or the file's only part;
  - already claimed by an earlier file — components first, then KIT_MODELS, then the rest in
    path order — so a part copied into five settlements is one asset, not five.
The plan is derived from library_index.json, so run `_index_library.py` first.

CHARACTERS. The finished drifters listed in CHARACTERS are marked one asset each (body, rig and
eyes) in catalogue `Characters/Drifters`. A file in CHARACTER_FILES holds several finished
characters, each already in its own `Char_<Name>` collection (body and rig); every such
collection is marked, in that file's catalogue.

Collections someone already marked into a catalogue of a NESTED `blender_assets.cats.txt`
(models/buildings/ has one, for the decoration kits) are left alone, and those catalogues are
merged into the root file: Blender reads catalogues only from the library root, so without the
merge those assets would sit in "Unassigned".

Dual-purpose like _index_library.py: run under plain Python it drives Blender over each file;
run inside Blender (`--background <file> --python _assets.py -- --mark <category>`) it marks that
file. Re-running is safe: marking is idempotent and only asset metadata changes — no geometry.

Catalogue UUIDs are derived from the catalogue path (uuid5), so every run and every machine
agrees on them without a registry.
"""

import json
import os
import re
import subprocess
import sys
import uuid

LIB_ROOT = os.path.dirname(os.path.abspath(__file__))
COMPONENTS = os.path.join(LIB_ROOT, "components")
INDEX_FILE = os.path.join(LIB_ROOT, "library_index.json")
CATS_NAME = "blender_assets.cats.txt"
CATS_FILE = os.path.join(LIB_ROOT, CATS_NAME)
NAMESPACE = uuid.UUID("6f1c2a52-4d1e-4b8a-9d3a-5a0e7c2b9f10")   # fixed: stable catalogue ids
COLL = re.compile(r"^Coll_([A-Za-z0-9]+)(?:_([A-Za-z0-9_]+))?$")
REF_PREFIX = "Ref_"                                              # scale references, never assets

# Finished characters, file -> asset name. Not discovered by a rule: models/characters also holds
# byte-identical copies (sculpt_base/ duplicates drifters/) and scratch saves (gary-kopi), and
# every one of these files names its own collection Coll_HumanSculptBase. Each gets a
# `Char_<name>` collection that LINKS its body, rig and eyes; nothing is moved, and the new
# collection is not linked into the scene, so the file's own layout and exports are unchanged.
CHARACTERS = {
    "models/characters/drifters/alien_rigged.blend": "Alien",
    "models/characters/drifters/crumpy_rigged.blend": "Crumpy",
    "models/characters/drifters/gary.blend": "Gary",
    "models/characters/drifters/human_sculpt_base_rigged.blend": "HumanSculptBase",
    "models/characters/drifters/raxy.blend": "Raxy",
    "models/characters/drifters/raxy_classic.blend": "RaxyClassic",
}
CHARACTER_CATALOG = "Characters/Drifters"

# Files holding several finished characters, each in its own Char_<Name> collection: file -> catalogue.
CHARACTER_FILES = {
    "models/vehicles/strider_characters.blend": "Characters/Striders",
}
CHARACTER_FILE_PREFIX = "Char_"
CHARACTER_BODY_COLLECTION = "Coll_HumanSculptBase"   # body + rig, in every character file
CHARACTER_EYE_PREFIX = "Sphere"                      # the eyes sit loose in the scene

# models/ files that are not assets of their own: backups, copies and scratch saves of a file
# that is marked, plus the drifter folders CHARACTERS already covers.
SKIP_MODELS = re.compile(r"""^models/(
      characters/drifters/                          # CHARACTERS
    | characters/sculpt_base/                       # byte-identical copies of drifters/
    | characters/nomad/(astronaut\ \(1\)|nomad_backup_.*|nomad_before_.*|nomad_pre_.*)\.blend
    | buildings/Untitled\.blend                     # a copy of Raxy_outpost.blend
    | vehicles/dune_barge_(before_recovery_2052|track_snapshot)\.blend
    | vehicles/.*_copy\.blend
)""", re.X)

# Shelves of parts: their parts are assets, the file as a whole is not. Listed first in the claim
# order, so a part copied from a kit into a settlement is filed under the kit.
KIT_MODELS = [
    "models/buildings/decorations.blend",
    "models/buildings/astronaut_decorations.blend",
    "models/buildings/nomad_terrace_kit.blend",
    "models/buildings/nomad_settlement_decoration.blend",
]
MODEL_PREFIX = "Model_"
NOT_A_PART = re.compile(r"(\.\d{3}|_Rig$|_Markers$|_Collision$|_TraversalSources$|^Coll_Ref_)")

# Rendering a preview of a collection this big runs background Blender out of memory and crashes it
# before the file is saved (nomad_settlement.blend's whole-model asset: 11k objects). Above it the
# asset is marked without a preview.
PREVIEW_MAX_OBJECTS = 2000

DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"


def words(camel):
    return re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", camel)


def camel(stem):
    return "".join(p[:1].upper() + p[1:] for p in re.split(r"[_\-\s]+", stem) if p)


def catalog_path(category, family):
    return "%s/%s" % (words(category.title()), words(family))


def catalog_id(path):
    return str(uuid.uuid5(NAMESPACE, path))


def model_category(rel):
    return rel.split("/")[1].title()


def model_name(rel):
    return camel(os.path.splitext(os.path.basename(rel))[0])


# ── catalogues ───────────────────────────────────────────────────────────────

def read_cats(path):
    """{catalogue path: full definition line} of one catalogue file."""
    with open(path, encoding="utf-8") as fh:
        return {l.split(":")[1]: l.rstrip("\n") for l in fh
                if l.count(":") >= 2 and not l.startswith("#")}


def foreign_catalogs():
    """Catalogues defined in nested cats files, by path: kept verbatim, with their own UUIDs."""
    found = {}
    for root, _, files in os.walk(LIB_ROOT):
        if CATS_NAME in files and os.path.abspath(root) != LIB_ROOT:
            found.update(read_cats(os.path.join(root, CATS_NAME)))
    return found


# ── the plan, shared by the driver and the in-Blender marker ─────────────────

def model_files():
    return sorted(rel for rel in index() if rel.startswith("models/") and not SKIP_MODELS.match(rel)
                  and rel not in CHARACTER_FILES)


_index = None


def index():
    global _index
    if _index is None:
        with open(INDEX_FILE, encoding="utf-8") as fh:
            _index = json.load(fh)
    return _index


def collection_names(rel):
    return [c["name"] for c in index()[rel]["collections"]]   # every collection, nested or not


def model_parts():
    """{model file: [part collection names]} — see the module docstring for the rules."""
    claimed = {n for rel in index() if rel.startswith("components/") for n in collection_names(rel)}
    order = [r for r in KIT_MODELS if r in model_files()] + \
            [r for r in model_files() if r not in KIT_MODELS]
    plan = {}
    for rel in order:
        itself = model_name(rel).lower()
        candidates = [n for n in collection_names(rel) if n.startswith("Coll_")
                      and not NOT_A_PART.search(n) and n != CHARACTER_BODY_COLLECTION
                      and n[len("Coll_"):].replace("_", "").lower() != itself]
        parts = [n for n in candidates if n not in claimed]
        if rel not in KIT_MODELS and len(candidates) == 1:
            parts = []                       # the file's only part is the model itself
        claimed.update(parts)
        plan[rel] = parts
    return plan


# ── inside Blender ───────────────────────────────────────────────────────────

def mark_asset(coll, path, description, tags, marked, no_preview):
    if coll.asset_data is None:
        coll.asset_mark()
    coll.asset_data.catalog_id = catalog_id(path)
    coll.asset_data.description = description
    for tag in tags:
        if tag not in [t.name for t in coll.asset_data.tags]:
            coll.asset_data.tags.new(tag)
    if len(coll.all_objects) > PREVIEW_MAX_OBJECTS:
        no_preview.append("%s (%d objects, over PREVIEW_MAX_OBJECTS)" % (coll.name, len(coll.all_objects)))
        marked.append("%s	%s" % (coll.name, path))
        return
    try:
        coll.asset_generate_preview()
    except RuntimeError as err:          # background Blender may have no GPU context
        no_preview.append("%s (%s)" % (coll.name, err))
    marked.append("%s\t%s" % (coll.name, path))


def marked_elsewhere(coll, foreign_ids):
    return coll.asset_data is not None and coll.asset_data.catalog_id in foreign_ids


def save_and_report(marked, no_preview, kept=()):
    import bpy
    bpy.ops.wm.save_mainfile()
    for line in marked:
        print("MARKED\t" + line)
    for name in kept:
        print("KEPT\t" + name)
    for line in no_preview:
        print("NOPREVIEW\t" + line)


def mark_open_file(category):
    import bpy
    stem = os.path.splitext(os.path.basename(bpy.data.filepath))[0]
    marked, no_preview = [], []
    for coll in bpy.data.collections:
        if coll.name.startswith(REF_PREFIX) or not (coll.objects or coll.children):
            continue
        m = COLL.match(coll.name)
        if m:
            family, variant = m.group(1), m.group(2) or m.group(1)
            path = catalog_path(category, family)
        else:
            family, variant = camel(stem), coll.name
            path = catalog_path(category, family)
        mark_asset(coll, path, "%s — %s variation (%s)" % (words(family), words(variant), category),
                   (category, family, variant), marked, no_preview)
    save_and_report(marked, no_preview)


def mark_open_model(rel):
    import bpy
    foreign_ids = {line.split(":")[0] for line in foreign_catalogs().values()}
    category, name = model_category(rel), model_name(rel)
    marked, no_preview, kept = [], [], []
    if rel not in KIT_MODELS:
        whole = bpy.data.collections.get(MODEL_PREFIX + name) or bpy.data.collections.new(MODEL_PREFIX + name)
        scene = bpy.context.scene.collection
        for child in scene.children:
            if child != whole and child.name not in whole.children:
                whole.children.link(child)
        for obj in scene.objects:
            if obj.type not in ("CAMERA", "LIGHT") and obj.name not in whole.objects:
                whole.objects.link(obj)
        mark_asset(whole, "Models/" + category, "%s — the whole model (%s)" % (words(name), rel),
                   ("Model", category, name), marked, no_preview)
    for part in model_parts()[rel]:
        coll = bpy.data.collections.get(part)
        if coll is None:
            raise SystemExit("library_index.json lists %s in %s, the file does not — re-run "
                             "_index_library.py" % (part, rel))
        if not (coll.objects or coll.children):
            continue
        if marked_elsewhere(coll, foreign_ids):
            kept.append(part)
            continue
        label = part[len("Coll_"):]
        mark_asset(coll, "Models/%s/%s" % (category, words(name)),
                   "%s — part of %s" % (words(label.replace("_", " ")), words(name)),
                   ("Part", category, name), marked, no_preview)
    save_and_report(marked, no_preview, kept)


def mark_open_character(name):
    import bpy
    body = bpy.data.collections.get(CHARACTER_BODY_COLLECTION)
    if body is None:
        raise SystemExit("no %s collection in %s" % (CHARACTER_BODY_COLLECTION, bpy.data.filepath))
    eyes = [o for o in bpy.context.scene.collection.objects if o.name.startswith(CHARACTER_EYE_PREFIX)]
    coll = bpy.data.collections.get("Char_" + name) or bpy.data.collections.new("Char_" + name)
    for obj in list(body.objects) + eyes:
        if obj.name not in coll.objects:
            coll.objects.link(obj)
    marked, no_preview = [], []
    mark_asset(coll, CHARACTER_CATALOG, "%s — rigged drifter: body, rig and eyes" % words(name),
               ("Character", "Drifter", name), marked, no_preview)
    save_and_report(marked, no_preview)


def mark_open_character_file(catalog):
    import bpy
    marked, no_preview = [], []
    for coll in bpy.data.collections:
        if not coll.name.startswith(CHARACTER_FILE_PREFIX):
            continue
        name = coll.name[len(CHARACTER_FILE_PREFIX):]
        mark_asset(coll, catalog, "%s — rigged character: body and rig" % words(name),
                   ("Character", catalog.split("/")[-1], name), marked, no_preview)
    save_and_report(marked, no_preview)


# ── driver ───────────────────────────────────────────────────────────────────

def component_files(only):
    for root, _, files in os.walk(COMPONENTS):
        for f in sorted(files):
            if f.endswith(".blend"):
                rel = os.path.relpath(os.path.join(root, f), LIB_ROOT).replace("\\", "/")
                if not only or rel in only:
                    yield rel


def write_catalogs(paths, foreign):
    lines = ["# This is an Asset Catalog Definition file for Blender.",
             "# Generated by _assets.py from components/, models/, CHARACTERS and the nested",
             "# catalogue files it merges in — do not hand-edit.",
             "", "VERSION 1", ""]
    parents = set()
    for p in paths:
        parts = p.split("/")
        for i in range(1, len(parts)):
            parents.add("/".join(parts[:i]))
    for p in sorted((set(paths) | parents) - set(foreign)):
        lines.append("%s:%s:%s" % (catalog_id(p), p, p.replace("/", "-")))
    lines += [foreign[p] for p in sorted(foreign)]
    with open(CATS_FILE, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")


def main(argv):
    blender = argv[argv.index("--blender") + 1] if "--blender" in argv else DEFAULT_BLENDER
    only = set(argv[argv.index("--only") + 1:]) if "--only" in argv else set()
    plan = model_parts()
    jobs = [(rel, ["--mark", rel.split("/")[1]]) for rel in component_files(only)]
    jobs += [(rel, ["--mark-model", rel]) for rel in plan if not only or rel in only]
    jobs += [(rel, ["--mark-character", name]) for rel, name in CHARACTERS.items() if not only or rel in only]
    jobs += [(rel, ["--mark-characters", cat]) for rel, cat in CHARACTER_FILES.items() if not only or rel in only]
    paths, failed = [], []
    for rel, mode in jobs:
        proc = subprocess.run([blender, "--background", os.path.join(LIB_ROOT, rel), "--python",
                               os.path.abspath(__file__), "--"] + mode,
                              capture_output=True, text=True, encoding="utf-8", errors="replace")
        rows = [l for l in proc.stdout.splitlines() if l.startswith(("MARKED", "KEPT", "NOPREVIEW"))]
        count = {k: sum(r.startswith(k) for r in rows) for k in ("MARKED", "KEPT", "NOPREVIEW")}
        if proc.returncode != 0 or not (count["MARKED"] or count["KEPT"]):
            failed.append(rel)
            print("FAILED  %s\n%s%s" % (rel, proc.stdout[-400:], proc.stderr[-800:]))
            continue
        paths += [r.split("\t")[2] for r in rows if r.startswith("MARKED")]
        print("%-60s %d marked%s%s" % (rel, count["MARKED"],
                                       ", %d kept" % count["KEPT"] if count["KEPT"] else "",
                                       "  (no preview in background)" if count["NOPREVIEW"] else ""))
    foreign = foreign_catalogs()
    # a partial run adds its catalogues to the existing ones; a full run rewrites from scratch
    existing = set(read_cats(CATS_FILE)) if only and os.path.exists(CATS_FILE) else set()
    write_catalogs(existing | set(paths), foreign)
    print("wrote %s (%d own + %d merged catalogues)" % (CATS_FILE, len(existing | set(paths)), len(foreign)))
    if failed:
        raise SystemExit("%d file(s) failed: %s" % (len(failed), ", ".join(failed)))


if "--mark" in sys.argv:
    mark_open_file(sys.argv[sys.argv.index("--mark") + 1])
elif "--mark-model" in sys.argv:
    mark_open_model(sys.argv[sys.argv.index("--mark-model") + 1])
elif "--mark-characters" in sys.argv:
    mark_open_character_file(sys.argv[sys.argv.index("--mark-characters") + 1])
elif "--mark-character" in sys.argv:
    mark_open_character(sys.argv[sys.argv.index("--mark-character") + 1])
elif __name__ == "__main__":
    main(sys.argv[1:])
