# Ragdoll physics — design

*2026-08-24*

## Problem

Three places in the codebase already have a ragdoll-shaped hole, and all three are currently lies:

- `PlayerController.OnDeath` ends with a bare `// TODO: ragdoll`. A dead player stands frozen
  upright in whatever pose the animator left them in.
- `HealthReactionModule`'s header comment promises "death cleanup: ragdoll trigger, despawn timer,
  and noise emission". Only the last two exist; a dead creature stands still and then vanishes.
- `RepulsorGauntletArtifact`'s class doc says the blast "ragdolls everything in a wide cone". It
  does not. Players take a velocity through `NetMsg.Flung` and keep full control; leap-capable
  mounts hop; everything else gets a cosmetic hurt flinch and is otherwise unmoved.

There is no ragdoll infrastructure at all — `CharacterJoint` appears zero times in the project.

## Goal

Bodies go limp when they die, and when the repulsor gauntlet's shock wave catches them. A live
victim tumbles and gets back up; a dead one stays down until the existing despawn timer takes it.

## Decisions taken up front

| Question | Decision |
| --- | --- |
| Living things hit by the wave | Ragdoll, tumble, then get up |
| Player body | Ragdolls on death **and** when blasted |
| Rig coverage | One generic runtime builder covering every rig, no per-prefab authoring |

The caster is unaffected: `FireBlast` already excludes `root == ownerRoot`, so the repulsor-jump
and its recoil survive untouched.

## Architecture

New folder `Assets/Game/Scripts/Gameplay/Ragdoll/`. Gameplay rather than Presentation — this takes
control away from the player, so it is not cosmetic. No asmdef: the adapters reference
`AgentController` and `PlayerMovement`, which live in Assembly-CSharp, and an asmdef cannot
reference Assembly-CSharp.

### `RagdollSkeleton` — pure math, no scene state

Static class. Bone selection and capsule sizing for an arbitrary rig:

- Which bones deserve a physical body, ranked by how much skinned-mesh vertex weight each carries,
  so fingers, toes and jaw bones are skipped and the spine, limbs and head are not.
- Capsule radius, height and axis for a bone, derived from the bone's own length and the spread of
  the vertices weighted to it.
- The settle predicate: is this body at rest, given its speeds and how long it has been slow.

Pure so it is unit-testable and so one implementation answers for a Mixamo humanoid, an ostrich and
a hexapod alike. This mirrors what `RepulsorBlast` already does for the blast cone.

### `RagdollRig` — the physical skeleton

MonoBehaviour on the entity root. Owns:

- **Lazy build.** The capsules, `Rigidbody`s and `CharacterJoint`s are created on the first limp,
  not at spawn. A creature that never falls over never pays for them.
- **`GoLimp(Vector3 impulse)` / `Recover()` / `IsLimp` / `IsSettled`.**
- **Root-follow.** Every frame while limp, the root transform moves to the hip bone and the hips are
  compensated back, so the object's transform stays where the body actually is. Without this a
  corpse flies ten metres while its `NetworkTransform` and its save record both still say it died
  standing where it was.
- **Recovery pose blend.** On recovery the rig snapshots the bone pose, re-enables the animator and
  blends from the snapshot into the live animation over `recoverBlendSeconds`.

All tunables serialized on the component: joint swing/twist limits, total mass, minimum bone weight,
settle thresholds, blend duration, maximum limp time.

Recovery is a pose blend rather than a get-up animation because there are no get-up clips anywhere
in the project — four `.controller` files exist and none has a recovery state. Authoring ten of
them (humanoid, ostrich, hexapod, rat, golem, robots) is a different project.

#### Two kinds of rig, not one

An audit of the wired prefabs turned up a split the first design missed: **several creatures have no
skinned mesh at all.** `Golem`, `CrabWalker6` and `HumanoidRobot` report zero `SkinnedMeshRenderer`s
between them — they are hierarchies of separate rigid meshes, positioned each frame by
`LeggedLocomotion`'s IK. A builder that only walks `SkinnedMeshRenderer.bones` finds nothing on any
of them, and finds it *silently*: the prefab looks correctly wired and does nothing on the first
blast that hits it.

So `RagdollRig` measures whichever it finds:

| Rig | Bones from | Importance | Collider |
| --- | --- | --- | --- |
| Skinned (Nomad, Ostrich, player, rats) | `SkinnedMeshRenderer.bones` | vertex weight carried | capsule down the bone |
| Rigid parts (Golem, crab, humanoid robot) | `MeshFilter` transforms | mesh bounds volume | box around the part's own mesh |

The two importance measures do the same job — drop fingers, drop bolts — and the rigid side gets the
*better* collider, because a part that draws itself has real bounds rather than a length to estimate
from.

The skinned measure also falls through to the rigid one when it yields fewer than
`minimumUsefulBones`. A hard-surface model is often rigid pieces parented to bones plus one or two
small skinned bits, so the vertex weight lands on a couple of bones and the skinned pass returns a
confident two-bone "skeleton" for a fifty-bone rig.

#### Self-collision is off, and that is the design

A body's own bones do not collide with each other (`RagdollRig.selfCollision`, default off). This is
not a shortcut around bad colliders — it is forced by the geometry.

Two thighs are **siblings**: both jointed to the hips, neither jointed to the other, so
`CharacterJoint`'s built-in "don't collide with what I'm jointed to" does not cover them. Measured on
the Nomad, they interpenetrate by **15 cm** and the calves by **9 cm**, and PhysX spends every tick
trying to resolve a penetration it can never win. That is what a jittering ragdoll is made of.

It cannot be tuned away. Stopping two thighs overlapping at the hip needs a radius of ~5 cm on a
42 cm thigh, against a real thigh's ~9 cm — anatomically correct limbs *necessarily* overlap at the
joint, which is why real ragdolls rely on collision filtering rather than thin capsules. The cost is
that a limb can pass through the torso, which nobody notices on a corpse; the alternative is jitter,
which everybody does.

Two implementation notes that are easy to get wrong:

- The ignore state is **re-applied on every limp**, not once at build, because it does not survive a
  collider being disabled and re-enabled — and recovery does exactly that. Applied once, a body would
  fall correctly the first time and jitter every time after.
- Verified with `Physics.GetIgnoreCollision`, not by eye. The diagnostic reports penetrating pairs
  *and how many are unfiltered*; the second number is the one that matters.

#### The root follows the hips exactly

While limp the root transform is placed **at the hip bone**, with no attempt to drop it to where the
feet would be.

Subtracting a standing hip height is the obvious thing to do and it is the bug that made ragdolls
unusable. That offset is measured while the creature is upright (~1 m), so once the body is lying
down — hips a quarter of a metre off the ground — it plants the root the better part of a metre
**underground**, past `UnderTerrainGuard`'s 0.5 m tolerance. The guard then does exactly what it
exists to do: teleports the root to 1.2 m above the surface, dragging the whole bone hierarchy with
it and zeroing the velocities. The body falls, lands, goes under again, and is lifted again a quarter
of a second later — forever. That is the "falls down and springs back up in a loop" bug.

Root-is-hips is also the only choice a watcher can mirror, since it reconstructs the hips from the
replicated root and cannot know a pose-dependent offset.

`UnderTerrainGuard` is additionally **held off for the duration of a limp** and restored on recovery.
Its own header says it "never fires during normal play" because "the only way to reach the depth this
reacts to is for something to have already gone wrong" — a ragdoll is a state that did not exist when
that was written, and a body thrown at 48 m/s into a slope clips under the surface as an ordinary
part of falling over.

#### Coming to rest

Three things, because "settled" and "still" are not the same thing:

- **Damping.** `angularDamping` 0.6, `linearDamping` 0.05. With no angular drag nothing removes
  energy, so a chain of jointed bodies trades it back and forth through the joint limits indefinitely.
- **Solver iterations** raised to 14 from Unity's default 6, which is meant for loose props rather
  than a twenty-body joint chain. Under-solved joints leave a residual correction every tick.
- **Sleep on settle.** `IsSettled` is a *threshold* — the fastest bone has stayed under a speed for
  long enough — and a body sitting just under that threshold shivers there for as long as anyone
  watches. Damping is asymptotic and never removes it. Sleeping ends it outright, and anything that
  hits the body afterwards wakes it again by itself.

Sleeping also puts a hard ceiling on the whole thing, because `IsSettled` goes true at
`maxLimpSeconds` (4 s) whether the body agrees or not. Two ordering traps come with it: the follow
and the watcher's pin both stop once asleep, because writing a transform **wakes** the Rigidbody it
belongs to — a follow that kept running would sleep the body and wake it again every frame, which is
not sleeping at all.

### Audit

`Tools/SpaceGame/Ragdoll/Audit Skeletons` builds every wired prefab's rig and reports what came out.
It exists because this failure is invisible from the prefab — a correctly-wired `AgentRagdoll` over a
rig it cannot read looks identical to a working one until the first blast. Results:

| Prefab | Bones / joints | Measure |
| --- | --- | --- |
| Nomad, BountyHunter, PlayerCharacter(+Networked) | 19 / 18 | skin |
| DuneRat, Vrescal, CrabWalker6, PatrolRobot 2 | 20 / 19 | skin (crab: parts) |
| Golem, PatrolRobot 3 | 18 / 17 | parts / skin |
| PatrolRobot, DeathmatchBot | 17 / 16 | skin |
| Ostrich, NomadOstrich | 11 / 10 | skin |
| HumanoidRobot | 8 / 7 | parts |
| **PatrolRobot 1** | **2 / 1** | parts |

`PatrolRobot 1` ("Robert") is an asset limit rather than a code one: the model has four mesh parts
and near-rigidly bound skinning, so two bones is its honest maximum by either measure. Every other
body has a real skeleton.

### `AgentRagdoll` — the agent adapter

Suspends everything that writes the agent's bones or root, then hands it back:

| Layer | Suspend | Resume |
| --- | --- | --- |
| `AgentController` | `enabled = false` | `enabled = true` |
| `ISelfDrivingMotor` (NavMeshAgentMotor) | `SuspendSelfDrive()` | `ResumeSelfDrive()` |
| `LeggedLocomotion` | `enabled = false` | `enabled = true` + rebase |
| `Animator` | `enabled = false` | `enabled = true` |
| Root collider + `Rigidbody` | disabled / kinematic | restored |

`LeggedLocomotion` cannot use `ExternallyPosed` here. That flag means "someone else writes the root,
but I keep solving the legs" — exactly wrong for a ragdoll, which owns the bones. The component has
to be switched off.

Because `LeggedLocomotion` holds `pathPos` and every planted foot in **world space** and rewrites
the body transform from them each `LateUpdate` (invariant I4), resuming it after the body has moved
would walk the creature straight back to where it fell. Recovery therefore raises the existing
`ITeleportAware.OnTeleported` with a `TeleportMove` from the pre-limp pose to the settled pose. That
rebases the path, the feet, the ground normals and the swing arcs in one rigid change of frame —
the mechanism already exists for exactly this class of problem and costs nothing to reuse.

`NavMeshAgentMotor` needs the same treatment by a different route: resuming re-enables a
`NavMeshAgent` whose internal position is where the creature died, so the resume warps it onto the
NavMesh at the resting position first.

### `PlayerRagdoll` — the player adapter

Suspends `Input`, `PlayerMovement` and `PlayerLook` — the same three `PlayerController.ApplyDeathFreeze`
already freezes — plus the animator, and switches the capsule collider and body over to the ragdoll.

**Camera.** A first-person camera bolted to a tumbling head is unusable. On going limp the camera
detaches from the head and lerps to an over-the-shoulder framing of the hips; on recovery it lerps
back. The player's own head must be made visible again for their own camera while limp, or they
watch a headless corpse (the per-camera head hide runs off `beginCameraRendering`).

**Death vs knockdown.** Death is permanent limpness; `PlayerController` keeps owning the death
freeze and the death screen, and the ragdoll is layered under it. A knockdown recovers.

### Netcode

Ragdoll is presented on **every** machine, because bone transforms do not replicate. Position
converges through the authority split the codebase already uses, expressed as `RagdollRig.Drives`:

- **Agents** are server-authoritative — `Drives` is `Network.Simulates`.
- **Players** are owner-authoritative, the rule `FlungBody` already follows — `Drives` is
  `Network.Owns`.

The two answers need opposite plumbing, which is why it is a flag rather than an assumption:

- **The machine that drives** applies the impulse, and the root follows the hips
  (`RagdollRig.FollowHips`).
- **A machine that is only watching** applies no impulse — it would carry the body the distance a
  second time and land it at double the range — and instead pins the hips to the replicated root
  (`RagdollRig.PinHipsToRoot`). The hips are kinematic there and everything below them is not, so
  one bone is dragged by the wire and the whole body flails from it. `MovePosition`, not a direct
  assignment, so the joints get a sweep to follow rather than being left behind.

Settling is measured across **every** bone rather than the hips, for the same reason: on a watching
machine the hips are the one bone driven from outside, so their speed reports the wire rather than
the body.

**New message: `NetMsg.Knockdown = 82`**, server → everyone, on the victim's relay, alongside the
`NetMsg.Flung` the blast already sends. It carries the impulse in `P` and **how long the victim
stays down in `A`**, as milliseconds.

The duration travels with the message rather than being decided locally because it is the only part
of the recovery every machine can agree on. Settling cannot be — a watcher does not simulate the
flight, so its ragdoll comes to rest on a different schedule from the one that does. Each machine
waits out the shared floor *and* its own body on top of it, which keeps them within a frame or two
without a second round trip to say "get up now".

A new message rather than reusing `Flung`, because `Flung` is shared three ways and one of them is
self-inflicted: `GravelBlasterArtifact` flings the *holder* as self-propulsion. Hanging ragdoll off
`Flung` would knock players down every time they fired their own gravel blaster.

Death needs no new message — `NetworkedHealthComponent` already replicates death, so every machine
raises `OnDeath` and goes limp on its own.

### Recovery timing

Two constitution principles constrain this, since a player now loses control to something they did
not ask for:

- **`GDC-L1-ANIM-0002`** (never let animation block input). Control returns at the *start* of the
  recovery blend, not the end. The blend is display; the player is already driving.
- **`GDC-L1-FEEL-0002`**. Its own exception clause separates latency ("slow to hear you") from
  commitment ("time to carry out what it heard"). A knockdown is neither, so it gets a hard ceiling:
  `maxLimpSeconds` fires recovery even if the body never settles. A player wedged against a rock
  never loses control indefinitely.

Death ignores both — a corpse stays limp until the existing `despawnDelay` takes it.

### Persistence

Root-follow means the saved transform is already the resting place, so a corpse reloads where it
fell. On a restored death (`health.IsRestoring`, the path `HealthReactionModule.ApplyDeadState`
already guards) the rig goes limp **settled, with no impulse and no sound** — the body comes back
lying down instead of standing up.

Deliberately not saved:

- **Bone pose.** A corpse returns in a fresh slump at the correct position.
- **Knockdowns.** They last under two seconds; a save caught mid-tumble restores a standing creature
  at the recorded position.

### Performance

`RagdollBudget` — a cap on concurrent limp bodies (`GDC-L1-PERF-0004`). Past the cap a corpse
freezes to a static pose: joints and rigidbodies destroyed, bones left where they lie. Twenty
jointed skeletons from one blast is a real frame cost.

Eviction takes the oldest body that has already **settled**, falling back to the plain oldest.
Freezing preserves the pose as it is, so freezing a body still falling preserves a pose nobody
wants — worst case a creature that went limp this frame, left standing bolt upright and dead. That
case is real rather than hypothetical: a world reloading with a graveyard in it puts every corpse
limp on the same frame.

Both adapters watch for having been frozen out from under them (`suspended && !rig.IsLimp`) and take
the body back. Without it a knocked-down creature stays suspended for good with its brain switched
off — and a knocked-down player stays unable to move.

## Out of scope

- **Mounted creatures keep the leap.** A rider is parented to the seat, so ragdolling underneath one
  drags the player through the ground. A mount that *dies* ejects its rider first, then goes limp.
- **Vehicles never ragdoll**, empty or not — everything under `Assets/Game/Prefabs/agents/Vehicles/`
  (`DesertCrawler`, `RigWalker`, `DuneOrnithopter`, `ShipRV`). Excluded by folder because no
  *component* separates them: the `DuneOrnithopter` and the `Ostrich` carry an almost identical set
  (`AgentController`, `MountModule`, `SteerModule`, a motor), since a rideable flying machine and a
  rideable bird are the same kind of thing to everything except a ragdoll. The runtime rider check is
  no help — it only refuses while somebody is actually aboard, and an empty `ShipRV` going limp is
  still absurd; it is a mobile base with a `SpawnPoint` and a sandstorm shelter on it.
- **Loose rigidbodies** (crates, barrels) already fly correctly under `AddForce`. Untouched.
- **Only the gauntlet wave** triggers knockdown. `SuckerPuncherArtifact` and `GravelBlasterArtifact`
  keep plain `Flung`.

## Verification

- Edit-mode tests for `RagdollSkeleton` in `Assets/Game/Editor/Tests/` (Assembly-CSharp-Editor,
  where `RepulsorBlastMathTests` lives): bone selection, capsule sizing, settle predicate.
- Prefab wiring is done by `Tools/SpaceGame/Ragdoll/Wire Prefabs`
  (`Assets/Game/Editor/AssetPipeline/RagdollWiring.cs`) rather than by hand. Most agent prefabs are
  variants whose root is a prefab instance, and adding a component to one of those by editing the
  YAML writes a modification entry against a source object rather than a component block — the kind
  of thing that silently produces a prefab Unity cannot open. The tool is re-runnable and skips what
  is already wired.
- Host + client: blast a crowd, confirm both machines see the tumble and agree where the bodies end
  up; confirm the caster is not knocked down by their own blast.
- Kill a creature, save, reload, confirm the corpse is lying in the same spot and does not stand up.
