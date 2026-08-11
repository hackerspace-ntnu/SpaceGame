# Dune Ornithopter — a flyable wing apparatus carried in the inventory

**Date:** 2026-08-11
**Status:** implemented

> **Outcome note.** The rebuild gate below fired on its second branch. `dune_ornithopter.blend`
> turned out to carry hand edits — non-unit object scales on the cradle pad, both stirrups and the
> fuselage core, plus a mesh left in Edit Mode — so the generator was **not** run over it. The five
> exclusive components were verified reproducible and rebuilt at 10 m; the assembly was scaled in
> place by `dune_ornithopter_rescale.py`. A third case the gate did not anticipate also turned up:
> `shoulder_gear.blend` is shared with the horse and humanoid robots, so it is pinned to its own
> span and the assembly scales what it appends. See `dune_ornithopter_BUILD.md`.
>
> One design change during implementation: `OrnithopterWingAnimator` takes its timestep as a
> parameter (`Tick(dt)`) rather than reading `Time.deltaTime`, so the articulation can be driven
> outside play mode. Reading the clock internally left the damped channels dead in edit mode and
> untestable.

Make `Assets/Models/_Source~/models/vehicles/dune_ornithopter.blend` — the flapping-wing flyer with a prone rider
cradle — into a working prefab the player carries as an inventory item, uses in mid-air, and flies.

## Brief as agreed

| | |
|---|---|
| Use flow | Instant strap-in. Press Use → the craft spawns at the player, wings snap open, player is mounted prone. Escape or landing ends it. |
| Flight model | Energy flight sim — pitch/roll, real airspeed, flapping for thrust, gliding, diving, stalling. |
| Scale | Rebuild at 10 m wingspan, so the prone rider fits the cradle. |
| Launch | Air-only. Usable while airborne, or standing over a drop. Not from flat ground. |
| Rider | Prone, face-down, slung under the belly. |
| Camera | Third person, long boom, follows the craft's pitch and roll. |

## Why the model needs a rebuild first

`Assets/Models/_Source~/models/vehicles/dune_ornithopter_BUILD.md` records the problem: the machine ships at
`TARGET_SPAN = 6.0`, where the cradle board is 1.11 m and a prone adult is about 1.8 m. The rider
does not fit. The BUILD.md prescribes the fix — set `TARGET_SPAN = 10.0` in `models/_ornithopter.py`
and re-run the six component builds plus the assembly; the scale flows through every part and
nothing else needs touching.

**But the generator must not be run blind.** `dune_ornithopter.blend` was last written 3.5 hours
after `dune_ornithopter.py`, and a `.blend1` autosave sits beside it. That is the same signature as
`ostrich.blend`, which had been hand-modelled and which its generator would have destroyed. So the
rebuild is gated:

1. Copy the current `.blend` to a backup outside the build path.
2. Regenerate at 10 m into a scratch location and diff object names, object count, bone names and
   triangle count against the current file.
3. **If it reproduces** — the file is generator-output, so bump `TARGET_SPAN` and rebuild in place,
   as the BUILD.md prescribes.
4. **If it does not reproduce** — the file carries hand edits. Do not overwrite it. Scale the
   existing file in place instead (mesh data and bone positions ×1.667, object scales left at 1.0,
   which is the invariant the BUILD.md relies on), update `TARGET_SPAN` to match so the constant
   does not lie, and say so in the report.

Either path ends with a 10 m machine whose objects all read scale 1.0.

## Architecture

Five units, each with one job.

```
Blender                          Unity edit time                Unity runtime
───────                          ───────────────                ─────────────
dune_ornithopter.blend
  └─ dune_ornithopter_export.py
       └─────────────────────►  dune_ornithopter.fbx
                                  └─ OrnithopterBuilder
                                       └──────────────────────►  DuneOrnithopter.prefab
                                                                   ├─ MountModule + SteerModule
                                                                   ├─ OrnithopterFlightMotor
                                                                   │    └─ OrnithopterFlightModel
                                                                   └─ OrnithopterWingAnimator
                                                                        └─ OrnithopterWingRig
                                                                 WingPack.prefab
                                                                   └─ WingPackItem ──spawns──┘
```

### `dune_ornithopter_export.py` — Blender → FBX

Modelled on `desert_crawler_export.py`, which is the existing precedent for exporting a rigged
machine whose armature is walked live at runtime. It opens the `.blend` read-only and never saves.

- **Localises the palette materials.** They are linked from `Assets/Models/_Source~/palette.blend`, outside
  `Assets/`, and would not resolve from a copy inside it.
- **`add_leaf_bones=False`.** Blender otherwise appends a `<bone>_end` child to every chain tip.
  `OrnithopterWingRig` resolves bones by exact name so a leaf bone is harmless there, but leaf bones
  on five digits per wing plus five tail digits is 22 dead transforms in the prefab.
- **Default axis conversion**, so the model's −Y forward arrives on Unity's +Z. No yaw correction
  anywhere downstream.
- **The skinned panels must survive.** Six of the meshes — two wings and the tail fan, frame and web
  each — are parented to the armature with an Armature modifier and vertex weights, unlike every
  other part which is bone-parented. `use_mesh_modifiers=True` applies modifiers on the way out but
  the FBX exporter deliberately skips armature modifiers when exporting deformation. Verify on the
  produced FBX that those six meshes arrive as `SkinnedMeshRenderer`s with bones bound; a plain
  `MeshRenderer` there means the wings will never deform and everything built on top is wasted.

Output: `Assets/Models/Vehicles/Ornithopter/dune_ornithopter.fbx`.

### `OrnithopterBuilder` — FBX → prefab

`Assets/Editor/Vehicles/OrnithopterBuilder.cs`, menu **Tools ▸ Vehicles ▸ Build Dune Ornithopter
Prefab**. Re-runnable: every position and collider is measured off mesh bounds at build time, so a
re-export with tweaked proportions and a re-run still lands in the right place. Follows
`HorseBuilder` and `ShipRVBuilder`.

Produces `Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab`:

| Node | What it is |
|---|---|
| root | `Rigidbody` (`useGravity = false`), `AgentController`, `MountModule`, `SteerModule`, `OrnithopterFlightMotor`, `OrnithopterWingAnimator` |
| `Model/` | the unpacked FBX instance |
| `SeatPoint` | at `Bone_Cradle`, pitched +90° about X |
| `CameraPivot` | above and slightly behind the cradle |
| `DismountPoint` | below the belly |
| `Collision/` | fuselage box + two wing-root boxes, measured off the meshes |

Three build-time decisions that are not obvious:

- **`useGravity = false`.** The flight model integrates weight itself, as part of the same equation
  that produces lift and drag. Leaving Unity's gravity on as well means two systems pulling the
  craft down and a stall that reads as a brick rather than a wing.
- **The prefab origin sits on the cradle**, not on Blender's origin. The craft rotates about roughly
  where the pilot is, which is what makes pitch and roll read as the rider's own motion.
- **`mountableByDirectInteraction = false`.** You never walk up to this craft and press E. The item
  mounts you, via `TryMount`. Leaving direct interaction on would make every hull collider a
  boarding point — the same problem `MountStation` exists to solve for the ship.

### `OrnithopterWingRig` — bone lookup, nothing else

Resolves the 30 bones of `Arm_DuneOrnithopter` by exact name off the live hierarchy at
`Initialise`, and caches each bone's rest `localRotation`. Mirrors `WalkerRig`, which does the same
job for the legged machines.

```
Bone_Root
└─ Bone_Body
   ├─ Bone_Nose
   ├─ Bone_Cradle                     rider mount point
   ├─ Bone_Shoulder_L/R               FLAP
   │  ├─ Bone_Gear_L/R                gear spin
   │  │  └─ Bone_Crank_L/R            crank throw
   │  └─ Bone_Arm_L/R                 wing sweep
   │     └─ Bone_Digit_L/R_1..5       SPLAY + TWIST
   └─ Bone_Boom_1 → Bone_Boom_2       PITCH
      └─ Bone_TailHub
         └─ Bone_TailDigit_1..5       tail fan SPLAY
```

Caching the rest pose is what lets every motion downstream be expressed as a *delta* on the pose the
artist authored, rather than an absolute rotation that would have to re-derive the rest pose to stay
correct. A missing bone throws at `Initialise` naming the bone, rather than silently posing nothing.

### `OrnithopterFlightModel` — the flight sim, as pure C#

No `MonoBehaviour`, no `Transform`, no `Time`. Takes the current flight state and rider input plus a
timestep, returns the next flight state. This is the only reason the flight behaviour is testable at
all, and it is where every tuning constant lives.

State: airspeed, pitch, roll, flap phase, wing spread, stamina, stalled.

Per step:

- **Airspeed** integrates gravity resolved along the flight path, minus drag, plus flap thrust.
  Nose down and you gain speed; nose up and you bleed it.
- **Lift** = ½ρv²·S·C<sub>L</sub>(α), with wing area `S` scaled by wing spread — folded wings make
  very little lift — and C<sub>L</sub> falling off past the stall angle.
- **Flapping** adds thrust in beats at the flap frequency and spends stamina. Stamina regenerates
  while gliding, so sustained level flight is a rhythm rather than a held button.
- **Turning** is coordinated: bank angle produces yaw rate. The tail fan adds authority, so the
  turn is not purely a function of roll.
- **Stall**: below the minimum airspeed, lift collapses, the nose drops, and control authority fades
  until speed is recovered. A stall is recoverable by diving, which is the point.

### `OrnithopterFlightMotor` — the MonoBehaviour shell

`IRiderControllable` + `IMovementMotor`. Owns the `Rigidbody`.

Rider input is **latched in `ApplyRiderInput` and consumed in `FixedUpdate`**, exactly as
`FlyingRigidbodyMotor` does, and for the same reason recorded there: `ApplyRiderInput` is called on
the render loop, but `MoveRotation` and `linearVelocity` are only meaningful per physics step.
Writing the body from the render loop makes the per-step advance uneven, and a follow camera that
subtracts that pose every frame turns the unevenness straight into shake.

Input mapping — chosen so that **neither `SteerModule` nor the input asset needs to change**:

| `RiderInput` field | Bound to | Means here |
|---|---|---|
| `Move.y` | W / S | pitch — nose down / nose up |
| `Move.x` | A / D | roll — bank left / right |
| `Vertical` | Space / Left Ctrl | flap effort when positive, tuck and dive when negative |

`Vertical` already exists as an action; it was added for ShipRV. `SteerModule` needs only
`verticalActionName = "Vertical"` set on the prefab, with jump and leap off.

Publishes read-only flight state — airspeed, flap phase, flap effort, wing spread, bank angle, turn
input, stalled, grounded — for the animator, and raises `Landed` on ground contact.

### `OrnithopterWingAnimator` — the articulation

`[DefaultExecutionOrder(200)]`, poses the rig in `LateUpdate` from the motor's flight state. Every
motion is a signed delta on the cached rest rotation, on the axis the BUILD.md specifies:

| Motion | Bone | Axis | Per-side sign? |
|---|---|---|---|
| Wing beat | `Bone_Shoulder_L/R` | local **X** | **No** |
| Wing sweep | `Bone_Arm_L/R` | local **Z** | **Yes** |
| Digit splay | `Bone_Digit_*` | local **Z** | **Yes** |
| Digit twist | `Bone_Digit_*` | local **Y** (roll) | **No** |
| Gear spin | `Bone_Gear_L/R` | local **Y** | either |
| Tail fan splay | `Bone_TailDigit_1..5` | local **Z** | n/a |
| Boom pitch | `Bone_Boom_1/2` | local **X** | n/a |

The rule behind the table, quoted from the build record because getting it wrong is a real bug that
already happened twice during the model build: the wing bones point outboard in **opposite**
directions, so their local X and Y are already mirrored and the *same* angle on both sides produces
a symmetric result. Local Z points up on both sides and is *not* mirrored, so anything about Z needs
an explicit per-side sign — or one wing opens while the other closes.

What each motion is driven by:

- **Flap** — shoulder X = amplitude·sin(2π·phase), amplitude scaled by flap effort. The gears spin
  at flap frequency and the cranks follow, so the visible mechanism agrees with the wings instead of
  being decoration.
- **Spread** — `wingSpread` 0→1 opens digit splay from folded to full and sweeps the arms forward.
  Plays eased over 0.6 s at launch and reverses on despawn. This is the "spread out when starting to
  fly" animation.
- **Digit twist** — positive on the downstroke so the membrane bites, slightly negative on the
  upstroke so it feathers rather than pushing the craft back down. This is what makes a flap read as
  propulsion rather than as flapping in place.
- **Tail** — boom pitch tracks pitch input; the tail digits splay open in turns and under braking,
  asymmetrically with roll and turn input. This is the back wing that turns the machine.
- **Bank** — differential digit twist between the two wings, on top of the boom.

**Everything is driven off flap phase, never off a filtered velocity.** This is the lesson recorded
from the ostrich gait work: nothing at stride frequency may go through a smoothing filter, or the
motion detunes from the thing it is supposed to be showing.

### `WingPackItem` — the inventory side

`WingPackItem : UsableItem`, on `Assets/Prefabs/items/WingPack.prefab` (folded wings, held in hand),
driven by `Assets/Resources/Items/Artifacts/WingPack.asset` (`InventoryItem`), which is added to the
player prefab's `startingItems` so the player carries it from the start.

- **`CanUse()`** — true only when the player is airborne, **or** a downward ray finds no ground
  within `minLaunchClearance` (default 6 m), which is what makes standing on a cliff edge count as
  "off a ledge". A refused use logs and gives feedback rather than failing silently.
- **`Use()`** — instantiates the craft at the player, nose along their look direction, seeded with
  the player's current velocity so a running jump carries through into the launch. Calls
  `MountModule.TryMount` with the player's `Interactor`, and starts the spread animation.
- **Teardown** — subscribes to the craft's `Dismounted` and the motor's `Landed`; either despawns
  the craft and returns the player to their feet. `maxUses = -1`, so the pack is not consumed.

## The one change outside the feature

`MountModule`'s third-person camera builds its orbit from yaw alone —
`Quaternion.Euler(0f, cameraYaw, 0f)` in `MountModule.Camera.cs`. That is correct for ground
vehicles and wrong for flight: the camera would stay level while the craft dives or rolls, so the
horizon never tilts and a dive reads as the ground rising rather than the pilot pitching.

Add an opt-in `followMountPitch` bool, **defaulting to off**, that builds the orbit rotation from
the mount's full rotation instead of its yaw. Every existing mount keeps its current behaviour
byte-for-byte; the ornithopter turns it on. Camera tuned for a 10 m machine: pivot above the cradle,
11 m boom, 14 m look-ahead, softer follow lerp than a ground vehicle wants.

## Testing

- **`OrnithopterFlightModelTests`** (EditMode) — the flight model is pure C#, so its behaviour is
  asserted directly: a glide loses altitude while roughly holding speed; a dive gains speed; flapping
  climbs; below stall speed lift collapses; bank produces yaw; stamina depletes under sustained
  flapping and recovers in a glide.
- **`OrnithopterRigWiringTests`** (Editor) — loads the built prefab and asserts every bone
  `OrnithopterWingAnimator` needs resolves, and that the six webbed panels arrive as
  `SkinnedMeshRenderer`s. Shaped after the existing `HorseRigWiringTests`.
- **Play mode** — launch, glide, flap, turn, stall, land, verified through the unity-mcp bridge with
  screenshots.

## Files

**New**

```
Assets/Models/_Source~/models/vehicles/dune_ornithopter_export.py
Assets/Models/Vehicles/Ornithopter/dune_ornithopter.fbx
Assets/Editor/Vehicles/OrnithopterBuilder.cs
Assets/Scripts/Flight/OrnithopterWingRig.cs
Assets/Scripts/Flight/OrnithopterFlightModel.cs
Assets/Scripts/Flight/OrnithopterFlightMotor.cs
Assets/Scripts/Flight/OrnithopterWingAnimator.cs
Assets/Scripts/Flight/README.md
Assets/Scripts/Item/WingPackItem.cs
Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab
Assets/Prefabs/items/WingPack.prefab
Assets/Resources/Items/Artifacts/WingPack.asset
Assets/Tests/EditMode/OrnithopterFlightModelTests.cs
Assets/Editor/Tests/OrnithopterRigWiringTests.cs
```

**Modified**

```
models/_ornithopter.py                              TARGET_SPAN 6.0 → 10.0
Assets/Models/_Source~/components/**, Assets/Models/_Source~/models/vehicles/dune_ornithopter.blend   rebuilt at 10 m
Assets/Models/_Source~/models/vehicles/dune_ornithopter_BUILD.md    record the rebuild and the Unity export
Assets/Scripts/agents/modules/MountModule.cs        followMountPitch field
Assets/Scripts/agents/modules/MountModule.Camera.cs followMountPitch behaviour
Assets/Prefabs/.../Player prefab                    WingPack in startingItems
```

## Out of scope

- Ground takeoff. The launch is air-only by decision; there is no taxi state and no ground roll.
- AI-flown ornithopters. `OrnithopterFlightMotor` implements `IMovementMotor` so a module *could*
  fly one, but no behaviour module is wired up and none is tuned for 3D flight.
- Networking. The craft is spawned locally by the item, like every other mount in the project.
- Damage, fuel, or durability. The pack has unlimited uses.
