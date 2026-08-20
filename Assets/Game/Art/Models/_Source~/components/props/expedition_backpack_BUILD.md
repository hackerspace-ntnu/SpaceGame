# expedition_backpack — build record

Built 2026-08-13. Replaces `field_backpack` as the player's deployable pack.
Spec: `docs/superpowers/specs/2026-08-13-backpack-rework-design.md`.

## Why a new file rather than an edit

`field_backpack.blend` is untouched and still in the project. Three reasons:

1. The `.blend` is the source of truth and may carry hand edits a generator would destroy.
2. `field_backpack.py`'s own header forbids re-running it over its output.
3. The swap is a single prefab reference on `PlayerCharacter`, so the whole change is
   reversible without touching history.

## Decomposition

This is one of the two files in `components/props/` that is a **contract, not a family**
(the other is `field_backpack`). Unity binds to object names, so it holds exactly one
variation and its names are load-bearing:

| Object | Role |
|---|---|
| `Mesh_ExpeditionPack_Frame` | everything static: carcass, exoframe, harness, lacing, side pouches, oxygen, bedroll |
| `PIVOT_Panel` → `Mesh_ExpeditionPack_Panel` | front panel, hinged on its bottom edge |
| `PIVOT_Lid` → `Mesh_ExpeditionPack_Lid` | storm flap, hinged on its back top edge |
| `SOCK_Ext_0..9` | ten exterior anchors |
| `SOCK_Int_0..11` | twelve interior anchors, all on static shelves |

Three meshes rather than a dozen components because the pack is a single authored object
whose parts cannot be reused anywhere else — a hinged panel with this exact hinge line is
not a library part. The generic pieces it does use (`bent_tube`, `ribbon`, `net`,
`loop_buckle`, `stitches`) are code helpers shared with `field_backpack.py`, which is the
right level of reuse for this kind of asset.

15.2k tris total.

## Materials

All from the shared palette, none added:

`Mat_Fabric_Canvas_Faded`, `Mat_Metal_Steel_Worn`, `Mat_Metal_Rust_Heavy`,
`Mat_Metal_Brass_Tarnished`, `Mat_Plastic_Rubber_Black`, `Mat_Emissive_Amber`,
`Mat_Paint_Safety_Orange` (the oxygen bottles — the single high-vis note),
`Mat_Fabric_Wing_Ochre` (lid, pouch flaps and pocket cloth, so the soft parts read a shade
off the body).

## Decisions worth knowing about

**The carcass is four lofted walls, not one hollow shell.** The obvious build is a single
loft whose cross-section is a C. That C caps into a concave n-gon, which triangulates into
overlapping faces on FBX export. Four convex lofts cost a few more faces and cannot go wrong.

**Interior anchors are never children of a hinge.** An anchor on a moving panel seats its
item along that panel's normal, so the item juts into mid-air the moment the panel swings.
All twelve stand on static shelves: three tiers of three inside the main bay, plus three in
the brain compartment that the lid uncovers.

**Only two exterior anchors are on the front panel, and this is the interesting one.** The
panel is hinged on its BOTTOM edge, which is the only hinge that lays it out on the ground —
and a board that falls forward about its own base necessarily arrives outer-face DOWN. So
anything netted to that face ends up underneath it when the pack is open. The panel keeps
its cargo net as the visual it is meant to be, and carries only the pair whose job is
specifically to show gear on a worn pack. The other eight ride surfaces that present in both
states: the lid top (2), the side pouch flanks (2), the pouch top nets (2), the outrigger
loops (2).

A top hinge was considered and rejected: it swings the panel out at chest height like an
awning rather than down to the ground, which loses the "unpacked onto a groundsheet" read
the whole rig exists for.

**Hinge signs were measured, not derived.** Blender is right-handed and Unity is left-handed,
and the FBX conversion mirrors X, so the sign of a rotation about a hinge is not something to
reason about in the abstract. The values in the prefab — panel `+95` and lid `-105`, both
about the pivot's local `(1,0,0)` — were confirmed by applying them to the imported prefab
and reading back where the parts ended up:

```
CLOSED  Lid centre=(0.000, 1.363,  0.008)   Panel centre=(0.000, 0.663, 0.312)
OPEN    Lid centre=(0.000, 1.444, -0.422)   Panel centre=(0.000, -0.012, 0.795)
```

Panel drops to the ground (y 0.66 → 0.00) and forward (z 0.31 → 0.80); lid tips up and back.

**Size.** 1.17 x 0.75 x 1.59 m over the pouches and pockets, 0.90 m across the body alone.
Bigger than the +15% the brief asked for in width and depth, because the flap-top rig adds
side pouches and front pockets the cabinet did not have; the body itself is close to the
target. Shelf pitch is 0.38 m and column pitch 0.28 m, both set from the new
`pocketFitSize` of 0.28 m rather than the old 0.19 — a shelf pitched for the old size clips
gear stowed at the new one.

**No armature.** The two moving parts are rigid and hinge about single axes, which the
`BackpackHinge` array drives directly from the prefab. Bones would add a skinning path with
nothing to skin.

## Verification

`dump_sockets()` runs at build time and prints every socket's world position and mouth
direction in both the closed and open states. The rule it checks — local +Y points out of
each socket's own mouth — is invisible when broken: the pack looks correct in Blender and
every item merely lies on its face in game.
