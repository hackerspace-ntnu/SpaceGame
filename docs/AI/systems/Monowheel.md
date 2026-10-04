---
system: Monowheel
layer: vehicles
summary: "Monowheel art: rings spin, paddles throw a lingering dust cloud, the ski rides the sand, the helm swings"
paths:
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPoseMath.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelGround.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelChassis.cs
  - Assets/Game/Editor/Tests/MonowheelGroundContactTests.cs
  - Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs
  - Assets/Game/Editor/Support/DustCloudRecipe.cs
  - Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs
  - Assets/Game/Editor/Tests/MonowheelPrefabTests.cs
  - Assets/Game/Editor/Tests/MonowheelPoseMathTests.cs
  - Assets/Game/Editor/Tests/MonowheelLeanTests.cs
  - Assets/Game/Art/Models/Vehicles/Monowheel/
  - "Assets/Game/Art/Models/_Source~/models/vehicles/desert_monowheel_export.py"
symptoms:
  - "the monowheel's wheels wobble or orbit instead of spinning"
  - "the wheels spin backwards, or sideways to the way it is moving"
  - "no sand comes off the wheels however fast it goes"
  - "a burst of dust appears when a monowheel is loaded or teleported"
  - "dust keeps pouring out while the monowheel is in the air"
  - "the monowheel's ski hangs in the air on flat ground, or digs into a slope"
  - "the monowheel stands on its ski with the wheel in the air"
  - "the helm on the back of a monowheel never moves"
  - "the steering handles swing instead of the helm on the back"
  - "the monowheel dust is a row of separate blobs floating over the sand"
  - "a big dust puff cuts a hard straight line where it meets the ground"
reads_with: [Vehicles, Jetpack, AgentSystem]
updated: 2026-10-04
---

# Monowheel presentation

The five desert monowheels (Runner, Hauler, Patched, Double, DoubleWide) spin their rings, throw sand, leave a lingering dust cloud and smoke from their hubs, all from **how their root actually moves**. This component presents the vehicle; it does not move it. `MonowheelMotor` moves it and `MonowheelLean` leans it: see [Vehicles.md](Vehicles.md).

**Scope:**
- scripts in [Vehicles/Monowheel/](Assets/Game/Scripts/Vehicles/Monowheel/)
- the builder [MonowheelPresentationBuilder.cs](Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs)
- art prefabs `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Variant>.prefab`
- models `Assets/Game/Art/Models/Vehicles/Monowheel/*.fbx`, exported by `desert_monowheel_export.py` from the `.blend` library

**Related:** [Vehicles.md](Vehicles.md) · [Jetpack.md](Jetpack.md) (the smoke shader and the silent-material trap) · [AgentSystem.md](AgentSystem.md)

## Model

- **Art prefab, not a gameplay prefab.** `Monowheel_<Variant>` is a clean root holding the FBX (nested at identity), `MonowheelPresentation` and three particle systems per wheel, and nothing else. The Strider builder nests it as a child named "Body" and adds the motor, riders, seats and netcode. It never edits these prefabs.
- **Motion in, presentation out.** Each frame:
  - speed = the root's displacement along its **+Z**, over Δt, smoothed
  - each ring turns by speed ÷ paddle radius about its own axle
  - spray and dust come off only while a ground probe under that wheel hits something
  - hub smoke idles, and rises with speed
- **Measured, never assumed.** The builder reads each wheel off its geometry:
  - **axle:** the ring mesh's plane normal
  - **radius:** the paddles' reach
  - **contact point:** the lowest point of the (possibly cambered) wheel
  - **spin sign:** chosen so the bottom of the wheel moves backwards when the vehicle moves forwards
- **World-space particles** make the lingering cloud. A puff stays where it was born as the vehicle drives on.
- **Wheel spin is owned here.** The Strider motor must not spin rings. The chassis pose (roll, ski pitch, helm) belongs to `MonowheelLean` on the gameplay root, applied to the Body child: see [Chassis pose](#chassis-pose).
- **The builder also measures the parts the pose needs:** the ski's lowest point (`SkiLowPoint`, none on the DoubleWide) and the **helm**, the `Mesh_TailPanel_*` panel hinged on the `Mesh_TailPost_*` at the very back, with the post's centre and axis as its hinge. The `Mesh_Tiller_*` T-handles in front of the rider are the steering grips; they do not move.

## Key types

| Type | File | Role |
|---|---|---|
| `MonowheelPresentationMath` | [MonowheelPresentationMath.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs) | Pure: speed step (snap detection by implied speed), spin degrees, speed fraction, rate lerp, LOD factor, ring plane normal (power iteration). |
| `MonowheelWheel` | [MonowheelWheel.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs) | Serialized per-wheel measurement: ring bone, signed local axle, paddle radius, root-local contact and hub, the wheel's three systems. |
| `MonowheelPresentation` | [MonowheelPresentation.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs) | The per-frame component. `Present(dt)` (LOD'd against `Camera.main`), `Present(dt, cameraDistance)` (NaN = no camera), `ResetBaseline()`. |
| `MonowheelPoseMath` | [MonowheelPoseMath.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPoseMath.cs) | Pure: the ground line under the hub from two samples (`GroundUnderHub`) and the nose-down pitch that puts the ski on it (`SkiPitch`). |
| `MonowheelGround` | [MonowheelGround.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelGround.cs) | The one ground probe the presentation and `MonowheelLean` share: own physics scene, own colliders skipped by hierarchy, nearest hit. |
| `MonowheelChassis` | [MonowheelChassis.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelChassis.cs) | On the kinematic `Chassis` body under Body (Strider builder): suspends its contacts with the vehicle's own colliders (`RiderCollisionIgnore`) in `Awake`; `IgnoreOwnWheel()` is public for EditMode tests. |
| `MonowheelPresentationBuilder` | [MonowheelPresentationBuilder.cs](Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs) | `Tools ▸ Vehicles ▸ Build Monowheel Presentation`: materials, measurement, systems, prefabs, then a self-check of each in a preview scene. |

## Flows

1. **Build.** For each variant, the builder:
   - loads the FBX and finds `Bone_Ring` / `Bone_RingL` / `Bone_RingR`
   - measures each wheel from the `Mesh_Ring*` under it and that ring's paddles
   - checks the front cowl is ahead of the hubs along the root's +Z
   - adds `FX_Spray*`, `FX_Dust*` and `FX_HubSmoke*`, then `Configure`s and saves the prefab
2. **Self-check (every build).** The builder puts the prefab over a ground box in a preview scene:
   - **Standing still for 1 s:** no spray, some hub smoke.
   - **Moving at 18 m/s for 1 s:** every ring turned, stayed centred on its hub (2 cm), and threw spray and dust.
   - Any failure throws, and the build stops.
3. **Per frame.** `Update` calls `Present(Time.deltaTime)`:
   - step the speed
   - rotate each ring bone about its local axle (`Space.Self`)
   - raycast down at each contact point through the object's own `PhysicsScene`, skipping the vehicle's own colliders
   - set `rateOverTime` on all three systems from speed fraction × grounded × LOD

## Chassis pose

`MonowheelLean.Pose(dt)` runs in `LateUpdate` on every machine and writes only the Body's local pose, so the physics root and its wheel colliders stay upright, while everything under Body tips with it: the seats, with their riders held on them by `TiltingSeats` ([Vehicles.md](Vehicles.md)), and the chassis.

- **The wheel carries the vehicle, nothing else.** Pitching about the hub keeps the hub where it is, so the pose is only right while the hub stands one wheel radius off the ground. Two things used to hold it higher: the hoop's boxes poked past the paddles (see [Striders.md](Striders.md) Gotchas), and the chassis boxes sat level on the upright root while the art tipped, so on ground rising about 13° or more their noses met the slope first. Either way the pose then put the ski on the sand and left the wheel hanging in the air. The chassis boxes are now on a kinematic `Chassis` body under Body (`MonowheelChassis`): they tip with the frame, so shots and bodies meet the frame where it is drawn, and a kinematic body makes no contacts with static ground, so it never props the vehicle up. It still shoves moving bodies and blocks raycasts. `MonowheelGroundContactTests` pins it.

- **Roll:** into turns, from the root's measured yaw rate and speed (`MonowheelDrive.Lean`).
- **Ski pitch:** the chassis tips about the **hub**. The wheel is round, so it still touches the ground at any pitch, and only the ski has to come down. Ground is probed under the wheel contact and under the ski's low point from the **unpitched** pose (so the answer does not chase its own tilt), and `MonowheelPoseMath` finds the pitch that puts the ski on that line. It is clamped to `maxPitch` (30°), eased by `pitchFollow`, and relaxes to level with no ski or no ground under either probe. On flat ground a Runner tips about 5.4° nose-down.
- **Helm:** the tail panel swings about its post into the turn (`helmSwingPerLean` per degree of roll, up to `maxHelmSwing`, eased by `helmFollow`), so its weight goes to the inside of the turn. It turns about the measured hinge, so it stays on the post.

## Layers and budget

| Layer | Born at | Lifetime | Rate per wheel | Cap per wheel |
|---|---|---|---|---|
| Sand spray | Contact, cone up and back, gravity 1 | 0.5–0.8 s | 0 → 150/s | 60 |
| Dust cloud | Thrown up and back off the contact at 4–7.5 m/s, drag 2.5 stops it within about a second; 2–3.2 m puffs billow to 4.2×, fade in, churn (noise), fade out | 8–12 s | 0 → 20/s | 240 (20/s × 12 s) |
| Hub smoke | Hub, rising, growing 3× | 2.5–3.5 s | 2 → 12/s | 40 |

- **Worst case:** at most 340 particles per wheel, about 4 100 for six doubles. The dust puffs are big (up to ~13 m across when old), so the cost to watch is overdraw, not count: in a cloud's middle the screen is covered many times over.
- **Distance LOD:** full effect to 60 m (`lodNear`), linear down to 0 at 150 m (`lodFar`). Spin never LODs.
- **One recipe for every lingering cloud:** the dust layer is [`DustCloudRecipe.Cloud`](Assets/Game/Editor/Support/DustCloudRecipe.cs), shared with the Strider city's footfall and track dust ([VehicleDust.md](VehicleDust.md)) and, in black, engine smoke. Change the look there, not per vehicle. The other machines share `SandDust.mat` (`VehicleDustWiring.SandMaterial`), with this dust's tint and `_SoftFade`; `MonowheelDust.mat` is its older twin and can switch to it.
- **Materials:** `MonowheelDust.mat`, `MonowheelSpray.mat` and `MonowheelHubSmoke.mat` in `Assets/Game/Art/Materials/Vehicles/`, all on the textureless `JetSmoke` shader. Only the dust sets `_SoftFade` (1.2 m), the shader's soft-particle fade against the camera depth texture.
- **Measured cost:** *not yet profiled* (plan Task 6: six moving doubles).

## Multiplayer

Every machine runs this locally, from the transform it already sees (a replicated transform on clients). There are no messages and no networked state. Nothing is spawned at runtime, so there's no network-prefab registration. (The Strider gameplay prefab that wraps this is registered by its own builder.)

## Persistence

N/A: **no state worth persisting.** Speed, spin angle and particles are re-derived every frame, and a loaded monowheel resumes presenting on its first frame of motion. A load is a snap, and snap detection keeps it from reading as speed.

## Gotchas

- **Anything that collides on the upright root and reaches past the wheel props the vehicle up**, and the ski pose then hides it by bringing the ski down to the sand: the symptom is the wheel in the air, never a wrong ski. Keep every root collider inside the wheel's circle, and put anything that must follow the frame under Body on its own kinematic body, never on Body alone (its colliders would join the root's compound collider, prop the root up as the pose tips them, and the pose, probing from the higher hub, would tip them further).

- **The doubles' tail post is a unit cube stretched by its transform.** Its mesh is the same size on every axis, so the hinge axis is judged from the box's edges after the transform, in root space. Judged from the mesh alone, it came out horizontal.

- **The ski cannot lie flat on the sand while the wheel touches it.** Extended backwards, the runner's underside passes inside the wheel, so only its lowest point (just behind the curl) can rest on the ground. That point is what `SkiLowPoint` measures and what the pitch brings down. Reshaping the ski is a model change, not a tuning one.
- **A puff born on the sand cut a hard line into it** until `JetSmoke` gained `_SoftFade`, which is why the old dust floated 1.4 m up as separate blobs. The fade needs URP's depth texture (`m_RequireDepthTexture`, on in both pipeline assets). The spray has its own material without it: grains a few centimetres across would fade out entirely.
- **The emitter is moving but the puffs must not inherit its velocity.** Inherit Velocity stays off, so the cloud stays behind the wheel. Drag uses `multiplyDragByParticleSize = false`, or the growing puffs would brake harder as they billow.

- **The axle is measured because the import and the camber make any fixed axis wrong.** The doubles' rings are cambered 7° and 20°. Turning about the vehicle's X axis would make them wobble.
- **A spinning bone must pivot on its hub.** If a ring or its bone is moved in Blender without the other, the ring orbits instead of spinning. The self-check's 2 cm hub test catches it. See `desert_monowheel_BUILD.md`.
- **A snap is judged by implied speed (`maxPlausibleSpeed`, 50 m/s), not distance.** A save restore, a streaming migrate or a NetworkTransform correction keeps the previous speed. A 250 ms frame hitch at top speed (5 m) still reads as driving, which a fixed 5 m threshold got wrong (caught by the Strider session).
- **A script-created particle system with no material draws nothing, silently** ([Jetpack.md](Jetpack.md)). The builder creates both materials, throws if the shader is missing, and the self-check asserts particles exist.
- **The ground probe uses `gameObject.scene.GetPhysicsScene()`,** so the builder's preview-scene check and a live world take the same code path. Plain `Physics.Raycast` would see nothing in the preview scene.
- **The self-check passes `float.NaN` as camera distance.** In the editor, `Camera.main` may be a scene camera far away, and the LOD would silence the very effects being checked.
- **The FBX's own root is the armature node, not a clean frame.** It's rotated (286°, 180°, 180°) by the rig tilt and the axis conversion, and scaled ×100, so its +Z is not forward. The art prefab therefore has a clean identity root with the model nested under it at identity, and speed is read along that clean root's +Z. The builder refuses a model whose front cowl isn't ahead of the hubs in that frame. The Strider "Body" child must not be yawed.

## Extending

- **A new variant:** export its FBX (with `Bone_Ring*` and a `Mesh_FrontCowl*`), then add one line to `MonowheelPresentationBuilder.Variants`. The prefab tests pick it up.
- **A new variant with a ski or helm:** name the parts `Mesh_SkiRunners*`, `Mesh_TailPanel_*` and `Mesh_TailPost_*`; the builder throws on a missing helm or post, and `MonowheelLeanTests` checks the pose.
- **A new layer:**
  - a recipe method in the builder
  - a field on `MonowheelWheel`
  - a rate line in `Present`
  - an assert in `MonowheelPrefabTests`
  - a row in the table above
- **Tuning:** runtime rates, LOD distances and speeds are serialized on `MonowheelPresentation`. Lifetimes, sizes and caps are the builder's recipe constants, so rebuild after changing them.
