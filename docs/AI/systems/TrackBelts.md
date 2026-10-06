---
system: TrackBelts
layer: vehicles
summary: "Strider barges' running gear: track links circulate, wheels turn at each side's measured speed"
paths:
  - Assets/Game/Scripts/Vehicles/Tracks/
  - Assets/Game/Editor/Vehicles/TrackBeltWiring.cs
  - Assets/Game/Editor/Tests/TrackBeltsTests.cs
symptoms:
  - "the dune barge drives but its tracks and wheels stand still"
  - "a barge's track links slide over the sand, or run the wrong way round"
  - "a track link swings round the middle of the barge when it moves"
reads_with: [Vehicles, Striders, DuneBarge, VehicleDust]
updated: 2026-10-06
---

# Track belts

The three Strider dune barges ([Striders.md](Striders.md)) show their tracks working: every link of each track steps round its loop and every wheel on it turns, at the speed **that side** of the hull is seen to move over the ground. The ground run lies still on the sand while the hull rolls over it; a barge turning on the spot runs its two sides in opposite directions. Purely presentation: one runtime component, one editor wiring helper called by `StriderBargeBuilder`.

## Model

- **The art.** Each track is a loop of separate link meshes, `Mesh_TrackAssembly_<Main|Pod>_Link_<Grouser|Chevron|Padded>_<nnn>`, hanging off `Bone_Chassis` (main tracks) or `Bone_Pod_L/R` (front pods), numbered in order round the loop. The wheels (road wheels, top rollers, return roller, idler, sprocket) are meshes on spin bones `Bone_Wheel_<Unit><L|R>_<Kind><n>` from `dune_barge_rig.py` ([DuneBarge.md](DuneBarge.md)). There is no tread texture to scroll: the links are geometry, so the links themselves move.
- **A belt** per track unit × hull side: 4 on the full and lookout barges (main and pod, both sides), 2 on the compact. Measured on the built prefabs: main loops ~50 links, pods 32, pitch ≈ 0.75 m; wheel radii 0.82-1.59 m.
- **Speed** of a belt = the smoothed hull-forward speed of the midpoint of its `TrackContact_<key>_Front/Rear` markers (the dust's contacts, [VehicleDust.md](VehicleDust.md)), read by `GroundSpeedGauge`, the same per-point reading `RollingDust` uses. Smoothed over 0.3 s; a step implying over 50 m/s is a snap and keeps the old speed.
- **Links**: the phase advances by speed·dt / pitch slots; the link baked into slot *i* is placed at slot *i* + phase, lerped/slerped between the two slot frames it lies between. Grousers, pads and chevrons keep their order as they circulate.
- **Wheels**: each bone turns speed / radius about the hull's x axis (`AngleAxis(angle, axle) * rest`). A spin bone that is no disc (reaches under 0.85× as far one way across its axle as the other: the return rollers, which carry their bracket) stays still.
- **LOD**: nothing is written beyond `animateDistance` (120 m) from this machine's `ViewCamera` (not `Camera.main`, which is null while riding, so belts once animated at every range) or below `stillSpeed` (0.02 m/s); the speed is still measured, so a belt is already at speed when the camera comes near. Near cost: ~165 link and ~25 wheel transforms per full barge per frame (GDC-L1-TECH-0002).

## Key types

| Type | File | Role |
|---|---|---|
| `TrackBelts` | [TrackBelts.cs](Assets/Game/Scripts/Vehicles/Tracks/TrackBelts.cs) | `LateUpdate` → `Present(dt, cameraDistance)` (NaN = no camera: animate). `ResetBaseline()` forgets the motion and leaves the gear where it is. Serialized `Belt[]`: contacts, `pitch`, slot poses, links + offsets from their own slot, wheels + rest rotation, axle, radius. Read-outs `BeltCount`, `IsLeft`, `LinksOf`, `WheelsOf`, `SpeedOf` |
| `GroundSpeedGauge` | [GroundSpeedGauge.cs](Assets/Game/Scripts/Vehicles/Tracks/GroundSpeedGauge.cs) | Struct: last position + smoothed speed. `Measure(position, along, …)` signed, `MeasureAlongStep` unsigned, `Reset`. Wraps `MonowheelPresentationMath.StepSpeed` |
| `TrackBeltWiring` | [TrackBeltWiring.cs](Assets/Game/Editor/Vehicles/TrackBeltWiring.cs) | `AddTrackBelts(root, contacts)`; returns null and logs if a track does not read as one loop |

## Flows

- **Build** (`StriderBargeBuilder`, after `AddRollingDust`): links grouped by unit and the side their **mesh centre** is on, ordered by `nnn`, checked to close into one even loop (no gap over 1.6× the median), reversed if the shoelace winding seen from +x says the ground run heads forward (forward motion must advance the phase); slot frame = mesh centre + `LookRotation(tangent, tangent × hull-x)` in the links' parent space; each link's offset = its rest local pose in its own slot's frame; pitch = loop length / links in world metres; wheels by unit and the side the bone sits on, radius = farthest own vertex from the axle.
- **Each frame**: per belt measure → (seen and moving?) phase → links → wheels.

## Multiplayer

Runs on every machine from the replicated hull: the contacts are children of the hull, which its `ClientNetworkTransform` carries. No message, not a `NetworkBehaviour`, nothing spawned or registered. Host, client and late joiner animate alike (the phase can differ between machines, which nobody can see).

## Persistence

N/A: no state worth persisting. Phases and wheel angles are cosmetic and start from the baked pose on every load; a load is a snap the gauge refuses to read as speed.

## Gotchas

- **The links have no pivot of their own.** Every link transform sits at the model origin with its geometry baked where it lies on the loop: `localPosition` says nothing about where a link is, and rotating one in place swings it round the barge's middle. Place links by the rigid step between slot frames measured from the mesh centres.
- **The rig's L/R is Blender's side.** `Bone_Wheel_MainL_*` sits on the hull's +x, which `StriderBargeBuilder` calls R. Group by where a part sits, never by the letter.
- **Not every `Bone_Wheel_*` is a wheel.** The return roller's bone carries its bracket, so spinning it swung the bracket round over the track like a lever (seen in the first render). `TrackBeltWiring.IsRound` leaves it still.
- **A pod's ground run is short and rounded**, so a link on it lifts as it travels; a slide test measures only along the hull.
- **Tunables are serialized on the prefabs**: changing a default changes nothing until the builder runs again.

## Extending

- **Another tracked hull with link meshes:** give it ground contacts named `TrackContact_<Unit>_<L|R>_Front/Rear` and links named as above, call `TrackBeltWiring.AddTrackBelts` from its builder, and add it to `TrackBeltsTests.Barges`.
- **A tread that is one mesh:** this component does not apply; scroll its UV through a `MaterialPropertyBlock` instead, driven by the same `GroundSpeedGauge`.
