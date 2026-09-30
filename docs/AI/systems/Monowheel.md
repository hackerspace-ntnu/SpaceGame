---
system: Monowheel
layer: vehicles
summary: "Monowheel presentation: rings spin at ground speed, paddles throw sand into a 10–14 s dust cloud, hubs smoke"
paths:
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs
  - Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs
  - Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs
  - Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs
  - Assets/Game/Editor/Tests/MonowheelPrefabTests.cs
  - Assets/Game/Art/Models/Vehicles/Monowheel/
  - "Assets/Game/Art/Models/_Source~/models/vehicles/desert_monowheel_export.py"
symptoms:
  - "the monowheel's wheels wobble or orbit instead of spinning"
  - "the wheels spin backwards, or sideways to the way it is moving"
  - "no sand comes off the wheels however fast it goes"
  - "a burst of dust appears when a monowheel is loaded or teleported"
  - "dust keeps pouring out while the monowheel is in the air"
reads_with: [Vehicles, Jetpack, AgentSystem]
updated: 2026-09-25
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
- **Wheel spin is owned here.** The Strider motor must not spin rings. Chassis lean belongs to `MonowheelLean` (applied to the Body child), next to `MonowheelMotor`: see [Vehicles.md](Vehicles.md).

## Key types

| Type | File | Role |
|---|---|---|
| `MonowheelPresentationMath` | [MonowheelPresentationMath.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs) | Pure: speed step (snap detection by implied speed), spin degrees, speed fraction, rate lerp, LOD factor, ring plane normal (power iteration). |
| `MonowheelWheel` | [MonowheelWheel.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs) | Serialized per-wheel measurement: ring bone, signed local axle, paddle radius, root-local contact and hub, the wheel's three systems. |
| `MonowheelPresentation` | [MonowheelPresentation.cs](Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs) | The per-frame component. `Present(dt)` (LOD'd against `Camera.main`), `Present(dt, cameraDistance)` (NaN = no camera), `ResetBaseline()`. |
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

## Layers and budget

| Layer | Born at | Lifetime | Rate per wheel | Cap per wheel |
|---|---|---|---|---|
| Sand spray | Contact, cone up and back, gravity 1 | 0.5–0.8 s | 0 → 150/s | 60 |
| Dust cloud | 1.4 m over the contact, 2.8–4.5 m puffs at 0.3 alpha, drifting, growing 3× | 10–14 s | 0 → 8/s | 120 (8/s × 14 s = 112) |
| Hub smoke | Hub, rising, growing 3× | 2.5–3.5 s | 2 → 12/s | 40 |

- **Worst case:** at most 220 particles per wheel, about 2 600 for six doubles. The dust puffs are big (up to ~13 m across when old), so the cost to watch is overdraw, not count: in a cloud's middle the screen is covered many times over.
- **Distance LOD:** full effect to 60 m (`lodNear`), linear down to 0 at 150 m (`lodFar`). Spin never LODs.
- **Materials:** `MonowheelDust.mat` and `MonowheelHubSmoke.mat` in `Assets/Game/Art/Materials/Vehicles/`, both on the textureless `JetSmoke` shader.
- **Measured cost:** *not yet profiled* (plan Task 6: six moving doubles).

## Multiplayer

Every machine runs this locally, from the transform it already sees (a replicated transform on clients). There are no messages and no networked state. Nothing is spawned at runtime, so there's no network-prefab registration. (The Strider gameplay prefab that wraps this is registered by its own builder.)

## Persistence

N/A: **no state worth persisting.** Speed, spin angle and particles are re-derived every frame, and a loaded monowheel resumes presenting on its first frame of motion. A load is a snap, and snap detection keeps it from reading as speed.

## Gotchas

- **The axle is measured because the import and the camber make any fixed axis wrong.** The doubles' rings are cambered 7° and 20°. Turning about the vehicle's X axis would make them wobble.
- **A spinning bone must pivot on its hub.** If a ring or its bone is moved in Blender without the other, the ring orbits instead of spinning. The self-check's 2 cm hub test catches it. See `desert_monowheel_BUILD.md`.
- **A snap is judged by implied speed (`maxPlausibleSpeed`, 50 m/s), not distance.** A save restore, a streaming migrate or a NetworkTransform correction keeps the previous speed. A 250 ms frame hitch at top speed (5 m) still reads as driving, which a fixed 5 m threshold got wrong (caught by the Strider session).
- **A script-created particle system with no material draws nothing, silently** ([Jetpack.md](Jetpack.md)). The builder creates both materials, throws if the shader is missing, and the self-check asserts particles exist.
- **The ground probe uses `gameObject.scene.GetPhysicsScene()`,** so the builder's preview-scene check and a live world take the same code path. Plain `Physics.Raycast` would see nothing in the preview scene.
- **The self-check passes `float.NaN` as camera distance.** In the editor, `Camera.main` may be a scene camera far away, and the LOD would silence the very effects being checked.
- **The FBX's own root is the armature node, not a clean frame.** It's rotated (286°, 180°, 180°) by the rig tilt and the axis conversion, and scaled ×100, so its +Z is not forward. The art prefab therefore has a clean identity root with the model nested under it at identity, and speed is read along that clean root's +Z. The builder refuses a model whose front cowl isn't ahead of the hubs in that frame. The Strider "Body" child must not be yawed.

## Extending

- **A new variant:** export its FBX (with `Bone_Ring*` and a `Mesh_FrontCowl*`), then add one line to `MonowheelPresentationBuilder.Variants`. The prefab tests pick it up.
- **A new layer:**
  - a recipe method in the builder
  - a field on `MonowheelWheel`
  - a rate line in `Present`
  - an assert in `MonowheelPrefabTests`
  - a row in the table above
- **Tuning:** runtime rates, LOD distances and speeds are serialized on `MonowheelPresentation`. Lifetimes, sizes and caps are the builder's recipe constants, so rebuild after changing them.
