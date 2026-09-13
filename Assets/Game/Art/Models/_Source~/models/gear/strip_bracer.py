"""Take the bracer out of a gauntlet .blend, leaving the device module alone.

The gauntlet base stopped being part of a gauntlet on 2026-09-04: the player
wears one bracer permanently and a gauntlet is now just the device that clamps
onto its deck. So every `gauntlet_*.blend` built before that date carries a copy
of the bracer it must not carry any more, and this takes it out.

    blender --background gauntlet_leash.blend --python strip_bracer.py -- --save
    blender --background gauntlet_leash.blend --python strip_bracer.py -- --report

**Subtractive on purpose.** Re-running the generators would have done the same
job for six of the seven, but `gauntlet_item_scanner.blend` is hand-edited and
its generator refuses to overwrite it — see its BUILD.md. Deleting objects out
of the shipped file is the one operation that works the same on a generated file
and on a hand-corrected one, so the whole family goes through it and no hand
edit is at risk. The generators lost their `append_base()` call in the same
change, so a regeneration lands on the same objects this leaves behind.

## What counts as the bracer

Every object named `Mesh_GauntletBase_<part>_<variation>` whose part is in
`BRACER` — the shells, the collar, the hinges and latches, and the hardpoint
itself (`Deck`, `Bosses`), which stays on the arm because it is the thing a
device bolts *to*.

## The two exceptions

Two objects come out of `gauntlet_base.blend` but are device hardware, so they
are renamed into the device's own namespace rather than deleted:

- **the puncher's rails.** `RailLeft`/`RailRight` are the track its sled rides.
  They are the puncher's mechanism, not a mount anyone else could use, and no
  other gauntlet ever appended the Rail variation.
- **the item scanner's deck.** The lead hand-rotated it by `(0, -90, 0)` onto the
  arm's flank together with the console, making it that console's bracket rather
  than the arm's hardpoint. Deleting it would leave the console floating beside
  the arm. The bracer's own deck is untouched on the base, where nothing now
  stands — which is exactly what the hand edit did.

Renaming matters beyond tidiness: Unity derives an FBX sub-object's file id from
its name, so a node that keeps a `Mesh_GauntletBase_` name would still read as
part of the bracer to anything that greps the model.
"""

import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SAVE = "--save" in argv

PREFIX = "Mesh_GauntletBase_"

#: Parts of `gauntlet_base.blend` that belong to the permanently worn bracer.
BRACER = ("Undersleeve", "DorsalShell", "VentralShell", "Collar",
          "HingeFront", "HingeRear", "LatchFront", "LatchRear",
          "Deck", "Bosses")

#: Base objects that are really device hardware: {blend stem: {old name: new name}}.
KEEP_AS = {
    "gauntlet_puncher": {
        "Mesh_GauntletBase_RailLeft_Rail": "Mesh_SuckerPuncher_RailLeft",
        "Mesh_GauntletBase_RailRight_Rail": "Mesh_SuckerPuncher_RailRight",
    },
    "gauntlet_item_scanner": {
        "Mesh_GauntletBase_Deck_Mount": "Mesh_ItemScanner_Bracket",
    },
}


def part_of(name):
    """`Mesh_GauntletBase_DorsalShell_Mount` -> `DorsalShell`."""
    return name[len(PREFIX):].rsplit("_", 1)[0]


def strip(stem):
    """Delete the bracer, rename the exceptions. Returns (deleted, renamed)."""
    keep_as = KEEP_AS.get(stem, {})
    deleted, renamed = [], []

    for ob in [o for o in bpy.data.objects if o.name.startswith(PREFIX)]:
        if ob.name in keep_as:
            new = keep_as[ob.name]
            renamed.append((ob.name, new))
            ob.name = new
            ob.data.name = new.replace("Mesh_", "Data_")
            continue
        if part_of(ob.name) not in BRACER:
            raise SystemExit(
                "%s: '%s' comes from the base but is neither bracer nor a known "
                "exception. Decide which it is in strip_bracer.py before running."
                % (stem, ob.name))
        deleted.append(ob.name)
        bpy.data.objects.remove(ob, do_unlink=True)

    missing = [n for n in keep_as if not any(n == old for old, _ in renamed)]
    if missing:
        raise SystemExit("%s: expected to rename %s, but no such object." % (stem, missing))

    # Meshes the deleted objects were the last user of. Materials are LINKED from
    # palette.blend, so their datablocks are left alone — dropping a zero-user one
    # here would break the link the next time the palette is edited.
    for mesh in [m for m in bpy.data.meshes if m.users == 0 and m.library is None]:
        bpy.data.meshes.remove(mesh)

    return deleted, renamed


def main():
    path = bpy.data.filepath
    stem = os.path.splitext(os.path.basename(path))[0]
    deleted, renamed = strip(stem)

    print("%s: removed %d bracer object(s)" % (stem, len(deleted)))
    for old, new in renamed:
        print("  kept as device hardware: %s -> %s" % (old, new))
    left = sorted(o.name for o in bpy.data.objects)
    print("  %d object(s) remain: %s" % (len(left), ", ".join(left)))

    if SAVE:
        bpy.ops.wm.save_mainfile()
        print("  saved %s" % path)
    else:
        print("  DRY RUN — pass --save to write the file")


main()
