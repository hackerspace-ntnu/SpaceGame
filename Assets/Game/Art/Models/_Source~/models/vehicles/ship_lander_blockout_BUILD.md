# Lander interior block-out — build record

Built 2026-08-30. Volumes only — no hull, no detail. This is the reference the
hand-built lander gets modelled around, so that its interior is proven to hold
3 m characters *before* any exterior work starts.

Files:

- `ship_lander_blockout.blend` — the block-out inside a ×30 copy of the example hull
- `ship_lander_blockout.py` — generator (`--out`) and fit checker (`--check grid.pkl`)

---

## Source

`models/example/futuristic+spacecraft+3d+model.fbx 3` — the larger of the two
Tripo files. One mesh, 5 748 verts, normalised to **1.00 long × 0.74 wide ×
0.38 tall**, ground at z = 0, nose at −Y. It is a closed-ish outer shell with
**no interior at all** and overlapping sub-shells (pods, greebles), so it is not
watertight — an inside/outside test flips in places. Everything below was read
off a 0.01-unit voxelisation that requires *both* an X-ray and a Z-ray to agree
that a point is inside the fuselage; wings and pods drop out of that on their
own because they are thin.

## What the hull actually offers (normalised units, hull length = 1)

| station (y) | floor | flat ceiling | dome above | half-width | note |
|---|---|---|---|---|---|
| −0.46…−0.36 nose | 0.02 | 0.17 | — | 0.09 | too low for standing 3 m characters unless ×36+ |
| −0.36…−0.14 fore body | 0.02–0.03 | 0.19 over ±0.11 | to 0.27 over ±0.05 | 0.12–0.13 | canopy hump — the dome is narrow |
| −0.14…+0.08 mid body | 0.07 → 0.10 rising aft | 0.25 → 0.28 over ±0.13 | — | 0.13–0.14 | **the only full-height, full-width volume** |
| +0.08…+0.27 aft body | centre 0.12 → 0.18, but 0.08 at \|x\| 0.10–0.14 | 0.29 | — | 0.14 + wings | underside is a raised channel between two nacelles |
| +0.27…+0.36 tail boom | 0.17–0.19 | 0.27 | — | 0.12 | not habitable; ramp passes under it |

Two facts drive the whole layout:

1. The fuselage floor **rises aft** (0.02 → 0.10 → 0.18). A single flat deck
   either pokes out of the belly aft or wastes half the height forward, so the
   interior is two decks: a low forward deck for the cockpit and a raised main
   deck for the big room.
2. The aft-body underside is already recessed upward between two lower
   nacelles. That recess *is* the rear ramp bay — the exterior only needs the
   channel opened, not reshaped.

## Scale

`SCALE = 30` (one normalised unit = 30 m): the ship becomes **30 × 22.2 × 11.4 m**.
Chosen from the tightest clear height, not from the room sizes: at ×30 the
cockpit nose has 3.6 m clear over its deck (3 m character + 0.6 m), the bridge
4.2 m, the main room 4.2–4.8 m. Every number below scales linearly with that
one constant; if the main room feels small, raise it (×36 gives a 8.6 × 7.9 m
main room and a 5.4 m ship-length increase — nothing else changes).

## Layout (metres at ×30; y negative is forward, z from the ground)

| volume | W × L × H | y range | deck | fits because |
|---|---|---|---|---|
| `Room_Cockpit_Nose` | 4.8 × 4.5 × 3.6 | −12.3…−7.8 | 1.20 | hull ±0.09 wide, roof 0.17–0.22 |
| `Room_Cockpit_Bridge` | 6.0 × 3.6 × 4.2 | −7.8…−4.2 | 1.20 | hull ±0.12, flat roof 0.19+, dome 0.27 |
| `Steps_Bridge_To_Main` | 1.8 × 2.1 × 2.1 rise | −6.3…−4.2 | 1.20→3.30 | 2.1 m rise over 2.1 m run — five 0.42 m steps |
| `Door_Bulkhead` | 1.8 wide × 3.9 tall | −4.2 | 3.30 | bulkhead between decks |
| `Room_Main_Fore` | 7.2 × 2.4 × 4.2 | −4.2…−1.8 | 3.30 | roof 0.25 over ±0.13 |
| `Room_Main_Aft` | 7.2 × 4.2 × 4.8 | −1.8…+2.4 | 3.30 | roof 0.27–0.28 over ±0.13 |
| `Door_Side_Sliding` | 3.0 wide × 3.6 tall, starboard | −3.0…0.0 | 3.30 | hull side is flat here at x ≈ 0.13–0.14 |
| `Door_Side_Pocket` | 3.0 × 0.75 × 3.6 | 0.0…+3.0 | 3.30 | door slides **aft** into the wing-root wall (hull is 0.18+ thick there) |
| `Door_Rear_Baggage` | 5.4 wide × 4.2 tall | +2.4 | 3.30 | rear bulkhead of the main room |
| `Cut_RampBay` | 6.0 × 5.7 × 5.7 | +2.4…+8.1 | 2.40 | exterior volume to remove (see below) |
| `Ramp_Baggage_Lowered` | 5.4 wide × 8.8 long | hinge +2.4, foot +10.6 | — | 22°, sill 3.3 m up, passes under the tail with ≥ 5 m headroom |
| `Keepout_RampHeadroom` | 6.0 × 2.7 × 5.1 | +8.1…+10.8 | 0 | tail boom underside must stay ≥ 5.1 m up — it already does |
| `Keepout_SideDoorApproach` | 2.55 × 3.0 × 6.9 | −3.0…0.0 | 0 | outside the side door, for stairs/lift |

Main room total: **7.2 × 6.6 m, 4.2–4.8 m clear** (47 m²). Cockpit total 8.1 m
long. Three `Ref_Character_*` boxes (0.9 × 0.6 × 3.0 m) stand in the main
room, the cockpit and on the ramp for scale.

Every room/opening box was sampled on a 0.01 grid against the voxelised hull:
all samples inside except those on three known parity-flip rows of the
non-watertight mesh (y −0.28 z 0.10–0.13, y −0.16 z 0.15–0.16, y +0.08 z
0.12–0.17). No wall, floor or ceiling sample fails. A 0.01-unit (0.3 m) skin
allowance is kept between every room and the example surface.

## Exterior changes the interior forces

These are the places the rebuilt hull must differ from the example. They are
marked in red in `Coll_Blockout_ExteriorChanges`.

1. **Open the aft centre channel as the ramp bay.** `Cut_RampBay` — x ±3 m,
   y +2.4…+8.1, z 2.4…8.1. The example fills 58 % of that volume, mostly the
   centre-line thruster housing seen from below/behind. Move that thruster into
   the two nacelles either side (their bottoms are at z 2.4, i.e. they stay).
2. **Remove the starboard blister beside the side door.** The example has a
   side bulge at y −1.2…+0.6, z 5.4…7.5 that sticks 1.5 m out over the top
   half of the door opening (7.8 % of `Keepout_SideDoorApproach` is hull). Either
   delete it, or mirror-delete both and keep the hull side flat there.
3. **Side door sill is 3.3 m above the ground.** The hull belly under the door
   is at z 2.4, so there is 0.9 m of skin then 2.4 m of landing-gear air.
   Kneeling gear, a deployable stair, or a lift needs to live in
   `Keepout_SideDoorApproach`. Alternatively lower the main deck by cutting the
   belly — not recommended, the mid-body underside is where the gear must go.
4. **Keep the tail boom underside at or above z 5.1 m** for y +8.1…+10.8 — the
   example already does (5.1–5.7 m), so this is a constraint on the rebuild,
   not a change.
5. **The bridge dome** (fore-body canopy hump) only spans ±1.5 m at its top;
   the bridge box stays under the flat 0.19 ceiling. If the rebuilt canopy is
   wider, the bridge can grow taller for free.

## Decisions to revisit

- **Two decks, not one.** The 2.1 m step between cockpit and main room is what
  makes both rooms full-height. A single deck at z 3.3 would leave the cockpit
  2.4 m high; a single deck at z 1.2 would leave the main room floor 1.8 m
  below the belly aft of y = 0.
- **Cockpit is 8.1 m long** (nose + bridge) because the fore body's flat roof
  is too low to be anything else at this scale. If a shorter cockpit is wanted,
  the bridge half becomes an airlock/vestibule; the step stays where it is.
- **Side door on starboard, sliding aft.** Port is symmetrical in the example,
  so the mirror is free. Sliding forward would need the fore-body pod grown up
  by 0.9 m to make a pocket; aft needs nothing.
- **Ramp at 22°.** 8.8 m long. Steeper shortens it but 3 m characters on a
  30°+ slope read as climbing.

## Materials

Nothing added to the palette. The boxes borrow palette emissives purely as
colour coding (`Green_CRT` rooms, `Amber` openings, `Red_Warn` cuts,
`Portal_Orange` keep-outs, `Cabin_Warm` ramp, `Flag_Bleached` characters) and
the reference hull carries `Mat_Glass_Canopy_Tinted`. None of this ships.

## Not done on purpose

- No armature: nothing here is a deliverable that moves. The ramp and both
  doors will need bones on the real model.
- No components: block-out volumes are not reusable parts. The rebuilt hull
  should be decomposed (nacelle, wing, canopy, door leaf, ramp) when it is built.
- The example FBX itself is untouched; the .blend holds a scaled copy.

---

## Addendum 2026-08-30 — turbines and fittings (additive)

`ship_lander_blockout_turbines.py` appended library components into two new
collections, leaving every pre-existing object untouched (asserted by world
matrix and collection membership before saving; backup taken first).

**New component:** `components/mechanical/turbine.blend` — four variations
built for this, all intake at −Y, origin at the intake centre:
`Turbine_Long` (8.0 × ⌀2.2 m), `Turbine_Short` (3.6 × ⌀1.8), `Turbine_Ducted`
(2.4 × ⌀3.2 ring fan), `Turbine_Stub` (1.8 × ⌀1.2). Spinner, bladed fan,
stator ring, core, casing with lip rings and seams, exhaust cone with an
amber glow band, mounting pylon. Palette materials only.

**Reused:** `thruster_nacelle` (Tail as the main drive, Vernier ×4 as RCS),
`vent_grille` (Scoop ×2 as roof intakes), `sensor_cupola` (Radome on the roof).

| collection | objects |
|---|---|
| `Coll_Lander_Turbines` | Long ×2 under the wings, Ducted ×2 at the wingtips, Short ×2 on the aft flanks, Stub ×2 roof (reversed, exhaust aft) + ×2 belly |
| `Coll_Lander_Fittings` | Thruster_Main_Tail, RCS nose/tail ×4, Intake_Scoop ×2, Sensor_Radome_Roof |

Positions are in the script's `TURBINES` / `FITTINGS` tables and were placed
by eye against the ×30 reference hull — move freely; the meshes are appended
copies, so edits here do not touch the component files.

---

## Addendum 2026-08-31 — colour theme (colours only)

Ten local `Mat_Lander_*` materials were created and assigned across the
hand-built shapes — no geometry, transform, name or collection was touched
(asserted by world-matrix + vertex-count snapshot before saving). Objects that
already carried materials (turbines, fittings, `Ref_ExampleHull`) were left
alone. These are **deliberately local, not palette-linked**: the whole point is
that the user tweaks the theme freely in this file without touching the shared
palette. Base colour, roughness/metallic and the viewport display colour are
set together, so solid mode matches rendered mode.

| Material | Hex | Assigned to |
|---|---|---|
| `Mat_Lander_Hull_Primary` | `CDC5B4` | big shell masses, mid/aft body, nose belly (25 objects) |
| `Mat_Lander_Hull_Secondary` | `6B6A50` | side skirts, wing planes, nose tip, medium hull pieces (15) |
| `Mat_Lander_Hull_Accent` | `B65C33` | tail fins, roof rails (8) |
| `Mat_Lander_Nacelle` | `7A7D80` | outboard wing engine clusters (14) |
| `Mat_Lander_Equipment` | `8A8D90` | external gear boxes, pods (17) |
| `Mat_Lander_Mech_Dark` | `33363A` | greebles, rods, antennae, back-door ribs (26) |
| `Mat_Lander_Deck` | `4A4C50` | floor/deck plating incl. cockpit deck slabs (11) |
| `Mat_Lander_Wall_Interior` | `D8D4C8` | interior partitions (21) |
| `Mat_Lander_Door` | `D98A2B` | back door leaf + the four sliding-door leaves (5) |
| `Mat_Lander_Canopy` | `9FC4CE` | cockpit dome icosphere (1) |

Hues sit in the same family as the palette materials already on the appended
turbines (bleached hull / olive / worn steel / amber), so the ship reads as one
object (GDC-L1-TECH-0004: coherence over per-part fidelity). Assignment logic
and per-object mapping: session scratch `apply_colors.py` / `color_mapping.json`
(geometric classification by slab thickness / size / station, plus hand
overrides for the antenna rods, cockpit deck slabs and nose-tip plates).

## Cockpit addendum (2026-08-31, `ship_lander_blockout_cockpit.py`)

Second authorized additive pass (explicit user request, same contract as the
turbine pass: backup taken, new collection only, pre-existing transforms and
collection membership asserted unchanged before save).

`Coll_Lander_Cockpit` — three appended library components on the pilot dais
under the canopy dome. The dais was located by ray-cast probe, not bounds
(the nose AABBs of `Cube.063/064` lie): floor `Cube.030` top z=4.04, clear
y −9.7..−7.9, x −1.7..1.4, with the `Cube.070` dash mass (top z=5.34) as the
forward limit and the step down to `Cube.029` (z=3.35) as the aft limit.

| Object | Component (new variation, built for this pass) | Placement |
|---|---|---|
| `Cockpit_Console_Bridge` | `props/console_panel.blend` → `Coll_ConsolePanel_Bridge` | (−0.15, −9.45, 4.04), fascia aft; front flush with the dash mass, screen bank just proud of it |
| `Cockpit_Steering_Wheel` | `props/steering_yoke.blend` → `Coll_SteeringYoke_Wheel` | hub (−0.15, −8.98, 4.77), euler (78°, 0, 180°): face raked 12° up at the pilot, column down to a foot plate on the dais, threaded between the seat's leg guards |
| `Cockpit_Seat_Command` | `props/crew_seat.blend` → `Coll_CrewSeat_Command` | (−0.15, −8.28, 4.04), facing the nose; pedestal fully on the dais, backrest overhangs the aft step |

Components author +X as forward, so the console and seat carry Rz(−90°); the
yoke authors wheel-face +Z / column −Y (ShipRVBuilder's mount frame).

**Canopy made see-through** (the one authorized material change):
`Mat_Lander_Canopy` alpha 0.30, transmission 0.60, roughness 0.05, blend
mode BLEND, backface culling off. Note the dome ellipsoid's lower skin hangs
0.1–0.3 m above the dais, so floor-standing geometry near the rim visibly
pierces the glass — pre-existing authored overlap, now visible because the
glass is clear.

New component variations use palette-linked materials (Safety_Orange /
White_Arctic / Panel_Grey family) like the turbines — the local
`Mat_Lander_*` theme stays untouched.

## Addendum 2026-08-31 — collision proxy (`player_ship_export.py`)

The export now writes a second FBX beside `player_ship.fbx`:
`player_ship_collision.fbx`, nothing but convex hulls, which is what
PlayerShipBuilder mounts as the ship's collision.

It exists because Unity will only put a *convex* MeshCollider on a Rigidbody,
and no rule applied to this art survives that. A hull per mesh fills the rooms
with the skin around them — `Cube.005` is 12.8 m3 of metal inside an 85 m3
hull. Shrink-wrapping each shell's surface into grid cells (what shipped
first) is worse in a way that is harder to see: a cell has to span every
surface point in it, so wherever the skin curves from floor to roof the cell
becomes a pillar standing in the bay.

So `_collisionlib.py` splits every closed structural mesh with plane bisects
until each piece is nearly convex (volume >= 0.88 of its own hull), then fuses
back any pair whose union hull adds almost nothing. 420 hulls over 119 meshes,
holding 1320 m3 around 1257 m3 of ship — **1.05x**, five percent of phantom
solid across the whole hull. The export fails if that ratio passes 1.15, so a
model change the tuning no longer fits stops the build instead of quietly
filling the interior back in.

Out of the bake, because something else gives them a collider:

| Left out | Who gives it one instead |
|---|---|
| `sliding_door_1..4`, `back_door`, `back_door_support*`, `Cube.129`/`.119`/`.043` | their hinge pivot, so the collider travels with the panel |
| `Mesh_CanopyDome` (`Icosphere`) | nobody, deliberately — a 3 m head sits inside the glass ball |
| `Part_*`, `Cockpit_*`, `Turbine_*`, `Thruster_*`, `Intake_*`, `RCS_*`, `Sensor_*` | one convex MeshCollider on the mesh itself: a socket switches its module's off, and a chair's is the MountStation click target |

The two lists (`COLLISION_SKIP` / `COLLISION_OWN_COLLIDER_PREFIXES` here,
`NoStructuralCollider` in PlayerShipBuilder) have to agree, and a mesh dropped
by both sides is simply not solid — which reads as a hole in the hull long
after anyone connects it to a renamed mesh. Each hull is named
`COL_<source mesh>_<n>` so the builder can read the source names back and fail
the build when one is uncovered.

**Open, pre-existing:** the cockpit's two forward chairs sit inside the dais
block. `Cube.030` is a solid box spanning z 2.93..4.44 and the command chairs'
pedestals bottom out at z ~3.87 — 0.6 m below its top surface, not on it. The
cockpit addendum above records the dais top as z=4.04, so either that
measurement or the block has moved since. It follows through into the built
prefab: the pilot's seat point and dismount point are inside solid geometry.
Not caused by the collision rework (the block was solid before too) and not
fixed here — it is a placement question in the .blend.
