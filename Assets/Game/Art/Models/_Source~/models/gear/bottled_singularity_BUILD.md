# Bottled singularity — build record

A thrown flask with a knot of nothing in it. Throw it, the collar's iris opens,
everything within 8 m is dragged in for 3 s, then it lets go all at once.
Design: [`docs/AI/systems/Artifacts/BottledSingularity.md`](../../../../../../docs/AI/systems/Artifacts/BottledSingularity.md).

Written as the decomposition was decided, not proposed for approval. It exists
so a later reader can see *why* the model is cut this way without
reverse-engineering it from geometry.

## Reused from the library

Nothing existed that served, and two obvious candidates were rejected on
purpose:

- **`components/props/gas_bottle.blend`** is a *pressure vessel*. Its largest
  single feature is a dial gauge with a bezel, ticks, a needle and a lit
  sector, because a gauge is what says "this thing stores pressure and you plug
  a hose into it". A thrown flask has no hose, no valve and no gauge; it has a
  window and a closure. Sharing the part would have blunted both objects.
- **`components/props/oxygen_tank.blend`** is a 0.39 m back-worn cylinder — the
  same silhouette family at twice the size and a different job.

The shared **palette** is reused throughout and nothing was added to it.

## New components

| Component | Why it is separate |
| --- | --- |
| `components/props/flask_body.blend` | The moulded shell is the reusable half of a thrown bottle. Anything in this kit that is a sealed vessel someone throws takes one, and the storm flask already does. |
| `components/props/flask_collar.blend` | *How a flask opens* has no reason to change when *how much it holds* does. The two questions are answered by two files and joined by one number, `SEAT_R`. |
| `components/props/flask_kit.py` | Not a component — the family's shared assembly and shader-channel helpers, the `_console_kit.py` / `_gauntlet.py` pattern. No .blend. |

### Variations built

Bold is what this model actually needed; the rest are built ahead, per the
skill's overproduction rule. All differ in silhouette or structure, not colour.

- **flask_body** — **Squat** (0.14 m wide, 0.17 tall: a puck you palm),
  **Shouldered** (the storm flask's, tall with a pronounced shoulder), Slim (a
  straight tube with a knurled waist), Ovoid (an egg that will not stand up).
- **flask_collar** — **Iris** (six leaves rotating open in the collar's plane),
  **Stopper** (a ground-glass plug on a yellow pull ring), Screw (a knurled cap
  that turns off), Bail (a swing-top lid on a wire clamp).

## How it assembles

Everything is written in **flask space**: origin at the base of the bottle on
its axis, +Z up, the frame `flask_body.py` authors in. The assembly is *not*
re-origined at the end — unlike a weapon this thing has no grip that the rest
should be measured from. It is a bottle; it stands on a table; the base is
where it touches.

```
                 iris collar               z 0.168 .. 0.190   (Marker_Cap)
        ┌───────────────────────┐
        │  cowl                 │         z 0.121 .. 0.168
        │   ╔═══ window ═══╗    │         z 0.083 .. 0.124
        │   ║  ● core      ║    │         core centre z 0.1035, r 0.040
        │   ╚═ rim light ══╝    │         two rings at the glass edges
        │  shell                │         z 0.000 .. 0.086   (Marker_Grip 0.055)
        │  ▓▓ navy band ▓▓      │         z 0.006 .. 0.028
        └───── bumper ──────────┘         z −0.006 .. 0.009
```

- The collar rides up by exactly `SEAT_Z = 0.168`, the height at which the squat
  body presents its neck. Each iris leaf keeps **its own pin** as its origin
  through that move — `place(origin=lift @ obj.matrix_world.to_translation())`,
  with the *moved* pivot, not the component's. With the component's, the iris
  opens about a point 168 mm below itself, which does not fail, it just chews.
- The window is a glass ring, not a porthole. A thrown bottle lands in whatever
  orientation it pleases, and a window on one face is a window the player is
  looking at the back of half the time.
- Shell and cowl are two capped solids with a gap between them, and the gap is
  the window aperture. That gives an interior the eye reads as an interior with
  no boolean, no interior faces, and no chance of the boolean silently deleting
  ornaments the way it did on the dragon head.

Unique to this model: the **core** (a lofted sphere of latitude rings), the
**rim light** (two emissive rings at the glass edges) and the **colour-code
band**.

## Reading it at a glance

The bottle has to say "that is the one that pulls" from across a room and from a
256 px icon, against a storm flask that is the same kit, the same manufacturer
and the same 0.2 m. Three channels do that work, in the order the eye takes
them, and they are deliberately ranked (`GDC-L1-UX-0003` — hierarchy, and never
encode critical information in colour alone):

1. **Silhouette.** Squat and wide against the storm flask's tall shouldered
   bottle. The only channel that survives being small, dark or in motion.
2. **The black core.** A void behind glass reads as a hole punched in the
   object, and nothing else in the kit is a hole.
3. **Colour, last.** Near-black navy and cold cyan against the storm flask's
   warm yellow and amber — and the two differ in *value*, not only in hue, so
   the pair stay distinguishable in greyscale and for a colour-blind player.

`Mat_Paint_Blue_Station` was tried for the band first and abandoned: pale powder
blue on an arctic-white shell is 0.6 apart in value, so the band vanished at any
distance and the only blue left was a 2 mm rim light.

## Articulation — and why there is no armature

Seven moving parts, seven rigid transforms about seven fixed axes:

- the **six iris leaves**, each turning about the pin its object origin sits on;
- the **core**, swelling through the inhale and snapping flat at the release as
  one uniform scale about its own centre.

An armature would be a bone hierarchy storing the same seven numbers with a
skinning evaluation attached. Same call, for the same reason, as
`dragon_bazooka.py`'s jaw and `sucker_puncher.py`'s ram.

The core's swell is not decoration: it is the only warning anyone standing
nearby gets that the fling is coming, which makes its readability a gameplay
property (`GDC-L1-ANIM-0003`).

## Scale

Authored at its **real-world size**: 0.1946 × 0.1422 × 0.1422 m, longest axis
0.195, against the design's "about 0.2 m".

The bracket was measured, not assumed: `models/gear/dragon_bazooka.blend`
measures **1.3685 m** on its longest axis and wears `holdSize` 1.25, so the
ladder's anchor corresponds to a model authored slightly over it. This bottle at
0.195 m is 16% of the bazooka.

**Judgement call for the wave-2 assets agent.** `ItemScaleLadder`'s existing
`Consumable` bracket is **0.50** (AntiGravityPotion, LightningSpell) — 2.6x this
model's authored size, because the player's hand is roughly 1.7x a human's and
the ladder is tuned for the *sensation* of that hand rather than for physical
accuracy (`GDC-L1-FEEL-0007`, and see `project_item_scale_ladder`). The model is
authored at the design's stated real size; whether the prefab wears it at 0.20
or at the ladder's 0.50 is a hand-feel decision that belongs with whoever can
hold it, not with the mesh.

## Markers

Empties, shipped by `keep_empties=True`. Blender coordinates; Unity is
`(−x, z, −y)`, and all three sit on the axis so Unity reads them as
`(0, z, 0)`.

| Marker | Blender | What it is |
| --- | --- | --- |
| `Marker_Grip` | (0, 0, 0.055) | Where a fist closes on the shell |
| `Marker_ThrowPivot` | (0, 0, 0.090) | What the throw arc turns about |
| `Marker_Cap` | (0, 0, 0.168) | The collar seat — the iris plane |

Empties rather than the 4 mm marker cubes `dragon_bazooka.py` uses: that model
predates `_exportlib.export(keep_empties=True)` and had to smuggle coordinates
through as geometry, leaving the prefab four tiny renderers to strip.

## Palette

Nothing added. Six materials, all existing:
`Mat_Paint_White_Arctic` (the issued shell), `Mat_Metal_Steel_Worn` and
`Mat_Metal_Steel_Dark` (collar and hardware), `Mat_Glass_Canopy_Tinted`
(window), `Mat_Neutral_Black_Matte` (the core), `Mat_Neutral_Slate_Dark` (the
colour code), `Mat_Emissive_Portal_Blue` (rim light),
`Mat_Metal_Chrome_Scuffed` (band and neck lip), `Mat_Plastic_Rubber_Black`
(bumper).

## Verified

- `_zverify.py`: **0 clashing pairs, 0.000 m²**.
- Bounds measured off the built file, not the build log: 0.1946 × 0.1422 ×
  0.1422 m.
- FBX re-imported and checked: all three empties survive with their
  coordinates.

## Principles cited

`GDC-L1-UX-0003` (readability and hierarchy; never colour alone),
`GDC-L1-UX-0004` (affordances and signifiers),
`GDC-L1-ANIM-0003` (animation as telegraph),
`GDC-L1-FEEL-0007` (tune for the sensation, not physical accuracy),
`GDC-L1-CONTENT-0003` (naming and organisation conventions).
