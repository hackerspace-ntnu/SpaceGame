# Repair Station — build record

The lander's scrap-fed workstation (`RepairWorkstation`), as a piece of the
crew's furniture rather than a stack of Unity primitives. Brief: a bench that
says "things get fixed here" at a glance, with an obvious place to put the
scrap, a lamp that changes colour, and a screen the progress gauge is drawn on.

Design basis: the receptacle is the signifier for the one verb the machine has
(GDC-L1-UX-0004 — make the right action obvious), and the lamp + gauge are its
feedback, ranked lamp-first because the lamp reads from across the room and the
gauge from in front of it (GDC-L1-UX-0003 — hierarchy and feedback).

## Reused, by path

- `components/mechanical/panel_control.py` — `rocker_bank`, `guarded_toggle`,
  `ribbed_knob` imported as builders, MATS indices 0–9 matched index-for-index
  (the holo_base precedent).
- `_tracked.TrackedPart` for every part with a torus/loft in it; `restamp()`
  before the bevel pass.

## New components, and why each is separate

`components/props/scrap_hopper.blend` — the intake. Separate from the bench
because it is what makes ANY machine scrap-fed: the ShipRV's cargo-bay unit,
an outpost recycler, a wall slot in a corridor. Three variations, each two
objects (body + lid, the lid being the part a game nudges):

| Collection | Silhouette | Needed / ahead |
|---|---|---|
| `Coll_ScrapHopper_Chute` | square funnel on a plate, flap lid | the request |
| `Coll_ScrapHopper_Drum` | banded drum, domed lid | built ahead (floor-standing) |
| `Coll_ScrapHopper_Slot` | wall letterbox with a spring flap | built ahead (bulkhead) |

`components/props/repair_bench.blend` — the furniture. Separate from
`field_bench` (free-standing, dragged outside, plywood and rust) and from
`wall_locker` (storage, nothing happens on it) because this is bolted-in ship
kit with a work surface and a machine on it. Three variations:

| Collection | Silhouette | Needed / ahead |
|---|---|---|
| `Coll_RepairBench_Bulkhead` | bench + upright back panel, 1.40 × 0.70 × 1.75 | the request (the lander's) |
| `Coll_RepairBench_Island` | open bench, console pedestal, lamp mast, 1.20 × 0.80 | built ahead (an outpost workshop) |
| `Coll_RepairBench_Compact` | tall locker with a fold-down leaf, 0.80 × 0.50 × 1.78 | built ahead (a cramped cabin) |

Each variation is several objects — cabinet, worktop, back panel/console,
spindle housing, spindle, status lamp, vice, leaf — because the game reaches
them by role (below), and because parts are never merged in this library.

## Assembly

`repair_station.blend`, one collection `Coll_RepairStation`: the Bulkhead
bench's seven objects and the Chute's two, appended (not linked — an export
needs real mesh data) and renamed to their roles, plus one marker:

| Object | Role |
|---|---|
| `Mesh_RepairStation_Spindle` | `RepairWorkstation.spinningParts` — origin on the axle, spins about local X |
| `Mesh_RepairStation_StatusLamp` | `RepairWorkstation.statusLight` — ONE material, so the retint covers the whole part |
| `Mesh_RepairStation_HopperLid` | `RepairWorkstation.clunkTarget` — origin on the hinge |
| `Marker_RepairStation_Gauge` | 4 mm cube whose origin is the screen face; `RepairProgressUI` is drawn there |

The chute stands centred on the worktop, sunk 5 mm, its lid barrel 2 cm off
the back panel. Gauge screen above it, control fascia to the right, louvre
vent and conduit to the left, lamp on the top rail, spindle at the left end
of the top and the vice at the right on a rubber mat.

Exported whole by `repair_station_export.py` to
`Assets/Game/Art/Models/Props/repair_station.fbx`; `RepairStationBuilder`
(Tools ▸ SpaceGame ▸ Build Repair Station Prefab) wires it, and
`PlayerShipBuilder.BuildRepairStation` nests it on the lander's main deck at
the crew's 1.7× fixture scale.

## Materials

All palette, nothing added: Panel_Grey carcass and panel, Steel_Dark frames,
Steel_Worn worktop, White_Arctic fronts with Safety_Orange grips and hopper
(the cockpit kit's accent language), Chrome_Scuffed vice screw and axle,
Rubber_Black mat and conduit, Black_Matte throats and vents, Cream_Aged label
plate. The lamp is `Mat_Emissive_Red_Warn` — the runtime tints it from red to
green, so the authored colour is the "broken" state.

## No armature

The spindle spins and the lids hinge, but each is a single rigid object whose
pivot is carried by its origin — which is exactly what Unity drives
(`RepairWorkstation` rotates and nudges transforms directly) and `_exportlib`
drops rigs on export. A bone would add nothing the origin does not already say.

## Verified

`_zverify.py`: 0 clashing pairs on `repair_station.blend`; on the component
files the only reported pairs are BETWEEN variations, which stack at the origin
by convention. Rendered iso + front of every variation and of the assembly.

## Gotchas hit

- A plate set 1.5 mm proud of its frame is flagged by `_zverify` (SEP is 2 mm)
  and is close enough to shimmer at distance — stand decal-like plates ≥ 3 mm
  proud, or embed them.
- Two objects that share a back plane (worktop and panel both at y = hd)
  z-fight along their overlap even when neither face is ever seen; step one
  of them 5 mm.
- A 10-segment cylinder lying along X has a flat top facet, so a hinge barrel
  whose top is within 2 mm of the leaf's top face fights it; sink it 3 mm.
