# Ostrich model update, neck motion, and file restructure

Date: 2026-08-10

Three deliverables: bring the updated hand-modelled ostrich into the prefab via a full re-rig,
add autonomous look-around neck movement, and move the ostrich out of the "vehicle" folders it
does not belong in.

## Background — what is actually stale

`Ostrich.prefab` instantiates `ostrich_rigged.blend` (guid `97ea118a0e184c21a51c7ee1fa32ec7f`,
last written 2026-08-09 02:27). That file still carries the **old** neck
(`OSTRICH_NeckSegments`, `OSTRICH_NeckCollars`) and the **old** head (6 meshes).

`ostrich.blend` (2026-08-10 00:38) is the art source and has moved on:

- the new neck — collection `NECK_Neck`, 52 objects across `NECK_Vertebrae` / `NECK_Joints` /
  `NECK_Tendons` / `NECK_Actuators` / `NECK_Base` / `NECK_HeadMount` / `NECK_Wiring`
- armature `NECK_Rig`, 14 bones: `NeckBase`, `Neck_01..Neck_11`, `Head`, `Jaw`
- the old head quarantined into `OSTRICH_Head_OLD` (hidden), the old neck segments deleted
- two new head parts, `Cube.014` and `Cube.015`

**`ostrich.blend` cannot be used by the prefab directly: it has no leg rig.** `OSTRICH_Rig` — the
armature `OstrichLocomotion` drives through `WalkerRig` — exists only in `ostrich_rigged.blend`.
Pointing the prefab at the art file yields a bird with no gait.

## 1. Model — full re-rig

### Why a re-rig rather than a graft

A graft (new neck onto the existing rigged file) is less work, but it silently drops any body edit
made to the art file since 2026-08-09 02:27. A re-rig picks up all art edits. The usual objection —
that rebuilding a rig from scratch means re-deriving and re-verifying the whole gait — does not
apply here, because the rig does not have to be *derived*. It can be *reproduced*.

### The load-bearing fact

`ostrich.blend` and `ostrich_rigged.blend` share one world coordinate frame, verified:

| Check | Art file | Rigged file |
|---|---|---|
| `OSTRICH_Pelvis` bounds min | `[-1.0, -1.325, 7.28]` | `[-1.0, -1.325, 7.28]` |
| `OSTRICH_Pelvis` bounds max | `[1.4, 1.325, 8.65]` | `[1.4, 1.325, 8.65]` |
| Neck chain start (`Neck_01` head) | `[5.5377, 0, 10.234]` | `Spine` tail `[5.53771, 0, 10.234005]` |
| `Head` bone head | `[7.6949, 0, 16.5222]` | `[7.694908, -0.00084, 16.522242]` |
| `Head` bone tail | `[8.8949, 0, 16.7222]` | `[8.894908, -0.00084, 16.722242]` |

`OSTRICH_Rig`'s object matrix is world identity. So every bone can be rebuilt at a literal
coordinate taken from the file whose gait is already tuned, and the new neck's endpoints already
land exactly on the leg rig's `Spine` tail and old `Head` bone. Nothing needs repositioning.

### Target skeleton

```
OSTRICH_Rig  (object matrix = identity)
  Root                                     [0.2, 0, 7.8] -> [0.2, 0, 8.8]
  ├ Coxa_L   Root      roll  0.000         [0.2,  1.15, 8.6] -> [0.2,  1.15, 7.8]
  │  └ Hip_L           roll  0.469         -> [-1.55,  1.15, 4.35]
  │     └ Knee_L  (connected) roll -0.592  -> [0.4,  1.15, 1.45]
  │        └ Ankle_L (connected) roll -0.018 -> [0.4261,  1.143217, 0.000059]
  │           └ Foot_L roll 0.000          -> [1.4261,  1.143217, 0.000059]
  ├ Coxa_R   ... mirrored on y
  └ Spine    Root      [0.2, 0, 8.4] -> [5.53771, 0, 10.234005]
     └ NeckBase        <- from NECK_Rig, reparented to Spine, NOT connected
        └ Neck_01 .. Neck_11 (connected chain)
           └ Head
              └ Jaw    (not connected)
```

Old `Neck_01..Neck_05` and the old `Head` bone are replaced wholesale by the `NECK_Rig` chain.
Bone count on the neck goes 5 + head -> 11 + base + head + jaw.

### Build steps

1. Delete `OSTRICH_Head_OLD` (6 quarantined meshes) outright — hidden collections are not a
   reliable export exclusion.
2. Create `OSTRICH_Rig` at world identity; add the leg/root/spine bones from the dumped table
   (exact head, tail, roll, parent, connect flags); append the `NECK_Rig` chain under `Spine`.
3. Append collection `OSTRICH_Rig_Parts` from `ostrich_rigged.blend` — the 8 `COL_*` collision
   proxies plus `CoxaPin_L/R` and `FootPin_L/R`. These four pins and the proxies were synthesised
   by the original rig pass and do not exist in the art file. The other pins (`LEG_HipPin`,
   `LEG_KneePin`, `LEG_AnklePin`, and their `.001` mirrors) **do** exist in the art file already.
4. Bone-parent every object using the name -> bone map dumped from the rigged file (207 entries).
   `NECK_*` objects use the bone they are already parented to in the art file. `Cube.014` and
   `Cube.015` are new and go to `Head` (see Open questions).
5. Re-point the 14 skinned neck meshes' armature modifiers to `OSTRICH_Rig`. Their vertex groups
   (`NeckBase`, `Neck_01..Neck_11`, `Head`, `Jaw`) all exist on the new skeleton, so weights carry
   over untouched.
6. Delete `NECK_Rig`.

### The FBX rules this must not break

From the previous pass, all still load-bearing:

- **FBX has no `matrix_parent_inverse`.** Anything parked there is dropped on export. Every one of
  the 207 bone-parented objects in the current rigged file has an identity `matrix_parent_inverse`;
  the new file must too.
- Geometry is baked to world space, object transform left at identity, and the bone-relative
  transform put in `matrix_basis` only. With `matrix_parent_inverse = I` and bone parent matrix
  `P = arm.matrix_world @ bone.matrix_local @ Translation((0, bone.length, 0))` (Blender parents to
  the bone **tail**), the requirement `matrix_world = I` gives `matrix_basis = P⁻¹` — a rigid bone
  inverse, which survives export.
- **Apply modifiers before baking**, or bevel widths re-evaluate against rescaled geometry and
  quietly change the art.
- Use `mesh.transform(world)`, not `bpy.ops.object.transform_apply`, which mishandles delta
  transforms.
- Do not measure from `ob.bound_box`; it is cached and goes stale after the geometry under it is
  rewritten. Measure from `mesh.vertices`.

### Verification gates, before the prefab is touched

- `WalkerRig.Build` finds exactly **2** legs, neither skipped for a missing coxa or a zero-length
  segment, and no "pitch pins are not parallel" warning.
- Measured `UpperLength`, `LowerLength`, `FootLength` and `MaxReach` match the current rig. This is
  what keeps `rideHeight = 0.0206`, `hipHeightFraction = 0.86` and the rest of the tuning valid.
- Zero objects with a non-identity `matrix_parent_inverse`.
- Per-object world-space vertex bounds compared against the old rigged file; the only differences
  should be the neck, the head, and the two new parts.
- Rendered preview inspected.

## 2. `OstrichNeckMotion` — autonomous look-around

`OstrichSpineMotion` stays exactly as it is. It already owns gait-locked peck, turn counter-swing,
look-toward-travel and head stabilisation, and it is tuned. The new component runs **after** it
(execution order 160 vs 150) and *adds* rotation on top rather than resetting to rest, so the two
compose instead of fighting. It can be disabled independently.

Not player-steerable, by explicit instruction.

| File | Job |
|---|---|
| `OstrichNeckMotion.cs` | The component: serialized fields, chain resolution, per-frame order |
| `OstrichNeckGaze.cs` | Attention model — where to look, when to switch, how long to hold |
| `OstrichNeckSpring.cs` | Ride bounce — the neck as a spring driven by body acceleration |

`OstrichNeckGaze` and `OstrichNeckSpring` are plain classes inside the `SpaceGame.Vehicles.Ostrich`
asmdef with no `UnityEngine` component dependency, so EditMode tests can drive them directly — the
same reason `OstrichSteering` is a separate pure class.

### Gaze

An ostrich does not sweep its head; it snaps to a heading and holds. So the model is saccadic:

- pick a new gaze heading (yaw + pitch offset) on a randomised interval
- slew to it fast
- **hold** it for a dwell period, then pick again
- occasional larger "startle" turns, and occasional low glances

A continuous ease between targets reads as a periscope, which is the failure mode to avoid.

### Ride bounce

The neck lags and overshoots the body's motion — the whippy quality of riding one. Driven by the
body's measured vertical and lateral acceleration through a spring-damper, producing a bend offset.

**This is a resonator, not a filter.** The standing rule on this rig is that nothing at stride
frequency may go through a first-order smooth, because it costs amplitude and phase at exactly the
frequency it is meant to pass. The spring is a second-order system driven by acceleration; it is
allowed to lag, because the lag is the effect being modelled. The *input* is not pre-smoothed.

### Bend distribution

`OstrichSpineMotion.SpreadOverNeck` shares a rotation evenly across the chain. With 11 vertebrae
that reads as a hose. The new component uses a weight curve along the chain instead, so large
sweeps are paid for near the base and fine gaze adjustments near the head.

### Jaw

The `Jaw` bone opens occasionally off the idle timer — a beak-open idle. Small, low frequency.

## 3. File structure

The ostrich is a robot creature, not a vehicle. Target layout:

```
Assets/Models/Environment/creatures/
    animals/            Ant.fbx, Spider.fbx      (moved from entities/animals/)
    robots/Ostrich/     ostrich.blend            (art source)
                        ostrich_rigged.blend     (imported by the prefab)
                        ostrich_neck.blend       (neck component library)

Assets/Prefabs/agents/creatures/Ostrich.prefab

Assets/Scripts/Creatures/
    OstrichDriver.cs                 stays in Assembly-CSharp, outside the asmdef, because it
                                     implements IRiderControllable / IMovementMotor
    Ostrich/
        SpaceGame.Vehicles.Ostrich.asmdef        name unchanged, so the test asmdef is untouched
        OstrichLocomotion.cs  (+ .Rig .Gait .Body .Ik)
        OstrichSpineMotion.cs
        OstrichNeckMotion.cs  OstrichNeckGaze.cs  OstrichNeckSpring.cs
        OstrichSteering.cs
```

Every move carries its `.meta` file, so GUIDs — and therefore every prefab and scene binding —
survive. In particular `OstrichLocomotion.cs` keeps guid `fdb87fb2bba7844c3a62ea2d74a6f3af`, which
is what the prefab binds to; that file is moved, never recreated.

Also in scope:

- delete the `.blend1` files sitting inside `Assets/` — they are Blender autosaves, already
  gitignored, and should never have been there
- move `ostrich_build.py` out of `Assets/`. It opens with `wipe()`, which deletes every object in
  the `OSTRICH*` collections, so running it destroys the hand-modelled bird
- move `backups/` out of the repository root

### Prefab

The model GUID does not change, so the prefab's reference to it survives the move. The bone
hierarchy *does* change (5 neck bones -> 14), which invalidates the 8 `m_Enabled: 0` overrides that
hide the `COL_*` collision boxes, since importer fileIDs are derived from the hierarchy.

This is cosmetic only. `WalkerRig.LowestRendererPoint` skips renderers whose name starts with
`COL_` (`WalkerRig.cs:292`) and passes `true` for inactive renderers, so the sole contact
measurement is correct whether or not those renderers are enabled. The overrides get re-applied
after import; if any cannot be resolved, the bird is still correct, just with visible collision
boxes.

## Out of scope

- Renaming the assembly `SpaceGame.Vehicles.Ostrich` -> `SpaceGame.Creatures.Ostrich`.
- Re-categorising `Ant.fbx` / `Spider.fbx` as robots. They keep the categorisation they have.
- Folding `SpiderWalkerLocomotion`'s duplicated ground-probing onto `WalkerGround`.

## Outcome

### Resolved: the two new head parts

`Cube.014` and `Cube.015` turned out to be a hand-modelled **beak**, replacing the deleted
`NECK_HEAD_BeakUpper`. One block cut in two at z=16.70: `Cube.014` is the upper mandible
(z 16.704..17.266) and `Cube.015` the lower (z 16.135..16.697), the latter occupying the same
volume as `NECK_HEAD_Jaw`. Confirmed by rendering both tinted with the jaw open.

So `Cube.014` -> `Head` and **`Cube.015` -> `Jaw`**. The conservative "both on Head" guess would
have left the beak unable to open, with the mandible swinging out from behind a static lower beak.

### Verification results

Rig rebuild, against the art file as ground truth:

| Check | Result |
|---|---|
| Shared meshes compared | 229 |
| Meshes drifted > 2 mm | **0** |
| Objects with non-identity `matrix_parent_inverse` | **0** |
| Segment lengths | `upper 3.868462`, `lower 3.494639` — identical to the old rig |
| Sole contact | `FootPin_L/R` at z = −0.149941 — identical to the old rig |
| Deformation | `Jaw` moves 2 objects; `Neck_06` moves the 27 above it; `Knee_L` moves 39 |

In Unity, on the real prefab:

- `LegCount = 2`, `IsReady = true`, `MaxSpeed = 8.670635` — the same top speed recorded when the
  gait was originally tuned, so `rideHeight` and `hipHeightFraction` remain valid.
- Planted-foot slip **0.00000 m** at 0, 0.35, 0.7 and 1.0 speed. Worst reach fraction 0.79–0.93.
- 11 neck bones, `Head` and `Jaw` all resolved; no NaN anywhere.

### Three defects found and fixed during verification

1. **`COL_Head` lost its BoxCollider and its renderer override.** The 8 `COL_*` proxies carry the
   bird's physics colliders as prefab additions keyed to importer fileIDs. Seven survived; the
   eighth did not, because the `Head` bone moved from under `Neck_05` to under `Neck_11` and its
   fileID changed with it. Rebuilt from the mesh's own local bounds, matching the other seven.
2. **`OstrichSpineMotion` threw on any re-import that changes the bone count.** Its `Resolve()`
   only rediscovered when the array was empty, so a stale 5-entry array of dangling references
   sailed past the check and then threw on `neckBones[i].localRotation`. Both it and
   `OstrichNeckMotion` now rediscover when any entry is null.
3. **`OstrichSpineMotion.Resolve()` did not pick up its component wiring**, so resolving and then
   stepping from an EditMode test NREd on `locomotion`. It now mirrors
   `OstrichLocomotion.Initialise()` and caches the wiring too.

### Bounce tuning, measured rather than guessed

The first implementation drove the spring from `locomotion.MeasuredVelocity` and produced **1.27
degrees** of bend — invisible. That was a design error: `MeasuredVelocity` is the path velocity and
deliberately excludes the bob, but the bob is exactly the motion a rider feels. Driving from the
body transform instead roughly doubles the available signal (11.6 m/s² vertical at half speed
against 5.2).

Gain was then chosen by sweeping it against the real acceleration trace:

| gain | walk (0.35) | canter (0.7) | sprint (1.0) |
|---|---|---|---|
| 12 | 1.0° | 2.7° | 3.5° |
| 20 | 1.6° | 4.5° | 5.8° |
| **32** | **2.6°** | **7.3°** | **9.3°** |
| 50 | 4.1° | 11.4° | 14.5° (clamped) |

32 was taken: clearly visible, scales with speed, and stays clear of the 14° clamp so the bounce
never sits pinned with nowhere left to express.

Two guards were added after the sweep, both covering discontinuities this project actually
produces — `SnapToGround` on spawn, and `WorldStreaming` migrating entities between scenes:
`maxDrive` (70 m/s², just above the 54 a sprint reaches) clamps the input, and a teleport check
restarts the spring rather than differentiating across the seam.

**A known, accepted behaviour:** the neck reaches its 14° clamp for roughly one second when the
bird breaks into motion from a standstill, settling to 2.6–9.1° thereafter. A standing settled
bird measures 0.00°. This is the neck reacting to a real lurch, it is bounded by the clamp, and the
measurement overstates it — the harness steps the speed command instantaneously whereas
`OstrichDriver` ramps it at `acceleration = 4`.

## Deviation from the agreed plan

`backups/` was left at the repository root rather than moved out. It is **tracked in version
control**, so moving it out of the tree would delete a versioned safety copy of the pre-neck bird.
Flagged rather than done unilaterally.

Only `ostrich.blend1` was removed from `Assets/`. The other five `.blend1` autosaves there belong
to the walker and ship assets and are outside this work.
