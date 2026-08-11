# ShipRV — build record

A rebuild of the RV spacecraft at a much higher detail level, replacing
`Assets/Prefabs/agents/vehicle/ship_model 1.blend` as the source the Unity
prefab is generated from. The original is untouched and still on disk.

Built 2026-08-09/10. **Hull reshaped 2026-08-10** — see "Reshape" below. This is
a record of decisions, not a proposal.

---

## What it is

A rundown motorhome that flies. Tapered faceted hull, two folding wings carrying
a turbine each, a cargo ramp at the tail and a clamshell that opens the whole of
each side. Walkable inside: bridge with two chairs and a helm yoke forward, a
fitted RV cabin aft, and reserved wall space for the repair workstation Unity
drops in.

**Envelope**

| | now | before reshape | original |
|---|---|---|---|
| overall | 12.94 × 10.58 × 5.37 m | 12.74 × 6.14 × 6.66 | 12.71 × 6.16 × 6.57 |
| hull alone (wings folded) | 12.94 × 4.76 × 5.37 m | — | — |
| cabin interior | 7.60 m long × 4.12 wide × 2.20 headroom | — | ~6.0 × 3.7 × 2.2 |
| triangles | 101,725 | 146,237 | ~7,000 |

The envelope match to the original is **deliberately abandoned**. Wings that
reach 2.8 m outboard of the hull cannot also fit a 6.16 m box, and deleting the
stern drive takes 1.3 m off the height. ShipRVBuilder measures its collision
boxes, hinges, seat and camera points off the meshes at build time, so all of
that follows on its own. The one number that is still a contract is the interior:
deck top z=−1.08, ceiling z=+1.12.

---

## Reshape — 2026-08-10

Seven complaints against the first pass, and what each one changed.

**"Too rectangular."** The hull was a parallel-sided box from end to end with a
separate cab stepped down in front. It is now one continuous form interpolated
from a single longitudinal table (`MASTER`), sliced into the tail, cabin and
cockpit shells the Unity builder needs. Half-beam runs 1.74 → 2.38 → 1.78 m and
the roof drops 1.72 → 2.03 → 1.52; only the 5.4 m of midbody carrying the
clamshell doors is parallel, because those panels are flat and have to stay
flat.

**"Kind of bubbly."** The old section was a rectangle with its four long corners
rounded off at a 0.40 m radius — which is the shape that reads as inflated,
because the only hard lines left were the top and bottom of the side wall. The
section is now five flat planes a side (keel, belly flare, side strake,
shoulder, roof) with hard creases between them, and a rail sitting on each of
the two long creases. Chamfer radius was never the tool; chine lines were.

Both of those failed in opposite directions across two earlier attempts — one
tapered with rounded corners and came out a barrel, one squared off and came out
a container. Few big flat facets swept along a real taper is what satisfies
both at once, and it is why the section function has no radius parameter now.

**"Wings are too short, turbines are on the roof."** They were: two pods sat in
a cradle on the roof at y=±2.05, inside the hull's own 2.36 m half-beam, and the
"wings" were 0.10 m fairings. The wings now hinge off the shoulder crease and
reach y=±5.15, 2.77 m outboard of the hull side, with the leading edge swept
0.86 m aft over the span. Each turbine sits at y=±3.58 with three-quarters of a
metre of wing still outboard of it, plus a tip fin — a pod that covers the whole
exposed span just turns the wing back into a stub.

**"A large engine at the back that is completely unneeded."** Deleted. The
3.8 × 3.6 × 3.0 m stern drive was carried on the aft roof (it did not fit
anywhere else without eating the cargo bay) and dominated the silhouette from
every angle while duplicating what the wing pods do. Two turbines on two wings
is a complete propulsion story. The nose RCS pod went with it — it sat on the
bonnet directly in the new windscreen's sightline. −10.7k triangles.

**"The window at the front is way too small, it leaves overlaps."** The old
screen was a letterbox punched into a flat cab face, with a sun visor slab and
two rotated mullions laid over the top — the visor and mullions were the
overlapping geometry. There is no plate-with-a-hole anywhere now: the roofed
section stops at the header, the bonnet picks up at the sill, and the glass *is*
the hull between them, framed by a header beam, a cowl lip and two A-pillars.
Roughly twice the glazed area, and it cannot overlap because it is sized from
`fwd_hw_at` — the hull's own half-width at the screen's own height.

**"A bit too detailed."** 146,237 → 101,725 triangles, −30%. Cut: the stern
drive and nose RCS (10.7k), fifteen thousand triangles of ceiling and corner
clutter (beacon, festoon, duct, cable bundle, manifold, vent, extract fan, two
deck accents), most of the exterior greebles (roof vent, ram scoop, work lamp,
two belly plates, two roof plates), the helm wheel swapped Ring → Twin (−5.3k
for a part only ever seen from behind), and the door panels lost their rivet
rows and two of six ribs. Everything cut is still in the component library.

**Known interaction:** the wing spar and the upper clamshell leaf both occupy
the space just outboard of the shoulder crease, so at full deployment they
overlap. The previous model had the same arrangement. Clearing it properly means
either a 0.6 m thinner wing or lifting the pods ~0.4 m higher, and the second
undoes the point of moving them off the roof.

---

## Authoring frame — a deliberate deviation from library convention

The library's convention is **−Y forward**. This model is **+X forward, +Y port,
Z up**, matching the original.

`ShipRVBuilder.cs` yaws the imported model 90° about Y and then measures every
hinge axis, collision box, seat point and camera pivot off the meshes at build
time. Re-authoring in the library's frame would have silently rotated the entire
prefab and every derived measurement. Preserving the original frame was the
lower-risk choice by a wide margin, and it is the reason the C# patch below is
twenty lines rather than a rewrite.

Nothing else deviates: metric at unit scale 1.0, 1.0 m structural grid, origins
on connection points, transforms applied, palette-only materials.

---

## Decomposition

### Reused from the library

Nothing — the library did not exist. It does now, and everything below is
available to the next model.

### New components (14 files, 58 variations)

Each is its own file because each could plausibly appear on a different model.
The variation counts are all above the three-minimum; where a variation was
built for later rather than for this ship it is marked **(ahead)**.

| Component | Variations | Why separate |
|---|---|---|
| `structural/hull_plate` | Flat, Ribbed, Riveted, Patched, Vented | The exterior's plating language. Overlays laid on the shell rather than cut into it, so the same vocabulary reaches the nacelles and any future hull. |
| `structural/deck_plate` | Solid, Grate, Hatch, Worn, EdgeStrip | Split from hull plating because the player stands on these at 1.7 m: tread depth matters, rivet rows do not. |
| `structural/bulkhead_frame` | Door, Arch **(ahead)**, Reinforced, HatchRim **(ahead)** | A door is a frame + panel + hinge + handle. This is the frame — the part that stays put. |
| `mechanical/thruster_nacelle` | Main, Tail, Maneuver, Vernier **(ahead)** | One propulsion language at four scales. |
| `mechanical/pipe_run` | Straight, Elbow, Junction, CableBundle, Duct | Exposed services. The highest-reuse component here — hull, ceiling, workstation wall, pylons. |
| `mechanical/hinge_heavy` | Barrel, Piston **(ahead)**, Strap, SlideRail **(ahead)** | Six panels moved on invisible axes before. Origins sit *on* the pivot line. |
| `mechanical/vent_grille` | Louvre, MeshScreen, Fan, Scoop | Interior-facing, so they need a real recess behind the slats rather than a hinted one. |
| `props/crew_seat` | Pilot, Copilot, Bench **(ahead)**, Stool | Two chairs stand side by side on the bridge; identical ones would read as a duplicated asset. |
| `props/steering_yoke` | Ring, Butterfly **(ahead)**, Twin **(ahead)**, Salvaged **(ahead)** | Replaces the twelve-cylinder placeholder the builder assembled at runtime. |
| `props/console_panel` | Helm, Nav **(ahead)**, Breaker, Overhead | The bridge. Three-segment wrapping console with the steering column boss at its centre. |
| `props/light_fixture` | Strip, Dome **(ahead)**, Clamp, Emergency, Festoon | Used more times than anything else. Four different fittings down one cabin is most of what makes an interior look accumulated. |
| `props/wall_locker` | Tall, Bank **(ahead)**, OpenShelf, Dented | The bay walls are the largest empty surfaces on the ship. |
| `props/bunk` | Single **(ahead)**, Stacked, Folded **(ahead)** | Somebody sleeps here — half the RV read. |
| `props/galley_unit` | Sink **(ahead)**, Hob **(ahead)**, Compact | The other half. |

`models/_buildlib.py` is shared infrastructure, not a component: bmesh
primitives, the rivet/seam/greeble generators, palette linking, and the
angle-limited bevel. It exists so fourteen scripts do not each carry their own
slightly different bevel width.

### Unique to this model

Hull shells (closed and open variants), cab, nose, tail, deck, ceiling, interior
lining, the six door panels, wing spars and axles, wing root block, canopy glass.
All of it is shape specific to this ship and none of it would be reused as-is.

---

## How the hull is built

The shell is **plated, not lofted as one skin**: for each pair of stations and
each segment of the cross-section, one closed solid with a 16 mm gap around it.
That gives real wall thickness (so the interior is a wall, not the back of a
one-sided surface), crisp panel lines for free, and lets the side openings be
made by simply omitting two segments.

`Mesh_HullShell_Closed` and `Mesh_HullShell_Open` differ only in whether those
two segments are emitted. `ShellVariantSwitcher` swaps them when the clamshell
opens, exactly as before.

**The section has been rebuilt twice.** Pass one tapered it with rounded corners
and produced a barrel. Pass two squared it off with parallel sides and a stepped
cab and produced a shipping container. The current section is five flat planes a
side with hard creases, swept along a hull that tapers in plan and in profile —
see "Reshape" above for why that is the combination that works.

Three things about the longitudinal shape are load-bearing and easy to undo by
accident:

- **The belly is flat and the tail is a transom.** Tapering top and bottom
  symmetrically at both ends is the obvious move and it makes an almond; an
  almond in profile is a blimp. All the taper the silhouette needs comes from
  the plan view and from the nose dropping away forward.
- **Roof width is per-station** (`MASTER`'s fifth column), 0.56 through the
  cabin rising to 0.86 at the header. The cabin wants a narrow roof and big
  shoulders; the cockpit wants a wide flat one, because the windscreen header
  sits just under the roof edge and a roof that pinches in up there makes the
  screen wider than the hull it is set into.
- **Everything measured forward of the header goes through `fwd_hw_at`, not
  `hw_at`.** `station()` clamps at the end of `MASTER`, so the sill and the
  A-pillars sized off it got the header's 1.78 m half-beam at a point where the
  hull is 1.40 and stood 38 cm out in mid-air.

The deck, ceiling and interior lining are all lofted to `inside_hw` rather than
laid out at constant width — that is what permits the taper at all. The deck
section is a trapezoid whose underside is cut to the hull at the underside's own
height; as a rectangle its bottom corners came out through the belly facet as a
dotted line of tabs the length of the ship.

---

## Articulation

`Arm_ShipRV` carries nine bones, each on the axis its panel was modelled around:
root, two wings, four clamshell leaves, cargo ramp, bulkhead door. Meshes are
parented rigidly to bones rather than skinned — weight deformation on a hinge
smears at the pivot.

The rig is for animating in Blender. It is **stripped on export**: see below.

---

## Unity wiring

`models/models/vehicles/ship_rv_export.py` → `Assets/Models/Vehicles/RV/ship_rv.fbx`

The export is re-runnable (unlike the generators) and never writes to the
`.blend`. It does three things a plain FBX export would not:

1. **Localises the palette materials.** The model links them from
   `models/palette.blend`, outside `Assets/`, which would not resolve from a
   copy inside it.
2. **Un-parents the meshes and drops the armature.** `ShipRVBuilder` finds parts
   with `Transform.Find` — direct children only — and reparents them into its
   own hinge pivots regardless. A bone hierarchy would break the first and be
   redundant against the second.
3. **Exports with Blender's default axis conversion**, so the builder's existing
   `ModelYaw` still lands the model where it always did.

FBX rather than a copied `.blend` because the linked-material path is the exact
thing that breaks when a file moves.

### Changes to `ShipRVBuilder.cs`

Deliberately minimal — the role names on the right of `PartNames` are unchanged,
so nothing downstream of that table had to move.

- `ModelPath` → `Assets/Models/Vehicles/RV/ship_rv.fbx`
- `PartNames` → maps the model's `Mesh_*` names onto the existing role names
- `PartLookup.Find` — a lookup that returns null instead of logging, for optional parts
- `AdoptSteeringWheel` — uses the modelled `Mesh_Bridge_Wheel` and gives it the
  interaction collider, falling back to the primitive wheel if absent. The old
  code's own comment asked for this.
- `BuildCockpit` — seats the pilot on `Mesh_Bridge_SeatPilot`'s actual bounds
  rather than at a guessed offset from the nose

Everything else — collision boxes, deployment wiring, double-siding, the
workstation placement — is untouched and still measures off the meshes.

Rebuild with **Tools ▸ Vehicles ▸ Build ShipRV Prefab**.

---

## Palette

19 materials, created for this build. Three are conversions of colours already
in the original so the rebuilt ship still matches its screenshots:
`Mat_Metal_HullRust_Orange` (#764E2A, the hull), `Mat_Neutral_Slate_Dark`
(#1F2736, nacelles and trim) and `Mat_Neutral_Panel_Grey` (#606060, interior).
The rest fill the gaps that detail at this density needs: bare and dark steel,
scuffed chrome, oxidised copper, aged cream plastic, rubber, seat vinyl, faded
canvas, worn ply, tinted glass, and four emissives.

Four additions triggered the near-duplicate warning (ΔE 6–11) and were kept
deliberately: `Black_Matte` vs `Slate_Dark` (neutral seal black against
blue-tinted hull trim), `Rubber_Black` vs `Black_Matte` (roughness 0.88 vs
0.55, and they sit adjacent on every hinge), `Seat_Ochre` vs `HullRust`
(fabric against metal), `Canvas_Faded` vs `Panel_Grey` (same).

---

## Judgement calls worth knowing about

**Triangle count is 102k, not the 50–70k originally chosen.** The full RV
fit-out, a modelled bridge and greebled nacelles cost more than that estimate
allowed. Roughly: engines 21k, bridge 22k, interior fittings 25k, hull structure
23k, exterior detail 11k. The single biggest remaining lever by far is the two
main nacelles at 10.3k each — they were kept because they are now the visual
centrepiece rather than roof cargo, and cheapening them would undo most of the
reshape. After that it is the bridge console (8.9k) and the pilot seat (4.8k),
both of which face away from the player.

**The ship is much wider than it is tall now** — 10.6 m against 5.4. That is a
consequence of putting the turbines on real wings, and it is only the deployed
figure: the wings fold, and the hull alone is 4.76 m across. If the folded
footprint matters for spawning or garaging, measure the shells, not the bounds.

**The cabin is longer than the original's** — 7.6 m against ~6.0 — because the
cargo doorway was moved to a real aft bulkhead instead of overlapping the tail
structure. Overall length is unchanged.

**The cockpit bulkhead door is a 1.10 × 2.10 m door**, where the original part
was a 2.31 × 2.80 m slab spanning most of the bulkhead. The builder measures the
surrounding bulkhead collision from the door's bounds, so it adapted on its own.

**Both main nacelles use the same orientation rather than being mirrored.** They
are a matched pair off one production line and a mirrored greeble pattern reads
no differently.

**Geometry is welded, not watertight.** `remove_doubles` fuses coincident verts
where two solids meet, which leaves non-manifold junctions at those seams —
normal for kitbashed game assets, invisible in render and export. Genuinely open
boundaries exist only where intended: the canopy glass is a surface, and the
nacelle cowls are open at ends that other geometry covers.
