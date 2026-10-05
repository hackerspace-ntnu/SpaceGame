"""Make the component library browsable in Blender's Asset Browser.

    python _assets.py [--blender <path-to-blender.exe>] [--only <relative/path.blend> ...]

Every component file under `components/` holds one part FAMILY, with its VARIATIONS as
`Coll_<Family>_<Variant>` collections. This marks each such collection as an asset, files it in a
catalogue `<Category>/<Family>` (category = the folder under components/), gives it a
description, and asks Blender for a preview. It also writes `blender_assets.cats.txt` at the
library root, the file the Asset Browser reads catalogues from. Register this folder once in
Blender (Preferences > File Paths > Asset Libraries) and every part can be dragged into a scene.

The finished characters listed in CHARACTERS are marked too, one asset each (body, rig and eyes)
in catalogue `Characters/Drifters`. Add a character there by hand when it is finished. A file in
CHARACTER_FILES holds several finished characters, each already in its own `Char_<Name>`
collection (body and rig); every such collection is marked, in that file's catalogue.

Dual-purpose like _index_library.py: run under plain Python it drives Blender over each file;
run inside Blender (`--background <file> --python _assets.py -- --mark`) it marks that file.
Re-running is safe: marking is idempotent and only asset metadata changes — no geometry.

Catalogue UUIDs are derived from the catalogue path (uuid5), so every run and every machine
agrees on them without a registry.
"""

import os
import re
import subprocess
import sys
import uuid

LIB_ROOT = os.path.dirname(os.path.abspath(__file__))
COMPONENTS = os.path.join(LIB_ROOT, "components")
CATS_FILE = os.path.join(LIB_ROOT, "blender_assets.cats.txt")
NAMESPACE = uuid.UUID("6f1c2a52-4d1e-4b8a-9d3a-5a0e7c2b9f10")   # fixed: stable catalogue ids
COLL = re.compile(r"^Coll_([A-Za-z0-9]+)_([A-Za-z0-9]+)$")

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

DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"


def words(camel):
    return re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", camel)


def catalog_path(category, family):
    return "%s/%s" % (words(category.title()), words(family))


def catalog_id(path):
    return str(uuid.uuid5(NAMESPACE, path))


# ── inside Blender ───────────────────────────────────────────────────────────

def mark_asset(coll, path, description, tags, marked, no_preview):
    if coll.asset_data is None:
        coll.asset_mark()
    coll.asset_data.catalog_id = catalog_id(path)
    coll.asset_data.description = description
    for tag in tags:
        if tag not in [t.name for t in coll.asset_data.tags]:
            coll.asset_data.tags.new(tag)
    try:
        coll.asset_generate_preview()
    except RuntimeError as err:          # background Blender may have no GPU context
        no_preview.append("%s (%s)" % (coll.name, err))
    marked.append("%s\t%s" % (coll.name, path))


def save_and_report(marked, no_preview):
    import bpy
    bpy.ops.wm.save_mainfile()
    for line in marked:
        print("MARKED\t" + line)
    for line in no_preview:
        print("NOPREVIEW\t" + line)


def mark_open_file(category):
    import bpy
    marked, no_preview = [], []
    for coll in bpy.data.collections:
        m = COLL.match(coll.name)
        if not m:
            continue
        family, variant = m.groups()
        mark_asset(coll, catalog_path(category, family),
                   "%s — %s variation (%s)" % (words(family), words(variant), category),
                   (category, family, variant), marked, no_preview)
    save_and_report(marked, no_preview)


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


def existing_catalogs():
    """Catalogue paths already in the file — kept when only some files are (re)marked."""
    if not os.path.exists(CATS_FILE):
        return set()
    with open(CATS_FILE, encoding="utf-8") as fh:
        return {l.split(":")[1] for l in fh if l.count(":") >= 2 and not l.startswith("#")}


def write_catalogs(paths):
    lines = ["# This is an Asset Catalog Definition file for Blender.",
             "# Generated by _assets.py from components/ and CHARACTERS — do not hand-edit.",
             "", "VERSION 1", ""]
    parents = set()
    for p in paths:
        parts = p.split("/")
        for i in range(1, len(parts)):
            parents.add("/".join(parts[:i]))
    for p in sorted(set(paths) | parents):
        lines.append("%s:%s:%s" % (catalog_id(p), p, p.replace("/", "-")))
    with open(CATS_FILE, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")


def main(argv):
    blender = argv[argv.index("--blender") + 1] if "--blender" in argv else DEFAULT_BLENDER
    only = set(argv[argv.index("--only") + 1:]) if "--only" in argv else set()
    jobs = [(rel, ["--mark", rel.split("/")[1]]) for rel in component_files(only)]
    jobs += [(rel, ["--mark-character", name]) for rel, name in CHARACTERS.items() if not only or rel in only]
    jobs += [(rel, ["--mark-characters", cat]) for rel, cat in CHARACTER_FILES.items() if not only or rel in only]
    paths, failed = [], []
    for rel, mode in jobs:
        proc = subprocess.run([blender, "--background", os.path.join(LIB_ROOT, rel), "--python",
                               os.path.abspath(__file__), "--"] + mode,
                              capture_output=True, text=True, encoding="utf-8", errors="replace")
        rows = [l for l in proc.stdout.splitlines() if l.startswith(("MARKED", "NOPREVIEW"))]
        if proc.returncode != 0 or not any(r.startswith("MARKED") for r in rows):
            failed.append(rel)
            print("FAILED  %s\n%s" % (rel, proc.stderr[-800:]))
            continue
        for r in rows:
            if r.startswith("MARKED"):
                paths.append(r.split("\t")[2])
        print("%-55s %d variation(s)%s" % (rel, sum(r.startswith("MARKED") for r in rows),
                                           "" if not any(r.startswith("NOPREVIEW") for r in rows)
                                           else "  (no preview in background)"))
    # a partial run adds its catalogues to the existing ones; a full run rewrites from scratch
    all_paths = set(paths) if not only else existing_catalogs() | set(paths)
    write_catalogs(all_paths)
    print("wrote %s (%d catalogues)" % (CATS_FILE, len(all_paths)))
    if failed:
        raise SystemExit("%d file(s) failed: %s" % (len(failed), ", ".join(failed)))


if "--mark" in sys.argv:
    mark_open_file(sys.argv[sys.argv.index("--mark") + 1])
elif "--mark-characters" in sys.argv:
    mark_open_character_file(sys.argv[sys.argv.index("--mark-characters") + 1])
elif "--mark-character" in sys.argv:
    mark_open_character(sys.argv[sys.argv.index("--mark-character") + 1])
elif __name__ == "__main__":
    main(sys.argv[1:])
