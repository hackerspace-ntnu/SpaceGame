# Sandloper — build record

A large, rideable desert rodent: the dune rat's silhouette at twice the size,
with four times the geometry, a countershaded coat and a jump.

Rebuild:
`blender --background --python models/creatures/sandloper.py -- --out models/creatures/sandloper.blend`
then `... --python models/creatures/sandloper_export.py`.

## It is built from the FBX, and that is not a workaround

`models/creatures/dune_rat.blend` **cannot be opened by Blender 4.2**, the only
Blender installed here — *"not a blend file"*, exactly like `palette.blend` and
`components/props/supply_crate.blend`. It was written by a newer Blender.

That alone would have forced the FBX route, but the better reason is ownership:
the dune rat's mesh and skeleton were **hand-authored by Tobias Fremming**.
Reading the shipped FBX into a new file means no code path in this build can
touch the original, whatever it does. Nothing under `DuneRat/` is written.

## Detail: subdivision, not a remesh

A remesh throws away vertex groups and UVs, and with them the rig and all six
animations. Catmull-Clark subdivision keeps both — weights interpolate onto the
new vertices, UVs come along — so the animal goes from 3,694 to **14,732 tris**
and still walks.

## Colour: countershaded, by facing and not by height

Five palette hides, assigned per face. Two earlier attempts are worth recording
because both are visible failures:

1. **Inverted.** Pale body over saturated yellow legs — it read as a plastic toy.
   Real desert animals are dark on top and pale underneath.
2. **Cut at a flat height.** A horizontal boundary saws across a curved animal
   and stair-steps through his ribs.

What works is height *and* facing together: `BACK` is the sunlit surface at or
above the spine (z ≥ 0.93 with a normal pointing up), `BELLY` is what faces the
sand, `FLANK` is the turn between them. Facing alone was tried too and put tan
patches on the haunches — every upward-facing thigh face read as "back" and he
came out dappled.

| Zone | Hide | Where |
|---|---|---|
| Back | `Mat_Hide_Plate_Tan` | the dorsal stripe |
| Flank | `Mat_Hide_Dune_Tan` | the sides |
| Belly | `Mat_Hide_Ivory_Spine` | underside, inner limbs |
| Claw | `Mat_Hide_Claw_Horn` | crest spines, feet, tail tip |
| Ear | `Mat_Hide_Slate_Teal` | ear cups, the one cool note |

## Gait: amplified, not re-authored

The rat's clips were built for a nervy 1.26 m animal and read as a shuffle on
something 5 m long — "so small they are barely visible". `amplify()` scales each
rotation curve **about its own rest value** by `GAIT_GAIN = 1.75`, so the
footfall frames, the duty cycle and the phase offsets between limbs are all
still exactly where the author put them. Only the arcs widen.

Location curves are deliberately untouched: the feet are IK targets and their
positions are what put the toes on the sand.

**`GAIT_GAIN` and `SandloperBuilder.GaitGain` are one decision in two files.** A
leg sweeping 1.75x further covers 1.75x the ground per cycle, so the Unity
speeds carry the same factor. Change one without the other and the feet skate.

## Sandloper_Jump

Authored here, not inherited — the rat has no jump. 30 frames: coil, push, tuck,
reach, absorb, strictly **in place**. `NavMeshAgentMotor` supplies the height by
animating the agent's `baseOffset`; keying a rise here as well would double it.

**Set `use_fake_user`.** An action nothing points at has zero users and Blender
purges it on save. The first build reported seven actions and reloaded with six,
and the export shipped no jump take at all.

## Saddle

`Coll_Saddle_Loper` in `models/gear/saddle.blend`, fitted to his measured back
and flank. He is 0.11 m half-width at the spine where Appa is 0.66, so nothing
about that saddle scales onto this one — the difference is shape, not size. His
file is -Y forward like the library standard, so unlike Appa there is no
rotation between his frame and the saddle's.

The empties are suffixed `_Loper` because both saddles live in one `.blend` and
`_buildlib` refuses to save a file where Blender has auto-renamed a collision.

## Not done

No attack use: he keeps the rat's `Sandloper_Attack` clip but `SandloperBuilder`
gives him no `CloseCombatModule`, because he is a mount rather than a predator.
The clip is there for whoever wants to make him defend himself.
