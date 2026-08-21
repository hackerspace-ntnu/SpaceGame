# Handheld terminals, arm cuffs, panel controls — build record

Three components built together on 2026-08-21 for the item scanner
(`models/gear/item_scanner_BUILD.md`), recorded as one family because they were
designed against each other and only one of the three was actually needed.

| Component | Variations | Needed | Built ahead |
|---|---|---|---|
| `components/mechanical/panel_control.blend` | Rocker3, Rotary, KnobRibbed, ToggleGuard | — (its builders are) | all 4 as standalone fittings |
| `components/props/handheld_terminal.blend` | Scanner, Compact, Rugged, Wrist | Scanner | Compact, Rugged, Wrist |
| `components/props/arm_cuff.blend` | Leather, Webbing, Plated, Grip | Grip | Leather, Webbing, Plated |

## Why these are components and not part of the scanner

The library's existing hardware is authored for vehicles and buildings:
`console_panel` variants run 0.6–2.7 m, `vent_grille` is 0.6 m square,
`floodlight_bank` is 1.5 m. A handheld instrument is 0.15–0.20 m. Scaling a
console panel down 20× keeps its bolt and panel-line density, which at handheld
distance is noise rather than detail — the same finding
`item_devices_BUILD.md` recorded for the carried artifact devices.

So the fittings were rebuilt at panel-hardware scale (0.02–0.05 m) in
`panel_control`, and the two things that had no precedent at all — an instrument
with a screen, and a mount that puts kit on a limb — became their own files.

## Variation, per component

Silhouette first, then structure, then condition — colour last, because colour
is the axis that does not survive a thumbnail.

- **panel_control** — four genuinely different fittings on a shared mounting
  plate, not four knobs. Reusable on any console, dashboard or device face.
- **handheld_terminal** — `Scanner` (big CRT, bail handle, whip mast),
  `Compact` (lofted wedge, palm-sized, no handle or mast), `Rugged` (armoured
  clamshell with the lid propped open — the one diagonal in the family), `Wrist`
  (a shallow arc of plates, low profile, nothing protruding). Each is a
  different shape at a glance.
- **arm_cuff** — `Leather` (moulded sleeve), `Webbing` (open harness, three
  bands on two rails — the only one whose silhouette has holes in it), `Plated`
  (overlapping steel lames, serrated profile), `Grip` (waisted haft, hand-sized).

## Shared design decisions worth knowing

**Material index 0 is `Mat_Metal_Steel_Worn` in all three files.**
`bmesh.ops.bevel` stamps every face it creates with index 0, so index 0 is the
colour of every chamfered edge in the file. Structural steel, never an accent —
and it means a forgotten `mat=` argument lands somewhere harmless.

**`panel_control.MATS` and `handheld_terminal.MATS` must stay index-for-index
identical.** The imported builders write material *indices*, not names, so a
divergence silently repaints every knob on the terminal. Both lists are ten
entries in the same order; there is a comment on each saying so.

**`tube_path()` lives in `panel_control.py`.** `_buildlib` has no swept-tube
primitive — `leash_device.py` calls a `Part.sweep` that no longer exists — so
handles, wire guards, guard rails and whip masts all go through one helper that
places a cylinder per segment plus a corner fill.

**Bevel only the boxy faces.** Every builder accumulates a `hard` list and calls
`p.bevel(hard, ...)`. A whole-part bevel at this scale welds thin tube geometry
into blobs and, on the cuffs, stamps steel edges onto leather. `BEVEL_W` is
0.0012–0.0016 here against the library's structural default of 0.012.

**Fittings on a tapering surface are placed against that surface, not at a fixed
offset.** `arm_cuff._at(z)` interpolates the sleeve's width and depth so a strap
keeper written for the wrist is not buried in leather at the elbow. Both failure
modes look like modelling mistakes and neither shows up until it is rendered.

## Two departures from library convention, both in `handheld_terminal`

1. **Every variation emits a separate `_Screen` object.** The display has to be
   its own renderer in Unity or a shader written for it repaints the whole
   device.
2. **Those screen plates carry a UV layer; nothing else in this library does.**
   `_buildlib` writes none, because flat palette materials never needed one. A
   procedural display shader is addressed in 0..1 space and without UVs samples
   (0,0) everywhere. `planar_uv()` is the fix. The plates are deliberately not
   bevelled — a bevel folds faces into the UV island.

## Regenerated, not hand-edited

All three `.blend` files were deleted and rebuilt once, after the scanner's
proportions were revised (bigger screen, hand-sized grip). That is normally
forbidden — the `.blend` is the source of truth and may carry hand edits that
exist nowhere else — and it was safe here only because every file had been
created minutes earlier in the same session and had never been opened in
Blender. **Do not do this again to any of these files.** They are now shipped
work; edit them in place.

`Coll_ArmCuff_Grip` was added in that rebuild. The other three cuff variations
are byte-identical in intent to their first build.
