# Item Scanner — build record

A handheld salvage finder: a cream field instrument built around a green CRT,
carried on a leather-wrapped grip. Ships to Unity as
`Assets/Game/Art/Models/Items/item_scanner.fbx` and drives the
`ItemScannerArtifact` gameplay component.

Built 2026-08-21 from a reference image of a Fallout-style Pip-Boy — a chunky
CRT unit with a bail handle, whip antenna, control deck and a leather forearm
cuff.

`models/gear/` is a new category. The library had `buildings`, `characters`,
`creatures` and `vehicles`, and carried equipment lived as component files only
(`leash_device`, `antigrav_device`, `weather_station_device`, the two
backpacks). This is the first carried thing that *assembles* from more than one
component, so it needed somewhere to be a model rather than a component.

## Decomposition

| Piece | Source | Why separate |
|---|---|---|
| `Mesh_Terminal_Scanner_Case` | `components/props/handheld_terminal.blend` | the static body |
| `Mesh_Terminal_Scanner_Screen` | same | Unity paints the radar shader on this renderer alone |
| `Mesh_Terminal_Scanner_Dial` | same | the game spins it while scanning; origin on its axis |
| `Mesh_Terminal_Scanner_Antenna` | same | the game sways it; origin at its root |
| `Mesh_ArmCuff_Grip` | `components/props/arm_cuff.blend` | reused unchanged |
| `Mesh_ItemScanner_Bracket` | authored here | the only geometry unique to this model |

Component names are kept rather than renamed to `Mesh_ItemScanner_*`, so each
piece's provenance is readable straight off the outliner. Nothing in Unity binds
by name — the prefab wires serialized `Transform` references, which survive a
rename and an FBX re-export both.

## Reuse

Three components were created for this build; none of the existing library
served, and the reasons are worth recording because they are the same reasons
`item_devices_BUILD.md` gives:

- **`components/mechanical/panel_control.blend`** — rocker bank, rotary
  selector, ribbed knob, guarded toggle, connector strip. `console_panel.blend`
  already has all of these, authored against 0.6–2.7 m vehicle panels; scaled
  down 20× to fit a handheld deck their bolt and panel-line density turns into
  noise. The builders here are importable functions, and `handheld_terminal.py`
  calls them rather than appending the .blend — a rocker is 40 triangles and
  appending a whole file to get one is more machinery than the part is worth.
  What matters is that there is one definition of what a rocker looks like.
- **`components/props/handheld_terminal.blend`** — nothing in the library was a
  carried instrument with a screen. The nearest neighbours are
  `leash_device.blend` and `weather_station_device.blend`, which share the
  language (0.15–0.28 m, palette metals, an emissive readout) but not the form.
- **`components/props/arm_cuff.blend`** — nothing in the library mounted
  anything to a limb.

`Mat_Metal_Copper_Oxide` aside, every surface uses a material that was already
in `PALETTE.md`. **No palette entries were created.** The leather is
`Mat_Fabric_Seat_Ochre` (documented as "cracked ochre vinyl"), the bleached
pommel is `Mat_Paint_Hull_Bleached`, the case is `Mat_Plastic_Cream_Aged`, the
screen is `Mat_Emissive_Green_CRT`. That last one is replaced in Unity by a
live display material and only ever shows as the unlit fallback.

## The screen is its own object, and it carries UVs

Two departures from how everything else in this library is built, both forced by
the same requirement — the display has to be drawn by a shader at runtime.

**Its own object** so Unity gets a separate renderer. A face on the case would
mean the radar shader repainting the whole device, and a `MaterialPropertyBlock`
could not address the display alone.

**Its own UV layer** because `_buildlib` writes none — the vehicle and building
components are shaded by flat palette materials and never needed one. A
procedural display shader is authored in 0..1 screen space, so with no UVs every
fragment samples (0,0) and the display renders as one flat colour.
`handheld_terminal.planar_uv()` is the fix, and it is applied to the screen
plates only. The plate is also deliberately **not bevelled**: a bevel folds new
faces into the UV island and drags the display's edge pixels around the rim.

## No armature

The skill's default is to add one wherever anything could move, and two things
here do: the dial and the antenna. Both are rigid, neither deforms, and both are
already separate objects with their origins on their own axis of motion — the
cleaner form of exactly the same capability, and it skips a bone hierarchy Unity
would have to unpick on import. Recorded as a deliberate choice, not an omission.

## Proportions: revised after first review

The first build followed the reference faithfully and was wrong for the game.
Two changes, both requested:

1. **The screen doubled.** 0.085 × 0.060 m became 0.113 × 0.092 — about 78% of
   the instrument face, and 2.0× the area. The reference is a photograph; this
   has to be legible to a player glancing down at it mid-game. The controls paid
   for it: the deck under the screen is now a 22 mm strip and the card slot is
   gone, because at this bezel size there are 6 mm of face left and a slot drawn
   into it reads as a scratch. The status lamps moved off the face to below the
   deck for the same reason.
2. **The mount became a grip.** The model shipped on `Coll_ArmCuff_Leather`, a
   0.11 m forearm sleeve — something you strap on, not something you hold, and a
   hand cannot close around it. It now ships on `Coll_ArmCuff_Grip`, whose waist
   is held at 0.050 m because that is what fits inside a closed fist and it does
   not scale with how big the device is. The bracket shrank with it (0.104 →
   0.078 m plate) and grew shoulders, since the head is now half again wider
   than the haft it stands on.

Both variations are kept. The sleeve is still the right mount for anything
genuinely worn.

## Verification

- Rendered from `_preview.py` at each stage; the assembled model matches the
  reference's silhouette, two-tone case, control deck and left-flank asymmetry.
- Imported into Unity and rendered live through the display shader — see the
  prefab notes in `docs/`. Screen plate arrives with UVs; every other mesh
  arrives without, as intended.
- FBX sub-object file IDs are derived from object names, so the re-export after
  the proportion rework kept every prefab reference intact. Only the cuff was
  renamed, and nothing references it.

## Anything decided that might want deciding differently

- **The rear-contact strip.** Contacts behind the holder cannot live on a 180°
  forward arc, so they are parked on a rail in the footer at their lateral
  position. A full 360° PPI was the alternative and was rejected because the
  reference's semicircle is the whole look of the instrument.
- **`models/gear/` as a new category.** `models/props/` would also have been
  defensible; "gear" was chosen to mean carried equipment specifically, leaving
  "props" free for set dressing.
