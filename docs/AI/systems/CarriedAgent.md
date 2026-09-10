---
system: CarriedAgent
layer: characters
summary: "A rope lifts a NavMesh creature off its own mesh; the motor carries it, falls it, and lands it back on"
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
  - "a creature streamed out mid-hoist comes back unable to move at all"
  - "an animal in the air leans into the dune underneath it"
reads_with: [AgentSystem, LeashSystem, Jetpack, Locomotion, NavMeshSystem]
updated: 2026-09-09
---

# Carried agents

A rope can now take a NavMesh-driven creature off the mesh and hold it in the air.

**Scope:** [NavMeshAgentMotor.Carry.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Carry.cs)
(the state), [AgentCarry.cs](Assets/Game/Scripts/agents/AI/Motors/AgentCarry.cs) (the arithmetic),
[AgentCarryTests.cs](Assets/Game/Editor/Tests/AgentCarryTests.cs).
**Related:** [AgentSystem.md](AgentSystem.md) (the motor this is part of) ·
[LeashSystem.md](LeashSystem.md) (the only caller so far) · [Jetpack.md](Jetpack.md) (what does the
lifting) · [Locomotion.md](Locomotion.md) (legged machines, which this does **not** cover).

**Not this system:** a knockdown. `AgentRagdoll.HoldDown` takes a creature's body away from its motor
and lets it go limp, and that is the net's and the hogtie's meaning — a captured animal. A carried one
is upright, animated and alive the whole time, and lands on its feet still angry
(`GDC-L1-SYS-0005`: a carry that also disabled the creature would be doing the net's job as well as
its own).

## Model

- **This is the third thing that can be done to a creature.** `BlastPush` states the old constraint
  outright: a creature's transform belongs to its motor and forces never land on it, so the only
  options were to take the body away from the motor and let it fall (a ragdoll) or to throw it as a
  fixed arc (a leap). Neither is "hold it up and move it", which is what a rope wanted.
- **What was actually broken.** `LeashEnd.Pull` moved a kinematic NavMesh creature with
  `NavMeshAgent.Move`, which re-projects onto the mesh — so the vertical half of every pull was
  discarded, in silence. The rope could drag a DuneRat along the sand and never lift it, while
  `LeashLoad.HangingMassOn` counted its 40 kg all the same, so the pilot paid full thrust and heat
  for cargo that could not move. The jetpack's arithmetic was right the whole time.
- **A carry is the mounted leap with the arc removed and no end time.** Same mechanism, deliberately:
  `updatePosition` and `updateRotation` off, `isStopped`, the path reset, the transform driven by
  hand, `Agent.Warp` onto the sampled mesh on landing. The agent's own internal position never
  leaves the takeoff point, which is why `isOnNavMesh` stays true and `TrySnapToNavMesh` does not
  fire and yank the body back down.
- **Entry is judged on the pull's SLOPE, and it cannot be judged on its height.** A rope's far knot
  sits in a player's hand about a metre above the knot on an animal's back, so *every* flat drag in
  the game pulls slightly upward — over a two-metre rope that standing offset is a slope of about
  0.45. `carryEnterSlope` is 0.7, roughly 45°, which is well clear of it and well under a jetpack
  climb.
- **There is no matching test for coming down.** A carry ends by landing and by nothing else, so a
  body at exactly the threshold cannot flicker between states — the dead zone is structural.
- **The rope's ask is uncapped here, unlike `LeggedDriver`'s.** That cap exists so a rope cannot drag
  an animal faster than it could walk, which is right for something on its feet and wrong for
  something hanging in the air — a rat's walking speed has nothing to say about how fast a jetpack
  lifts it, and capping there breaks `JetpackLift`'s own arithmetic. The bound that does apply is
  the rope's `TowCap`, which is real physics: the winner's spare pull over this body's mass.
- **The creature is not told.** Its brain keeps pathing while it dangles, exactly as
  [LeashSystem](LeashSystem.md) already says of a leashed one, and `Tick` simply skips the frame.

## Key types

| Type | File | Role |
|---|---|---|
| `NavMeshAgentMotor` (partial) | [NavMeshAgentMotor.Carry.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Carry.cs) | `ITowable` (`TowAttachPoint`, `RequestTow`), `IsCarried`, `BeginCarry`/`EndCarry`/`AbandonCarry`, the `FixedUpdate` fall and `TryLand` |
| `AgentCarry` | [AgentCarry.cs](Assets/Game/Scripts/agents/AI/Motors/AgentCarry.cs) | Pure: `IsLift` (slope), `Fall` (gravity + terminal speed), `HasLanded` (tolerance that grows with the fall) |
| `ITowable` | [ITowable.cs](Assets/Game/Scripts/agents/AI/Motors/ITowable.cs) | The rope channel itself. Also `LeggedDriver`, `OrnithopterFlightMotor` |

Tunables, all on the motor: `carryEnterSlope` 0.7 · `carryLandTolerance` 0.15 m ·
`maxCarryFallSpeed` 30 m/s. Gravity is `Physics.gravity` — this world's is −18, not −9.81.

## Flows

1. **Ask.** `Leash.ResolveEnd` → `LeashEnd.Pull` → `RequestTow(TowAttachPoint + step)`, once per
   physics step for as long as the rope is stretched.
2. **Enter.** Not carried and `AgentCarry.IsLift(ask, carryEnterSlope)` → `BeginCarry`. Otherwise
   `Agent.Move(ask)` and the creature stays on the mesh, which is the old behaviour unchanged.
3. **Hold.** The ask is applied as `transform.position += ask` while carried.
4. **Fall.** The motor's own `FixedUpdate` reads this body's velocity back out of how far the
   transform moved since the last step, adds gravity, clamps to terminal speed and integrates. It
   runs **before** the rope's `FixedUpdate` — the motor is `[DefaultExecutionOrder(-100)]` — which is
   the same ordering `PlayerMovement` and `LeashedBody` have: the body moves under its own weight
   first, the rope corrects the result second.
5. **Land.** Nothing asked for a lift last step, the body is descending, and
   `NavMesh.SamplePosition` within `navMeshSnapDistance` (6 m) returns a point it has reached →
   `EndCarry`: `Warp` there, flags back, `isStopped` off.

## Multiplayer

- **Nothing new on the wire.** The carry runs on the machine that already owns the creature — the
  server for a loose one, the rider's machine for a ridden mount, since `MountNetworkSync` hands
  ownership over — and reaches every other machine inside the replicated transform, like all the
  rest of the agent's motion. `Leash.ResolveEnd` has already applied `Network.Owns` before this is
  reached; `selfDriveSuspended` is the second belt, and `RequestTow` refuses on it exactly as
  `LeggedDriver` refuses on `ExternallyPosed`.
- A machine that stops being the driver mid-carry abandons it on the next step rather than
  continuing to integrate a fall for a body whose pose is arriving over the wire.

## Persistence

**Deliberately none.** A carry is something happening to the creature right now, not a property of
it, and it is bounded by a rope that `LeashSaveable` already saves. A world saved mid-hoist reloads
the creature at the position `TransformSaveable` recorded and the motor's existing off-mesh recovery
puts it back on the ground — which is the honest outcome, since the rope's own shape is re-derived
on load too.

## Gotchas

- **Measure the velocity back out of the transform; never accumulate it in a field.** A kinematic end
  reports no velocity to the leash (`LeashEnd.Velocity` reads the Rigidbody, which is not the thing
  moving here), so the constraint contributes no arrest term on this side and repays the whole error
  as position. Reading the fall out of the position is what subtracts each step's correction from the
  next step's velocity — without it the correction is never seen and the creature sinks metres
  through an intact rope. `AgentCarryTests.Hang_AccumulatingTheVelocityInstead_SinksThroughTheRope`
  is the control; the working version settles about **1 cm** below the rope with no swing.
- **A body something is still lifting has not landed, and without that rule the state churns at 50 Hz.**
  A pilot hovering directly above at exactly the rope's length asks for a steep pull while the animal
  is still standing on the sand: it rises a millimetre, is descending again by the next step, finds
  the mesh right where it left it and lands — then re-enters on the next ask, and every round trip
  runs a `ResetPath` and an `Agent.Warp`. `liftedLastStep` is the gate, and it is a plain bool rather
  than a frame stamp precisely because of the execution order: the motor steps at −100 and the rope
  at 0, so the flag one step sets is always read by the next. The creature simply stays held, which
  is the truthful picture of something hauling on its collar.
- **`AbandonCarry` in `OnDisable` is load-bearing.** A carry leaves `updatePosition` off, and only
  `EndCarry` puts it back. A creature streamed out or disabled mid-hoist would otherwise come back
  with its agent unable to move it at all, standing where it was for the rest of the session with a
  clean console — the same trap the mounted leap's save/restore exists to close.
- **`AgentGroundConform` has to be told.** It already refuses to lean a body mid-leap; a carry is the
  same case with no end time, and without the refusal an animal hanging off a jetpack leans into the
  dune forty metres below it.
- **A dropped creature takes no damage, and there is no fall damage to give it.** `PlayerMovement`
  is the only thing in the game that measures a landing. Dropping an animal off a cliff is currently
  free for everybody involved; if that becomes a dominant way to kill things (`GDC-L1-SYS-0007`),
  the fix belongs in `Combat`, not here.
- **A creature carried over ground with no NavMesh keeps falling.** `TryLand` only lands on the mesh,
  so a body dropped where nothing is baked within `navMeshSnapDistance` falls until it finds some.
  That is the same "no NavMesh under this agent" case the motor already warns about after 3 s, and
  `UnderTerrainGuard` is the floor under it. A physics raycast was rejected as the second answer: the
  ground conform's probe is the one probe that decides where the ground is, and a second one that
  could disagree with it is worse than falling.
- **Legged machines are not covered.** `LeggedDriver` implements `ITowable` too, but
  `LeggedLocomotion.Drag` moves `pathPos` and the next settle pulls the height straight back to ride
  height above the terrain, so an ostrich still cannot be hoisted. It is not broken in a new way —
  it was never liftable — and fixing it means touching Invariant I4, the single-author rule in
  [Locomotion.md](Locomotion.md).

## Extending

1. **Another kind of mover** that wants to be liftable implements `ITowable` and decides for itself
   what a pull costs it. Nothing in the leash changes; that is the whole point of the interface.
2. **Another caller** (a tractor beam, a crane, a big bird) needs only to call `RequestTow` with one
   step's worth of ask per physics step. Hand it a distant destination instead and the body is
   teleported: every existing caller — `Leash`, `SingularityWell`, `GrapplingHookArtifact` —
   computes its own step first.
3. **Retuning** is `carryEnterSlope` (how steep a pull has to be before an animal leaves the ground)
   and `maxCarryFallSpeed`. `carryLandTolerance` is not a feel knob — it is a tunnelling guard, and
   `AgentCarry.HasLanded` already widens it by the distance one step covers.
4. Pure functions live in `AgentCarry` so they are testable with no scene, no NavMesh and no rope.
   Add to [AgentCarryTests.cs](Assets/Game/Editor/Tests/AgentCarryTests.cs), which is in `Editor/`
   rather than beside the EditMode tests because it touches Assembly-CSharp types.
