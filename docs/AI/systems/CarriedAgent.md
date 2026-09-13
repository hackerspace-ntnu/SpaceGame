---
system: CarriedAgent
layer: characters
summary: "A rope or a rocket lifts a NavMesh creature off its mesh; the motor carries it, falls it, lands it"
paths:
  - Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Carry.cs
  - Assets/Game/Scripts/agents/AI/Motors/AgentCarry.cs
  - Assets/Game/Editor/Tests/AgentCarryTests.cs
symptoms:
  - "a jetpack cannot lift the creature I roped, it just skids along the ground"
  - "the pilot is billed for the weight of an animal that never leaves the sand"
  - "the creature I hoisted hangs in the air after the rope is cut"
  - "a leashed animal is thrown into the sky for being walked along flat ground"
  - "a hoisted creature flickers between hanging and standing"
  - "a creature under a hovering pilot resets its path every physics step"
  - "a dropped creature sinks metres through an intact rope"
  - "a creature let go at altitude sinks at walking pace instead of falling"
  - "an NPC or a creature disappears the moment a booster is strapped to it"
  - "a creature streamed out mid-hoist comes back unable to move at all"
  - "an animal in the air leans into the dune underneath it"
reads_with: [AgentSystem, LeashSystem, Jetpack, Locomotion, NavMeshSystem]
updated: 2026-09-13
---

# Carried agents

A rope, or a motor strapped to the animal, can take a NavMesh-driven creature off the mesh and hold
it in the air.

**Scope:** [NavMeshAgentMotor.Carry.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Carry.cs)
(the state), [AgentCarry.cs](Assets/Game/Scripts/agents/AI/Motors/AgentCarry.cs) (the arithmetic),
[AgentCarryTests.cs](Assets/Game/Editor/Tests/AgentCarryTests.cs).
**Related:** [AgentSystem.md](AgentSystem.md) (the motor) · [LeashSystem.md](LeashSystem.md) and
[StrapOnBooster.md](StrapOnBooster.md) (the callers) · [Jetpack.md](Jetpack.md) (what lifts) ·
[Locomotion.md](Locomotion.md) (legged machines, **not** covered).

**Not this system:** a knockdown. `AgentRagdoll.HoldDown` takes the body away from its motor and
lets it go limp — the net's meaning, a captured animal. A carried one stays upright, animated and
angry (`GDC-L1-SYS-0005`: a carry that disabled it would be doing the net's job too).

## Model

- **This is the third thing that can be done to a creature.** `BlastPush` states the old constraint:
  the transform belongs to the motor and forces never land on it, so the options were a ragdoll or a
  leap (a fixed arc). Neither is "hold it up and move it".
- **What was actually broken.** `LeashEnd.Pull` moved a kinematic NavMesh creature with
  `NavMeshAgent.Move`, which re-projects onto the mesh, so the vertical half of every pull was
  discarded in silence: the rope dragged a DuneRat along the sand and never lifted it while
  `LeashLoad.HangingMassOn` billed the pilot its 40 kg.
- **A carry is the mounted leap with the arc removed and no end time.** Same mechanism:
  `updatePosition` and `updateRotation` off, `isStopped`, the path reset, the transform driven by
  hand, `Agent.Warp` onto the sampled mesh on landing. The agent's internal position never leaves
  the takeoff point, so `isOnNavMesh` stays true and `TrySnapToNavMesh` never yanks the body down.
- **Entry is judged on the ask's SLOPE, never on its height.** A rope's far knot sits about a metre
  above the knot on an animal's back, so *every* flat drag pulls slightly upward — a slope of about
  0.45 over two metres. `carryEnterSlope` is 0.7, clear of it and under a jetpack climb.
- **There is no matching test for coming down.** A carry ends by landing and nothing else, so a body
  at the threshold cannot flicker between states — the dead zone is structural.
- **A rope's ask is uncapped here, unlike `LeggedDriver`'s.** That cap stops a rope dragging an
  animal faster than it could walk: right on its feet, wrong in the air, and it breaks
  `JetpackLift`'s arithmetic. The bound that applies is the rope's `TowCap` — the winner's spare pull
  over this body's mass. A **thrust** brings no such bound, so the motor caps it along the mesh
  (`maxThrustDragSpeed`) and nowhere else.
- **The creature is not told.** Its brain keeps pathing while it dangles, as
  [LeashSystem](LeashSystem.md) says of a leashed one, and `Tick` skips the frame.
- **Two asks reach this motor, and they are different questions.** A rope has the distance from its
  own physics, so `RequestTow` takes a point one step away; a booster knows only how hard it pushes,
  so `RequestThrust` takes an **acceleration**. One vector cannot say both — a thruster on the rope's
  channel must invent a distance, and this motor moves the body by whatever it is handed (Gotchas).

## Key types

| Type | File | Role |
|---|---|---|
| `NavMeshAgentMotor` (partial) | [NavMeshAgentMotor.Carry.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Carry.cs) | `ITowable` (`TowAttachPoint`, `RequestTow`, `RequestThrust`), `IsCarried`, `BeginCarry`/`EndCarry`/`AbandonCarry`, the `FixedUpdate` fall and `TryLand` |
| `AgentCarry` | [AgentCarry.cs](Assets/Game/Scripts/agents/AI/Motors/AgentCarry.cs) | Pure: `IsLift` (slope), `Fall` (gravity + terminal speed), `HasLanded` (tolerance that grows with the fall), `ThrustDragSpeed` (a push along the mesh, bounded) |
| `ITowable` | [ITowable.cs](Assets/Game/Scripts/agents/AI/Motors/ITowable.cs) | The rope **and** thrust channel. Also `LeggedDriver`, `OrnithopterFlightMotor` |

Tunables, all on the motor: `carryEnterSlope` 0.7 · `carryLandTolerance` 0.15 m ·
`maxCarryFallSpeed` 30 m/s · `maxThrustDragSpeed` 20 m/s. Gravity is `Physics.gravity` — this
world's is −18, not −9.81.

## Flows

1. **Ask.** Once per physics step, for as long as it lasts: `LeashEnd.Pull` →
   `RequestTow(TowAttachPoint + step)`, or `BoostedBody.Push` → `RequestThrust(acceleration)`.
2. **Enter.** Not carried and `AgentCarry.IsLift(ask, carryEnterSlope)` → `BeginCarry`. Otherwise the
   creature stays on the mesh: `Agent.Move` of the rope's step, or of one step at
   `AgentCarry.ThrustDragSpeed` — a speed the motor accumulates, since the mesh keeps no momentum.
3. **Hold.** A rope's ask is `transform.position += ask`; a thruster's is `+= acceleration · dt²` —
   one step of acceleration, not of velocity, because the fall below measures it and carries it
   forward as momentum. Hand this branch a velocity and it is re-added every step.
4. **Fall.** The motor's `FixedUpdate` reads this body's velocity back out of how far the transform
   moved over the whole last step — its own fall included — adds gravity, clamps to terminal speed
   and integrates. It runs **before** anything that pushes (`[DefaultExecutionOrder(-100)]`), the
   ordering `PlayerMovement` and `LeashedBody` have. Two seconds of 40 m/s² against this world's 18
   leaves a creature 44 m/s off the ground and 98 m up — the boosted player's ride to the metre.
5. **Land.** Nothing asked for a lift last step, the body is descending, and
   `NavMesh.SamplePosition` within `navMeshSnapDistance` (6 m) returns a point it has reached →
   `EndCarry`: `Warp` there, flags back, `isStopped` off. A booster's burn ends the same way.

## Multiplayer

- **Nothing new on the wire.** The carry runs on the machine that already owns the creature — the
  server for a loose one, the rider's machine for a ridden mount — and reaches every other machine
  inside the replicated transform, like the rest of the agent's motion. The caller has applied
  `Network.Owns` first (`Leash.ResolveEnd`, `BoostedBody.FixedUpdate`); `selfDriveSuspended` is the
  second belt, which both asks refuse on exactly as `LeggedDriver` refuses on `ExternallyPosed`.
- A machine that stops being the driver mid-carry abandons it rather than integrating a fall for a
  body whose pose is arriving over the wire.

## Persistence

**Deliberately none.** A carry is something happening to the creature right now, not a property of
it, and it is bounded by a rope `LeashSaveable` already saves (or a booster that is two seconds of
state). A world saved mid-hoist reloads the creature where `TransformSaveable` recorded it and the
motor's off-mesh recovery puts it back on the ground.

## Gotchas

- **Measure the velocity back out of the transform; never accumulate it in a field.** A kinematic
  end reports no velocity to the leash (`LeashEnd.Velocity` reads the Rigidbody, which is not what
  moves here), so the constraint repays the whole error as position. Reading the fall out of the
  position subtracts each step's correction from the next step's velocity; without it the creature
  sinks metres through an intact rope (`Hang_AccumulatingTheVelocityInstead_SinksThroughTheRope`).
  **The mark is taken before the fall** — `lastCarryPos` is recorded at the top of `FixedUpdate`, so
  the reading covers the fall's own move too. Recorded afterwards, the body was left out of its own
  measurement and gravity was applied to zero every step: a released creature came down at 0.36 m/s
  for ever and `maxCarryFallSpeed` was unreachable, which looked like an animal parked in the sky
  with no rope on it (`Drop_MarkingAfterTheFall_NeverAcceleratesAtAll`).
- **A body something is still lifting has not landed, or the state churns at 50 Hz.** A pilot
  hovering at exactly the rope's length asks for a steep pull while the animal still stands on the
  sand: it rises a millimetre, descends, finds the mesh where it left it and lands — then re-enters
  on the next ask, each round trip a `ResetPath` and an `Agent.Warp`. `liftedLastStep` is the gate, a
  plain bool because the motor steps at −100 and the ask at 0: one step's flag is read by the next.
- **`AbandonCarry` in `OnDisable` is load-bearing.** A carry leaves `updatePosition` off and only
  `EndCarry` puts it back, so a creature streamed out mid-hoist comes back unable to move at all,
  with a clean console — the trap the mounted leap's save/restore already closes.
- **`AgentGroundConform` has to be told.** It already refuses to lean a body mid-leap; a carry is the
  same case with no end time, and without the refusal an animal hanging off a jetpack leans into the
  dune forty metres below it.
- **A dropped creature takes no damage, and there is no fall damage to give it.** `PlayerMovement`
  is the only thing that measures a landing, so dropping an animal off a cliff — or rocketing one
  95 m up — is free. If that becomes the dominant way to kill things (`GDC-L1-SYS-0007`), the fix
  belongs in `Combat`, not here.
- **A creature carried over ground with no NavMesh keeps falling.** `TryLand` only lands on the
  mesh, so a body dropped where nothing is baked within `navMeshSnapDistance` falls until it finds
  some — the "no NavMesh under this agent" case the motor already warns about after 3 s, with
  `UnderTerrainGuard` as the floor. A second raycast was rejected: the ground conform's probe is the
  one probe that decides where the ground is.
- **Never hand this motor a destination.** It reads `RequestTow`'s anchor as one step's displacement
  by design — the rope's physics is in that distance — so a far point is a teleport at physics rate.
  The booster asked for a tow 60 m out along its thrust and moved every creature it was clamped to
  at 3 km/s, which read as the animal disappearing on contact. Hence `RequestThrust`.
- **Legged machines are not covered.** `LeggedDriver` implements `ITowable` too, but
  `LeggedLocomotion.Drag` moves `pathPos` and the next settle pulls the height back to ride height,
  so an ostrich cannot be hoisted — by a rope or by a booster. Fixing it means touching Invariant
  I4, the single-author rule in [Locomotion.md](Locomotion.md).

## Extending

1. **Another kind of mover** that wants to be liftable implements `ITowable` and decides for itself
   what a pull or a push costs it. No caller changes; that is the point of the interface.
2. **Another caller** that knows where it wants the body calls `RequestTow` with one step's ask per
   physics step (`Leash`, `SingularityWell` and `GrapplingHookArtifact` each compute their step
   first). One that knows only how hard it pushes calls `RequestThrust` with an acceleration.
3. **Retuning** is `carryEnterSlope` (how steep an ask must be to leave the ground),
   `maxCarryFallSpeed` and `maxThrustDragSpeed`. `carryLandTolerance` is not a feel knob — it is a
   tunnelling guard `AgentCarry.HasLanded` already widens by one step's distance.
4. Pure functions live in `AgentCarry`, testable with no scene, NavMesh or rope. Add to
   [AgentCarryTests.cs](Assets/Game/Editor/Tests/AgentCarryTests.cs), in `Editor/` rather than beside
   the EditMode tests because it touches Assembly-CSharp types.
