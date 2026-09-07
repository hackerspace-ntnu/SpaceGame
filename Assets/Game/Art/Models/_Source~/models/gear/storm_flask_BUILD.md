# Storm flask — build record

Uncork it and a thundercloud parks itself over the point you aimed at, rains for
30 s and hits the tallest body underneath every 2.5 s.
Design: [`docs/AI/systems/Artifacts/StormFlask.md`](../../../../../../docs/AI/systems/Artifacts/StormFlask.md).

Written as the decomposition was decided, not proposed for approval.

## Reused from the library

**Everything structural.** This is the second bottle out of
`components/props/flask_body.blend` and `components/props/flask_collar.blend`,
which were built for the bottled singularity in the same wave and cut so that
this model would cost almost nothing:

| Reused | From |
| --- | --- |
| `Mesh_FlaskBody_Shouldered_{Shell,Cowl,Window,Bumper}` | `components/props/flask_body.blend` |
| `Mesh_FlaskCollar_Stopper_{Ring,Plug,PullRing}` | `components/props/flask_collar.blend` |
| `place`, `marker`, `sphere` | `components/props/flask_kit.py` |

Nothing new was added to either component file for this model, which is the
test of whether the split was cut in the right place. See
[`bottled_singularity_BUILD.md`](bottled_singularity_BUILD.md) for why
`gas_bottle.blend` and `oxygen_tank.blend` were rejected as the base.

## How it assembles

**Flask space**: origin at the base of the bottle on its axis, +Z up.

```
              ○ pull ring              z 0.181 .. 0.204
              ▲ ground-glass plug      z 0.144 .. 0.181
        ┌──── collar ring ────┐        z 0.138 .. 0.156   (Marker_Cap 0.138)
        │  cowl               │        z 0.110 .. 0.138
        │  ╔═══ window ═══╗   │        z 0.075 .. 0.113
        │  ║ ✳ core + arc ║   │        core centre z 0.094, r 0.029
        │  ╚═══════════════╝  │        arc filament at r 0.034
        │  shell              │        z 0.000 .. 0.078   (Marker_Grip 0.045)
        │  ▓▓ yellow band ▓▓  │        z 0.006 .. 0.026
        └──── bumper ─────────┘        z −0.006 .. 0.008
```

- The collar rides up by `SEAT_Z = 0.138`, where the shouldered body presents
  its neck. The plug and the pull ring both keep the **seat** as their origin
  through the move, which is what leaves the pop as one local +Z translation
  applied to two objects.
- The **arc filament** is two crossed emissive rings in the 5 mm gap between the
  core and the glass, and it is a separate object from the core on purpose: the
  core shrinks as the storm drains, and the lightning must not dim with it.

Unique to this model: the core, the arc and the colour-code band.

## Reading it at a glance

Its whole job is to not be mistaken for the bottled singularity — same kit, same
manufacturer, same 0.2 m. Three channels, ranked (`GDC-L1-UX-0003`):

1. **Silhouette.** Tall and shouldered against a squat puck. 0.21 × 0.11 m
   against 0.19 × 0.14 m, and a visibly different profile at hotbar size.
2. **A stopper standing 30 mm proud.** The singularity's closure is a flush iris
   disc; this one has a plug with a ring on top. A thing with a ring on it is a
   thing you pull, which is a convention the player already owns
   (`GDC-L1-UX-0004`) — and unlike colour it is visible in profile.
3. **Warm against cold, in value as well as hue.** Light warm yellow band and
   amber light inside, against near-black navy and cold cyan. The pair survives
   greyscale and colour-blindness because the two bands sit on opposite sides of
   the shell's own value, one lighter and one much darker.

The pull ring is `Mat_Plastic_Safety_Yellow`, which the palette documents for
exactly this — "safety pins, pull rings, trigger guards".

## Articulation — and why there is no armature

Three moving parts, three rigid transforms about fixed axes:

- the **stopper**, popping straight up, origin on the seat;
- the **pull ring**, riding with it on the same origin;
- the **core**, shrinking about its own centre as the cloud forms.

The core draining is the only feedback that says the charge is spent without a
HUD element, so its readability is a gameplay property rather than a flourish
(`GDC-L1-ANIM-0003`). An armature for three rigid parts on fixed axes is a
skeleton storing three numbers; same call as `dragon_bazooka.py`'s jaw.

## Scale

Authored at its real-world size: **0.2098 × 0.1076 × 0.1076 m**, longest axis
0.210, against the design's "about 0.2 m". Bracket measured, not assumed:
`dragon_bazooka.blend` is 1.3685 m authored and wears `holdSize` 1.25.

Same judgement call as the singularity's, and it applies identically: the
`ItemScaleLadder` `Consumable` bracket is 0.50, which is 2.4x this model, and
whether the prefab wears it there is a hand-feel decision (`GDC-L1-FEEL-0007`)
rather than a modelling one. See
[`bottled_singularity_BUILD.md`](bottled_singularity_BUILD.md).

## Markers

Empties, shipped by `keep_empties=True`. All on the axis, so Unity reads them as
`(0, z, 0)`.

| Marker | Blender | What it is |
| --- | --- | --- |
| `Marker_Grip` | (0, 0, 0.045) | Where a fist closes on the shell |
| `Marker_ThrowPivot` | (0, 0, 0.080) | What the throw arc turns about |
| `Marker_Cap` | (0, 0, 0.138) | The collar seat — where the stopper pops from |

## Palette

Nothing added. `Mat_Paint_White_Arctic` (shell), `Mat_Metal_Steel_Worn` /
`Mat_Metal_Steel_Dark` (collar, hardware), `Mat_Glass_Canopy_Tinted` (window and
the ground-glass plug), `Mat_Neutral_Panel_Grey` (the churning core),
`Mat_Paint_Hazard_Yellow` (colour code), `Mat_Emissive_Amber` (the arc),
`Mat_Plastic_Safety_Yellow` (pull ring), `Mat_Metal_Chrome_Scuffed`,
`Mat_Plastic_Rubber_Black`.

## Verified

- `_zverify.py`: **0 clashing pairs, 0.000 m²**.
- Bounds measured off the built file: 0.2098 × 0.1076 × 0.1076 m.
- FBX re-imported: all three empties survive with their coordinates.

## Principles cited

`GDC-L1-UX-0003`, `GDC-L1-UX-0004`, `GDC-L1-ANIM-0003`, `GDC-L1-FEEL-0007`,
`GDC-L1-CONTENT-0003`.
