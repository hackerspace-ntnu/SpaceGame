# Peaceful-until-provoked creatures

Design record for the Golem and Nomad temperament change, 17 Aug 2026.

## What was asked for

- The Golem stops being hostile on sight. It becomes peaceful, and fights only what hurts it.
- The Nomad gets the same temperament, walks instead of running, and carries a weapon he
  attacks with. (Revised mid-design from a ranged weapon to **close combat with a wooden
  walking staff**.)

Grudge rule, as specified: hostile the whole time the attacker is close; once they leave a given
range, calm after 60 seconds.

## The central idea

Peaceful is the **absence of anyone to be hostile toward**, not a disabled module or a state flag.

Three facts in the existing architecture make this nearly free, and finding them is most of the
design:

1. `ChaseModule`, `CloseCombatModule` and `AgentRangedCombatModule` all act only while
   `AgentTargeting` holds a target. **None of them consults the faction table itself.**
2. `AgentTargeting` only ever queries the registry for candidates it is **Hostile** toward.
3. `FactionRelationshipTable.Get` returns `Neutral` for any pair it has no row for.

So a faction with **no rows in `GlobalRelationships`** can never acquire anyone. Its creatures
wander, with every combat module still attached and simply never claiming a frame.

That leaves exactly one job for new code: when the creature is hurt, hand `AgentTargeting` the
attacker, and keep handing it over while the grudge lasts.

## Components

### `FaunaFaction.asset` — new, deliberately empty

Created by `GolemBuilder`. It has **no row in `GlobalRelationships`, and must never be given one**;
a single Fauna/Player row silently reverts every Fauna creature to attacking on sight, with no code
change to explain it. `ProvocationTests.Fauna_IsNeutralTowardThePlayer` guards that absence.

The Golem **moved** to Fauna rather than Wildlife being made peaceful, because DuneRat and Vrescal
are also Wildlife and are still meant to hunt. Corollary worth knowing: nothing hunts Fauna either,
so anything that treated the Golem as prey no longer sees it.

The Nomad needed no faction change at all — `NPCFaction ↔ PlayerFaction` was already `Neutral`.
He was always peaceful; he just had nothing to fight back with.

### `ProvocationModule` — new

`Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs`, execution order −40, between
`AgentTargeting` (−50) and `AgentController` (0) so modules see one coherent target per frame.

```
on damage → resolve the EntityFaction above the damage source; that is the aggressor
each frame while provoked:
    inside leashRange  → clock = 0; re-assert the target
    outside leashRange → clock runs
    clock ≥ 60s        → forget, clear the target, go back to wandering
```

The leash, not a timer starting at the hit, is what implements the spec: a fight cannot be waited
out from on top of the creature, and one stray shot does not buy a pursuit across the world.

Re-asserting every frame is load-bearing. `AgentTargeting.Reevaluate` can never re-find this
candidate (it is Neutral), and the line-of-sight staleness pass drops targets that break sight —
so without re-assertion a creature loses its grudge behind the first rock.

Provocation is deliberately **not** persisted as "angry": `OnEnable` calls `Forget()`, so a creature
that respawns or streams back in comes back calm. Loading a world into an ambush whose cause
happened before you quit is not the state the world was saved in.

| | Golem | Nomad |
|---|---|---|
| leashRange | 45 m (= its `loseRange`) | 30 m |
| calmDownDelay | 60 s | 60 s |
| damageThreshold | 5 (rock shrugs off scrapes) | 1 |
| fights with | fists, 45 dmg | staff, 18 dmg, 2.76 m reach |

`leashRange` must stay at or below `AgentTargeting.loseRange`, or the grudge is re-asserted and
dropped on alternate frames.

## The bug this would have silently died on

`Projectile.cs` passed **its own transform** as the damage source. `HealthComponent` stores that as
`LastDamageSource`, and everything asking "who hit me" resolves an `EntityFaction` from it. A loose
projectile has no `EntityFaction` above it and is destroyed on the same frame, so both
`AgentTargeting.LastAttacker` and provocation resolved to a dead object and gave up.

Shooting a peaceful creature with a player firearm would simply never have made it angry.
`Projectile` already tracked `ownerRoot` for its self-hit test; it now passes that. This also fixes
the last-attacker bias for every existing hostile agent.

## Nomad gait

Three things decide how he reads, and two of them look like "animation speed" without being it:

| | original | first pass | **final** | what it controls |
|---|---|---|---|---|
| `animationSpeedMultiplier` × `walkAnimBoost` | 1.5 × 2 = 3 | 2.67 × 1 | **1.54 × 1** | which clip the blend tree picks |
| `NavMeshAgent.speed` × `walkSpeedMultiplier` | 2.2 × 0.65 = 1.43 m/s | 1.5 × 1.0 | **4.68 × 0.556 = 2.6 m/s** | how fast he travels |
| `animatorSpeedScale` | *nothing set it* | 0.63 | **1.09** | **how fast the clips play** |

The third is what caused the original "jogging" read, and no combination of the first two could
have fixed it: neither changes playback rate, so a character moving slower than his clip's authored
stride skates on every step. `AgentAnimatorDriver` gained `animatorSpeedScale` for it.

**But it is global** — it scales the attack and every other one-shot along with the walk. The first
pass set it to 0.63 to match a deliberately slow 1.5 m/s walk, and put the staff swing into slow
motion as a side effect. That is the coupling to remember:

> Matched feet require `rate = groundSpeed / strideSpeed` **exactly**. So walk speed and animation
> rate cannot be tuned independently — they move together. Wanting a faster walk *and* a snappier
> attack is one change, not two.

Final: 2.6 m/s walk (≈1.5 m/s on a human frame; he is 3 m tall) puts the rate at 1.09, so every
clip now plays slightly *faster* than authored — 73% faster than the first pass.

The **run came back** for one reason: `ChaseModule` asks for it, and with `walkSpeedMultiplier = 1`
a provoked Nomad closed at walking pace. Its speed is not free — the blend tree samples run at 7.2
against walk at 4.0, so the run must be exactly 1.8× the walk (4.68 m/s) or the tree lands between
clips and he shuffles. `ProvocationTests.Nomad_WalkAndRunBothLandOnTheirBlendSamples` asserts both.

The stride is **measured** (`AnimationClip.averageSpeed` × height ratio), not hard-coded, so a
re-export re-derives it. `walking.fbx` turns out to be authored in place, so it falls back to a
documented 1.35 m/s and says so in the log.

Attack cadence: cooldown 1.1 s, commit 0.32 s. The commit must not outlast the swing clip, or the
attack ends with him standing still waiting for a timer.

## Facing you at conversation distance

`WatchModule`, reused rather than written — it already is "stop and face whoever is inside this
radius", and its default `requiredRelationship` of Neutral is exactly what NPCFaction is toward the
player. `FacePlayerModule` is the same behaviour with an Allied default and would have needed the
same override.

- **radius 6 m**, one metre wider than the player's 5 m `Interactor` cast. Turning to face you
  exactly as the prompt appears reads as reacting to a UI event; being already turned reads as
  having seen you coming.
- **priority 10 (Ambient)**, set explicitly. Unity does not call `Reset` for a component added from
  a script, so it would have kept its serialized default of Fallback (0) — tied with `WanderModule`,
  resolved by component order, so he would have wandered past you roughly half the time for no
  visible reason. Ambient also keeps him below Chase (20), so a provoked Nomad does not stop to
  politely face the person he is fighting.

## Walking staff

`components/props/walking_staff.blend` — four variations, hero exported to
`Assets/Game/Art/Models/Weapons/WalkingStaff/walking_staff.fbx`. Full build record in
`walking_staff_BUILD.md`. Zero materials added to the palette.

A **visual-only FBX**, not one of the gun prefabs: those carry `NetworkObject`, `PickupableItem`
and `DropItemPhysics`, so parenting one to a bone nests a NetworkObject and hangs a droppable
physics item off an NPC. An equipped visual must be inert.

Two things are measured on a real instance rather than dialled in, both after
`CorrectScaleAndSole`, because the staff hangs inside the hierarchy that pass rescales:

- **The shaft axis**, derived from the geometry. Assuming the Blender +Z → Unity +Y conversion put
  it on local +Y was wrong, and standing the wrong axis up produced a staff scaled to 42 m across
  whose length check nonetheless passed.
- **`CorrectScaleAndSole` had to learn to ignore held props.** It sizes the character by the extent
  of everything he renders, and a 1.86 m staff at chest height made him "measure" far too tall — a
  character who shrinks whenever you give him something to carry.

## Verification

`ProvocationTests` — 14 tests: the empty-relationship invariant, Wildlife still hostile (so DuneRat
and Vrescal are unaffected), attribution climbing from a child collider to the entity, the damage
threshold, self-damage, forgetting, prefab wiring for both creatures, both gaits landing on their
blend samples, a guard against the global slow-motion regression, and the facing radius/priority.

Full EditMode suite: **895 passed, 3 failed, 1 skipped**. The 3 failures are pre-existing persistence
wiring on prefabs untouched by this work (DuneRat, PatrolRobot 1–3, DuneOrnithopter, ShipRV, and 8
creatures/vehicles); the skip is a deliberately-parked contract test.

One regression was caused and fixed during the work: re-running `GolemBuilder` overwrites the prefab
wholesale and dropped the `SaveableEntity` that had been added to the Golem by hand after the last
build. The savers now live in the builder, so a rebuild reproduces them.
