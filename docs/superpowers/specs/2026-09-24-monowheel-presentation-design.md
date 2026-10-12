# Monowheel presentation — spinning wheels, sand spray, dust clouds, hub smoke

**Date:** 2026-09-24 · **Branch:** `Feat/create-factions` · **Status:** design approved in chat, spec for review
**Related:** [Strider monowheels](2026-09-24-strider-monowheels-design.md). This spec supplies that spec's
§2.1 **"Presentation (every machine)"** bullet — ring spin — plus the sand and smoke effects. It adds
no motor, no riders and no netcode.

## 1. What we are building

The five monowheel models (`Assets/Game/Art/Models/Vehicles/Monowheel/*.fbx`: Runner, Hauler,
Patched, Double, DoubleWide) come alive when they move:

- the **wheels spin** at ground speed,
- each wheel **throws sand**, lots of it, off its paddles where they meet the ground,
- the sand leaves a **dust cloud** behind the vehicle that lingers about **5 s**,
- **smoke rises from each wheel hub**, as if the drive lives in the hub.

### Decisions (user, 2026-09-24)

| Question | Decision |
| --- | --- |
| What moves it | **Effect only for now.** Driven by however the vehicle's root actually moves. Driving arrives with the Strider `MonowheelMotor`. |
| Where the smoke comes from | **The wheel hubs** — the roller cradle inside each ring. |
| How many on screen | **6 or more** (Strider convoys and scouts), so hard per-vehicle caps plus distance fall-off. |
| Approach | **A** — per-vehicle world-space Shuriken systems with caps and distance LOD. Not a shared dust field (premature), not VFX Graph (same overdraw, off-pattern for this repo). |
| Dust lifetime | **5 s** (first proposed 10 s). |
| Wheel spin | **Yes**, in the same component, from the same speed. |

## 2. `MonowheelPresentation`

One `MonoBehaviour` on the monowheel root (`Assets/Game/Scripts/Vehicles/Monowheel/`). It runs on every
machine and reads only the root transform, so it works identically for a host, a client watching a
replicated transform, a player-driven motor or an NPC-driven one.

### 2.1 Per frame

1. **Ground speed.** `(position − lastPosition) / Δt`, projected onto the root's forward axis (+Z in
   Unity, the models' −Y in Blender), smoothed over `speedSmoothing` seconds so a jittery replicated
   transform does not flicker the effects. A snap (a step implying more than `maxPlausibleSpeed`, 50 m/s: a save restore, a streaming
   migrate, a NetworkTransform correction) keeps the previous speed instead of reading as a burst.
   It is judged by implied speed, not distance, so a long frame hitch at top speed still reads as driving.
2. **Per wheel:**
   - **Spin.** Turn the wheel's `Bone_Ring*` transform about its own axle by
     `speed ÷ paddleRadius` radians/s. The axle is measured, not assumed (§2.2). The doubles are
     cambered, so turning about the vehicle's X axis would wobble the wheel.
   - **Ground contact.** One `Physics.Raycast` down from the wheel's contact point, `groundProbe`
     long, against a serialized `groundLayers` mask. It defaults to the project's terrain and
     default layers (see [ProjectConfig](../../AI/systems/ProjectConfig.md)); the vehicle's own
     colliders are excluded. No contact means no spray and no fresh dust (a jump stays clean).
   - **Emission.** Spray, dust and smoke rates are set from `speedFraction = |speed| / fullSpeed`
     through serialized curves (§3), times the distance factor (§4).

### 2.2 Wheels, measured at build time

The builder records per wheel, as serialized data: the ring bone, the axle in the bone's local
space, the spin sign, the paddle radius, the contact point and the hub point. The axle is the
ring mesh's plane normal (the direction of least variance of its vertices), expressed in the bone's
local frame. That is robust to whatever axis convention the FBX import applied to bones. The sign
is chosen so the bottom of the wheel moves backwards when the vehicle moves forwards. Runtime
code never guesses an axis.

## 3. The three layers

All three are **world-space** (`simulationSpace = World`). The puff stays where it was born while the
vehicle drives out from under it, and that is the lingering cloud. The shared shader is the jetpack's
`SpaceGame/Effects/JetSmoke` (soft textureless puff, alpha-blended, tint through the vertex colour).
Two new materials use it: `MonowheelDust.mat` (sand tint) and `MonowheelHubSmoke.mat` (grey-brown).
There is no new shader.

| Layer | Emitter | Look | Rate (per wheel) | Lifetime | Cap (per wheel) |
| --- | --- | --- | --- | --- | --- |
| **Sand spray** | Contact point, cone aimed up and back | Small sand grains thrown in arcs, gravity 1, fading fast | 0 → ~150/s with speed | 0.5–0.8 s | 60 |
| **Dust cloud** | Contact point, wide box, slight upward drift | Big soft puffs growing to 3–4 m, drifting and spreading, alpha falling to 0 | 0 → ~18/s with speed | **4.5–5 s** | 90 |
| **Hub smoke** | Hub point, small sphere, rising | Grey-brown puffs curling upward, growing | ~2/s idle → ~12/s at speed | 2.5–3.5 s | 40 |

All rates, sizes, colours, lifetimes and caps are serialized on the component or on the systems the
builder creates. The numbers above are starting values to be tuned by eye.

## 4. Budget (GDC-L1-TECH-0002, GDC-L1-PERF-0004)

- **Per wheel cap:** 190 particles, so 190 per single and 380 per double. **Six doubles** in view is at
  most about 2 300 particles. The dust puffs are the overdraw cost, and they are both the fewest and
  the largest.
- **Distance LOD** against the active camera: full effect to `lodNear` (60 m), rates falling linearly
  to 0 at `lodFar` (150 m); smoke and dust stop entirely beyond. Wheel spin always runs, since it is
  one rotation per wheel.
- **Culling:** the systems keep simulating off-screen (`AlwaysSimulate`), so a cloud you turn back
  toward is still there. The emission stops when nothing is emitting, which caps the CPU cost.
- **Measured before done:** the profiler, beside six moving doubles at full dust, with particle and
  frame-time numbers recorded in the system doc.

## 5. Prefabs and the builder

- **`MonowheelPresentationBuilder`** (`Assets/Game/Editor/Vehicles/`), menu item
  `Tools/Vehicles/Build Monowheel Presentation`. For each FBX it builds an **art prefab**
  `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Variant>.prefab`: the model, `MonowheelPresentation`
  with its measured wheels, and the three systems per wheel, with the materials created or reused.
- **The Strider builder wraps these.** When `StriderMonowheelBuilder` (Strider spec §2.3) is written,
  it starts from `Monowheel_<Variant>` and adds the motor, riders, seats and netcode. It does not
  rebuild the effects or add a second spinner. The Strider spec's §2.1 presentation bullet is
  satisfied by this component.
- **Self-check:** after building, the builder moves each prefab instance forward for a few simulated
  frames on a test ground plane, and fails with an error if any wheel spun zero degrees or any layer
  emitted zero particles. That catches the jetpack's "null renderer material draws nothing, silently"
  class of bug.

## 6. Multiplayer

Purely presentational and local on every machine: each peer derives speed from the transform it
already sees. There are no messages and no networked state. Nothing is spawned at runtime, so there is
no network prefab registration. (The Strider gameplay prefab that wraps this will be registered by its
own builder.)

## 7. Persistence

**No state worth persisting.** Speed, spin angle and particles are all re-derived every frame, and a
loaded monowheel resumes presenting from its first frame of motion. Nothing is added to the save.

## 8. Testing

- **Pure logic, EditMode:** a `MonowheelPresentationMath` static class with the speed estimate (with
  teleport reset), the spin step, the emission rate from speed, and the LOD factor from distance. Each
  gets unit tests.
- **Build read-back, EditMode:**
  - Each of the five art prefabs has `MonowheelPresentation`.
  - It has one or two wheels (matching the variant) with a non-zero axle and radius.
  - It has three systems per wheel, each with a material and the caps in §3.
  - Everything is world space.
- **Builder self-check** (§5) on every build.
- **Play check:** drag a monowheel prefab across sand at speed. Watch the wheels turn the right way,
  the spray and cloud appear and die at 5 s, the hub smoke, and the fall-off past 60 m. Verify on the
  host and on a client watching a moved transform.

## 9. Documentation

- A new system doc, `docs/AI/systems/Monowheel.md`: model, key types, flows, the budget numbers,
  multiplayer and persistence (as in §6 and §7), gotchas, and how to add a layer. It gets `paths:`
  for the new script folder, editor builder and prefab folder, so ROUTING picks it up.
- A plain-language entry in `docs/Human/the-systems.md`.
- Regenerate with `python3 tools/docs_check.py --index`.

## 10. Design principles consulted

- **GDC-L1-FEEL-0004** (objective) — the sand is feedback on a real event (the paddles biting the
  ground), so it is driven by actual speed and ground contact, not always on.
- **GDC-L1-TECH-0002** and **GDC-L1-PERF-0004** (contextual) — dense, lingering transparent dust is
  the classic overdraw cost. With 6+ vehicles on screen it is designed to per-wheel caps plus
  distance LOD, and measured in the profiler.
- The 10 s → 5 s change (user) halves the live dust per wheel, so the budget has headroom.

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Bone axes after FBX import differ from Blender's | The axle is measured from ring geometry at build time (§2.2), never assumed. |
| Wheels spin backwards | The sign comes from the build-time rule (bottom moves backward when going forward), and the self-check and play check confirm it. |
| Jittery replicated transform flickers the effects | Speed is smoothed, and teleports reset the baseline. |
| Particle materials render nothing, silently | The builder creates and assigns the materials, and the self-check fails if a layer emits nothing. |
| Overdraw with many vehicles | Per-wheel caps, distance LOD, measured in the profiler. |
| Unity MCP is down during implementation | The builder is a menu item the user can run. Verification needs either the MCP back or the user running it. |
