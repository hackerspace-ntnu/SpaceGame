# Inflator nozzle — build record

A pump that swells a target until it floats, then pops it. Design:
[`docs/AI/systems/Artifacts/InflatorNozzle.md`](../../../../../../docs/AI/systems/Artifacts/InflatorNozzle.md).

## Reused from the library

This model is mostly assembly. Five of its nine parts came from somewhere else, which is the point
of the library working.

| Reused | From | Role |
| --- | --- | --- |
| `Mesh_SprayerTank_Lance` | `components/props/sprayer_kit.blend` | the pressure barrel. 0.323 m along Y, ø0.091 — within 4% of what this device needed, and already the family's own tank. |
| `Mesh_SprayerGrip_Moulded` | same | the grip. A *moulded* grip, which `components/mechanical/weapon_grip.blend`'s pistol variant is not: that one has wooden cheeks, and this kit is issued equipment, not scavenged. |
| `Mesh_SprayerGauge_Plate` | same | the `SupplyGauge` face for the tank. |
| `Mesh_DeviceNozzle_Brass` | `components/props/device_nozzle.blend` (new, this build) | the sealed inflator tip. Nothing in `sprayer_kit.blend` is one — its outlets are a fan and a finned diffuser, which spray. |
| `Mesh_DeviceGauge_Dial` + `Mesh_DeviceGauge_Needle` | `components/props/device_gauge.blend` (new, this build) | the pressure instrument, with the needle as its own object. |

`Mesh_SprayerTrigger_Collar` was considered and left alone: its origin convention was not readable
from the outside, and guessing at another agent's frame is how a fitting ends up 20 mm inside the
barrel it is meant to wrap. The grip carries its own trigger.

## New geometry

Four parts, and each is only true of a pump:

| Object | Why it exists here rather than in a component |
| --- | --- |
| `Mesh_InflatorNozzle_Plunger` | The stroke. Origin on the barrel's rear face, so the whole animation is one local translation along −Y. |
| `Mesh_InflatorNozzle_Hose` | Coiled round *this* barrel at *this* radius. A hose is a path, not a part. |
| `Mesh_InflatorNozzle_Yoke` | The yellow hardware, and the only thing carrying the function colour. |
| markers | Four, below. |

## How it assembles

Barrel along Y, muzzle at −Y, origin on the bore axis at the barrel's own centre — where the
appended lance already puts it.

```
  y +0.265  T-knob ──── Mesh_InflatorNozzle_Plunger, origin at y +0.161
  y +0.161  ═ barrel rear dome
  y +0.055  ○ Mesh_DeviceGauge_Dial, tilted -128 deg about X (up and BACK)
  y +0.010  ▭ Mesh_SprayerGauge_Plate, on the +X flank
  y -0.014  ~ hose coil starts, 2 turns at r 0.0595
  y -0.030  ┬ Mesh_SprayerGrip_Moulded, 7 mm up inside the barrel
  y -0.040  ⊙ yellow band
  y -0.150  ~ hose coil ends, runs forward into the collar
  y -0.161  ═ barrel front, yellow muzzle collar
  y -0.234  ▷ Mesh_DeviceNozzle_Brass tip  ── Marker_Muzzle
```

**Two gauges, and they must never be confused.** The dial reads the *target's* inflation; the bar
reads *this device's* tank. They are deliberately different instruments — a needle and a length —
rather than two bars, which is `GDC-L1-UX-0003`'s hierarchy applied to a single object: two readings
that mean different things must not look alike.

The **dial is tilted −128° about X, not −90°**. A rotation about X takes the component's −Y face to
(0, −cos a, −sin a); past 90° the y term goes positive, which tilts the instrument up and *back*
toward the person holding it. Inside 90° it tilts up and away, readable by the target.

## Variations

One collection, `Coll_InflatorNozzle` — 0.136 × 0.501 × 0.241 m, 7 440 tris. The item has one state:
it is a refillable tank, not a consumable, so there is no spent variant to author. `dragon_bazooka`
ships one collection for the same reason. The overproduction the skill asks for went into the
component files instead, where it is reusable: three gauges, four clamp parts, three nozzles.

**0.501 m long** — the design's "about 0.5 m", one-handed, measured off the built file rather than
intended. For scale, `models/gear/dragon_bazooka.blend` measures 1.3685 m and anchors the whole
`ItemScaleLadder` at a `holdSize` of 1.25; a 0.5 m device is a hand tool on that ladder, not a gun.

## Articulation — and why there is no armature

Two moving parts, both rigid, both one axis, both carrying their pivot in their own object origin.

| Part | Pivot | Motion |
| --- | --- | --- |
| `Mesh_InflatorNozzle_Plunger` | (0, 0.161, 0), the barrel's rear face | translation along −Y, 0.078 m of stroke |
| `Mesh_DeviceGauge_Needle` | the dial spindle, local **Y** | rotation; rest is 215° (lower left, where every instrument ever made starts its sweep — `GDC-L1-UX-0004`, honour the convention rather than inventing one) |

## Markers

| Marker | Blender | Unity | What it is |
| --- | --- | --- | --- |
| `Marker_Muzzle` | (0, −0.2335, 0) | (0, 0, 0.2335) | the brass tip. Where the pump ray leaves. |
| `Marker_Grip` | (0, −0.028, −0.098) | (0, −0.098, 0.028) | in the palm of `Mesh_SprayerGrip_Moulded`. |
| `Marker_Gauge` | (0.0515, 0.010, 0) | (−0.0515, 0, −0.010) | the face of the supply bar. |
| `Marker_Dial` | (0, 0.055, 0.0375) | (0, 0.0375, −0.055) | the pressure dial's spindle. Separate from `Marker_Gauge` on purpose: whatever drives the needle is not the code that drives the supply bar. |

## Palette

**No material was added.** The function colour is `Mat_Plastic_Safety_Yellow`, which the palette
documents for exactly this — "injection-moulded high-vis yellow plastic: safety pins, pull rings,
trigger guards, lever grips". It goes on three parts with three different shapes (muzzle collar,
mid-barrel band, plunger T-knob), so the identity survives shadow and colour blindness
(`GDC-L1-UX-0006`). The plunger knob being the yellow one is the signifier: it is the part a hand is
meant to grab and pull.

## Faults found and fixed while building

1. **The hose buried both gauges.** Coiled over the whole barrel, it put the supply bar between two
   turns and the pressure dial behind a third. Nothing in the numbers showed it; the first render
   did. The coil now lives on the front half only, and the two instruments moved back behind it.
2. `_zverify`: **0 clashing pairs**.

## Shipping

`models/gear/inflator_nozzle_export.py` writes one FBX.

| FBX | Measured |
| --- | --- |
| `Assets/Game/Art/Models/Items/inflator_nozzle.fbx` | 0.136 × 0.501 × 0.241 m, 13 meshes, 7 440 tris |

## Decisions you may want made differently

- **The hose is wrapped, not hanging.** The design says "a coiled hose", and a free loop dangling off
  a 0.5 m item swings through the player's own arm in first person and has to be authored in a pose
  that is wrong from every other angle. Wrapped, it reads as pneumatic from all of them and never
  intersects the hand. If a dangling hose is wanted, it is a second object, not an edit to this one.
- **The nozzle is fixed to the barrel** rather than on the end of the hose. A floppy muzzle on a held
  item cannot be aimed and cannot carry a `Marker_Muzzle` that means anything.
- **`Mesh_SprayerGrip_Moulded` was taken as-is**, materials included. If the sprayers agent restyles
  it, this model follows, which is the point of a shared component and also the risk of one.
