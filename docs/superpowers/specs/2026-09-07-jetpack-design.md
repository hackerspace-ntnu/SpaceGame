# Jetpack — design

Date: 2026-09-07
Status: approved, implementing

A worn back item that flies the player's own body on four vectoring nozzles, limited by heat
rather than by fuel. The third `EquipKind.Back` item, so it is mutually exclusive with the wing
pack and the wingsuit for free: there is one torso slot and all three want it.

## Why this shape

The wingsuit already proved the pattern for "a back item that flies the player's own body":
nothing is spawned, nothing is mounted, the astronaut *is* the aircraft, and the item's job is the
gesture plus handing `PlayerMovement` and `PlayerLook` back intact. The jetpack reuses that
skeleton and changes what happens in the middle.

Two things make it a different machine rather than a reskin:

- **It takes off from standing.** The wing pack and the wingsuit are air-only and refuse on the
  ground. The jetpack's whole fantasy is vertical takeoff, so a double tap of Space on flat sand
  is a legal, and the default, way to start.
- **Thrust comes from where the nozzles point, not from what you pressed.** The nozzles are
  rate-limited toward the commanded angle, so the machine goes where it is already aimed. That lag
  is the learning curve (`GDC-L1-DESIGN-0005`: depth from interaction, not from added rules — one
  rate limit produces every skill expression the jetpack has).

## Control seam

While flying, `PlayerMovement.SetGliding(true)`. That is the existing flag for "something else owns
all three axes and its own gravity, but keep the ground probe and the animator running". No
walking, no air control, no fall damage from the movement side.

`PlayerLook.SetFlying` stays **false**. The mouse is still the ordinary look and still yaws the
body, because where you look is meant to steer the pack. The nozzles are expressed in body frame,
so turning the body swings the thrust — that is the entire yaw model, and there is no rudder.

## The vector chain

Three layers. Layer 2 is the design.

```
1 COMMAND   base is straight down (thrust up)
            W / S / A / D rake the nozzle target in the body's horizontal frame
            PlayerLook.Pitch folds in:  look down + W = flat and fast
                                        look up   + W = steep climb
2 NOZZLE    rate-limited toward the command
            vectorRate ~110 deg/s, maxDeflection ~40 deg
            thrust goes where the nozzles ALREADY point
3 THRUST    nozzleForward * thrust * throttle, on the Rigidbody in FixedUpdate
```

Left and right pods deflect asymmetrically for A/D, so a sideways move reads as the machine
leaning rather than sliding (`GDC-L1-ANIM-0003` — the animation is the feedback that the input
landed).

## States

```
STOWED               worn, inert; the player walks
FLYING
  THRUST    Space    heat +6.7 /s  -> 15 s from cold
  LEVITATE  default  heat +4.0 /s  -> 25 s from cold; hover servo cancels gravity,
                                      nozzles return to vertical
  CUT       LeftCtrl heat -10  /s  free fall, and the only way to cool
OVERHEAT             heat 100 -> hard cut and latch; relight only at heat <= 20
```

`LeftCtrl` is the cut, matching the wingsuit's tuck on the same key. Cutting and coasting is how a
skilled pilot extends a flight past either budget, which is what "usage on and off can keep it
going longer" asks for.

Enter: double Space anywhere, ground included, plus an upward impulse — the boost.
Leave: double Space again, landing, death, unequip, `OnDisable`.

Landing bills closing speed through the shipped `OrnithopterCrash.ImpactDamage` (safe 9 m/s,
lethal 30). An overheat at altitude therefore punishes itself with no new rule, and the two ways a
flight can end measure the same quantity.

## Presentation

- **Flames.** `JetFlame.shader` on the four exhaust meshes already in `jetpack.blend`
  (`Mesh_Jetpack_ExhaustInner/Outer`, two per pod). Unlit, hard colour bands (white core, orange,
  deep red edge), scrolled along the exhaust axis, threshold-cut noise so it flickers as blobs
  rather than as smooth fire. Length and width driven per-renderer from throttle. Stylized to sit
  with the shipped pastel-quantize look; no particle system for the flame itself.
- **Overheat glow.** Emissive red ramp on the nozzle cones, driven by heat through a
  `MaterialPropertyBlock`. No new material instances, so nothing to leak.
- **Smoke.** One `ParticleSystem`, playing with emission *disabled*, `Emit()` called per puff,
  `AlwaysSimulate`, Local scaling — the project's manual-emit pattern. Flat grey quads, few, slow,
  growing as they rise. Rate rises with heat above ~70%.
- **Pose.** `JetpackPose` tilts the hips from measured motion, at execution order 920, before
  `PlayerHeadLook` — the wingsuit's arrangement and for its reason. No animation clip and no
  animator parameter: the deviation is measured, and there is nothing for a remote to get wrong.

### The one addition beyond the brief

A **visor heat gauge** through `IVisorGaugeSource`, the interface oxygen and health already use.
The red nozzles are behind the wearer: in first person the diegetic read is invisible to the only
person who needs it, and misreading heat is what kills you (`GDC-L1-UX-0003`). The gauge hides
itself when no jetpack is worn.

## Multiplayer

**No new message and no new animator parameter.** `UseChannel.Release` returns early for an item
that is `IsContinuous` and `WantsHold`, so the double tap's `Press(); Release();` starts a 15 Hz
hold stream that outlives the press and ends when the item says it is done. The jetpack streams
for exactly the length of a flight.

Each tick carries what a peer cannot derive:

- `NetArg.P` = (throttle, heat 0..1, 0)
- `NetArg.R` = the nozzle deflection, which is literally a rotation

`PresentHold` fires on every machine, so `JetpackNozzles` reads it and drives the parts, the
flames, the glow and the smoke everywhere. The keepalive re-sends at least every 0.2 s, so a late
joiner is correct within a fifth of a second rather than never.

`JetpackFlight` is added on the owner only — the player's `NetworkTransform` is owner-authoritative,
so a flight simulated anywhere else is a second, divergent flight fighting the replicated one.
`JetpackPose` and `JetpackNozzles` run everywhere and measure motion off the transform, because a
remote body is kinematic and reads zero velocity anywhere but at home.

`UseAuthority.Owner`, for the reason `UsableItem` documents for this shape: a server-applied
velocity is overwritten by the owner, silently.

The dropped prefab is registered in `DefaultNetworkPrefabs.asset`.

**Verify on a real client:** take off past the host, watch the pods vector, the flames scale with
throttle, the tips go red and smoke at overheat, and the fall when it cuts.

## Persistence

The pack itself is saved by the torso slot (`BodyEquipmentSaveable`, key `body`) like any gear.
Two things ride the item's `ItemState`:

- **`heat`** — otherwise a quicksave is free coolant.
- **`jet`** — a flight in progress (velocity), plus the state and the overheat latch, resumed
  through `IItemDeferredRestore.TryCompleteRestore` entering *already flying*. Without it a
  mid-air quicksave reloads standing still in the sky, which is the mistake
  `OrnithopterSaveable` made first and the wingsuit fixed.

Dropped on the sand it carries `SaveableEntity` + `TransformSaveable` with a stamped `prefabId`.

## Inventory

`Jetpack.asset` in `Resources/Items/Artifacts/`, `equipKind: 2` (Back).
`ItemGrip.packSize` **1.8** — the wing pack's bracket, so it costs about as much space as the
ornithopter. `holdSize` from the item model's measured pair width.

`WornVisual`: `Carried` is `Coll_Jetpack_Item` (the two motors side by side), `Worn` is
`Coll_Jetpack_Worn` (a pod on each lash-rail tip). Both collections already exist in the .blend;
`jetpack_export.py` is new.

## Files

```
Scripts/Gear/Jetpack/                       own asmdef
  Flight/JetpackConfig.cs                   every tunable
  Flight/JetpackHeat.cs                     pure: the heat state machine
  Flight/JetpackVector.cs                   pure: input + look -> command -> rate-limited nozzle
  Flight/JetpackStep.cs                     pure: one physics step -> new velocity
Scripts/Characters/Player/Movement/
  JetpackFlight.cs                          owner only, order 150
  JetpackPose.cs                            every machine, order 920
Scripts/Items/Equipped/
  JetpackItem.cs                            UsableItem, UseAuthority.Owner
  JetpackNozzles.cs                         every machine: 3 parts per pod, flames, glow, smoke
Scripts/Presentation/UI/HelmetHUD/
  JetpackHeatGaugeSource.cs                 IVisorGaugeSource
Editor/Items/JetpackBuilder.cs              Tools > SpaceGame > Items > Build Jetpack
Art/Shaders/Effects/JetFlame.shader
Editor/Tests/JetpackHeatTests.cs  JetpackVectorTests.cs  JetpackStepTests.cs
Art/Models/_Source~/models/gear/jetpack_export.py
```

`PlayerInputManager` gains `JumpHeld`, mirroring the existing `CrouchHeld`. The jetpack needs to
ask whether Space is *down*, and today only the press is published.

## Model note

The pods are yawed −90° about Z on their mounts so the struts run inboard along the lash rail.
Measured extents: worn pair 1.998 × 0.394 × 0.650 m, item pair 0.944 × 0.394 × 0.650 m — so
`holdSize` is 0.94.

The Unity side is agnostic to the pod's authored orientation: it resolves the nozzle parts **by
name** and rotates each **relative to the `localRotation` captured in `Awake`**, because exported
empties keep their authored rest rotation and a yaw that changes in Blender must not become a
rotation offset baked into C#. What it does assume is that a pod's parts hang under an empty whose
own forward is the *thrust* direction at zero deflection; `JetpackNozzles` finds that axis by
measuring the nozzle cones against the housing rather than naming an axis, for the reason the
wingsuit's spar pays for.

## Tests

EditMode, in `Assets/Game/Editor/Tests/` — they touch `Assembly-CSharp` types, and the
`Tests/EditMode` asmdef cannot reference that assembly.

- `JetpackHeatTests` — 15 s at full thrust, 25 s levitating, cut cools, hard cut at 100, no relight
  above 20, the latch survives a state change.
- `JetpackVectorTests` — the nozzle lags the command at the rate limit; deflection is clamped;
  look pitch folds in; thrust follows the nozzle, never the key.
- `JetpackStepTests` — levitate holds altitude; cut is free fall; no energy is minted by waggling
  the input.
