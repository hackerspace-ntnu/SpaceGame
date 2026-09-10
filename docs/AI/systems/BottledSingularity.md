---
system: BottledSingularity
layer: items
summary: "A thrown bottle that inhales everything within 8 m, holds it in a white void for 5 s, and spits it back out"
paths:
  - Assets/Game/Scripts/Items/Artifacts/BottledSingularity
  - Assets/Game/Scripts/Gameplay/Status/SwallowedStatus.cs
  - Assets/Game/Scripts/Gameplay/Status/BodyVeil.cs
  - Assets/Game/Scripts/Core/SceneManagement/Interiors/SingularityVoid.cs
  - Assets/Game/Scripts/Core/Safety/Guards/SingularityVoidGuard.cs
  - Assets/Game/Editor/Items/SingularityBuilder.cs
  - Assets/Game/Editor/World/SingularityVoidBuilder.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/BottledSingularity.prefab
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/SingularityWell.prefab
  - Assets/Game/Resources/Items/Artifacts/BottledSingularity.asset
  - Assets/Game/Editor/Tests/SingularityTests.cs
symptoms:
  - "the bottled singularity does nothing at all when I throw it"
  - "a singularity opened at the world origin instead of where I threw it"
  - "throwing the bottle shoves the thrower sideways"
  - "the singularity is invisible — no sphere, no ring, just the bottle"
  - "the white sphere is opaque, or disappears when the camera is inside it"
  - "a swallowed body never came back and is invisible for the rest of the session"
  - "I am stuck in a completely white room with no way out"
  - "[SingularityVoidGuard] The local player has been in the singularity void"
  - "the singularity eats things but they never go anywhere"
  - "a swallowed body came back with a part of it missing"
  - "the white room is grey on one side, or the floor and the sky are different"
  - "the pull moves crates but never moves a player"
  - "the pull does nothing to a mounted rider"
  - "a saved world comes back with a singularity still open in it"
reads_with: [Artifacts, Multiplayer, Combat]
updated: 2026-09-09
---

# Bottled singularity

A squat sealed flask you throw. Where it lands it opens, drags everything loose within 8 m toward
itself, gulps, collapses — and everything it caught is **taken out of the world** for five seconds,
into a completely white room, before it is thrown back out. No exemptions, including for whoever
threw it.

**Scope:** everything under
[BottledSingularity/](Assets/Game/Scripts/Items/Artifacts/BottledSingularity), plus the void and its
guard (see `paths`). The brief is [Artifacts/BottledSingularity.md](Artifacts/BottledSingularity.md);
this page wins.

## Model

**Two prefabs**, both registered network prefabs: `BottledSingularity.prefab` is the bottle in the
hand and in the sand, `SingularityWell.prefab` is what is thrown.

**Six phases, derived from one stamp.** The server stamps the launch (`launchOrigin`,
`launchVelocity`, `seed`, `launchedAt`, `throwerNetId`) and later the opening (`centre`, `openedAt`).
Everything after is `SingularityWell.PhaseAt` — a pure function of how long ago it opened and the
five authored durations, so no machine holds a phase of its own to get wrong:

| Phase | Default | What it is |
| --- | --- | --- |
| `Flying` | until it lands | On its closed-form arc |
| `Inhaling` | 3 s | Pulling. The white sphere grows from nothing to the full 8 m |
| `Flaring` | 0.25 s | Still pulling. The sphere overshoots to 16 m — the gulp |
| `Collapsing` | 1 s | Everything caught leaves for the void. The sphere goes black and falls to nothing |
| `Held` | 5 s | Nothing to see out here. Everything it took is standing in the white room |
| `Spitting` | 0.15 s | The white ball flashes back and throws the lot out |

**The flight is closed form and the scatter is a function, not a roll.** `SingularityMath` takes the
world's own `Physics.gravity` (**-18 m/s² here, not -9.81**) and turns one seed from `NetArg.B` into
a deflection by integer avalanche arithmetic; nothing calls `Random` again, and the well stays out of
`SaveablePolicy.NeedsSaving`. The pull is `RepulsorBlast.DistanceFalloff` and the release is
`BlastPush.Apply` — the release does not sweep and does not fall off, because everything is at the
centre by then (`SpitVelocity`: a golden-angle fan, then `Scatter`).

## Key types

| Type | Does |
| --- | --- |
| `BottledSingularityArtifact` | The bottle in the hand. `ToolItem`, `UseAuthority.Server`, `maxUses` 1 |
| `SingularityWell` | The thrown bottle. Stamps, flight, sweep, pull routing, swallow and spit |
| `SingularityShell` | The horizon sphere and its ring, the collar, the core, dust and burst. Decides nothing |
| `SwallowedStatus` | `StatusKind.Swallowed`: a flag and a clock, nothing more. Does **not** suppress |
| `SingularityVoidGuard` | Puts a player back when they are in the void with no singularity holding them |
| `BodyVeil` | Renderers and colliders off and back on. Only for bodies the void cannot take |
| `SingularityBuilder` / `SingularityVoidBuilder` | Generate the horizon, and the void scene + its interior asset |

## Flows

**Press.** `OnRequestUse` on the holder's machine — the only honest aim. `ThrowPivot` in `P`, aim in
`R`, seed in `B`. **Never `A`:** that is the hotbar slot code. **Throw:** `Use()` on the server spawns
the well and calls `Begin`, which stamps the launch, records the thrower and retires their oldest
well if this puts them over budget. **Flight:** every machine traces the arc; only the server stamps
`centre` and `openedAt`.

**Inhale and flare.** `OverlapSphere` every physics step on every machine, one entry per body by
`transform.root`, each checked for line of sight. Who acts is a property of the **target**: a player
on their own feet by their own machine, anything `ITowable` by the machine that owns it (the
*rider's* for a ridden mount), any other Rigidbody by the server alone.

**Swallow.** Once, on the first frame of `Collapsing`, from the same sweep. Every caught body takes
`StatusKind.Swallowed` — a flag, not a hold — and the server hands it to
`InteriorManager.EnterInterior`, which routes a player through their own `PlayerInteriorTransit` and
moves everything else directly. A body with no `NetworkObject` (chunk scenery) cannot go, because the
move would not replicate; **each machine veils its own copy** instead.

**Spit.** Once, on the first frame of `Spitting`. Out of the void first, flag cleared second — the
guard's question is "in the void with no flag" — then the server alone throws, through `BlastPush`.

## Multiplayer

Server-authoritative, because it moves other people's bodies and props. Players are the exception:
their movement is owner-authoritative, the server owns the fact that a well is open, that fact has
already replicated because the well is spawned, and each caught player's own machine reads it and
moves their own body. **No message is sent for the pull.** The swallow is different — a discrete
latch, not a per-frame nudge — so it rides `NetMsg.StatusSet` on the victim's own relay, and the move
into the void is the server's alone because scene membership is session state.

`SingularityWell` carries `[DefaultExecutionOrder(200)]`, after `PlayerMovement`, which *assigns*
horizontal velocity while grounded, and no `NetworkTransform`: its pose is arithmetic.

## Persistence

**Nothing of the effect.** An unthrown bottle is an ordinary item with a charge under `UsableItem`'s
`"uses"` key. The well itself is unsaved, and that is a property of what its prefab **carries**:
`SaveablePolicy.NeedsSaving` opts in anything with a non-kinematic Rigidbody, a `HealthComponent`, a
`PickupableItem` or a `NavMeshAgent`. `SingularityTests.TheThrownWellIsNotSaved` guards it.

A player saved mid-hold *is* saved as being in the void (`InteriorVisitSaveable`) and reloads there
with no well left to let them out. `SingularityVoidGuard` is what puts them back.

## The void

A generated scene, `Assets/Game/Scenes/Interiors/SingularityVoid.unity`, entered through the ordinary
interior system: refcounted, one return position per occupant, exterior chunks pinned. It is the only
interior in the game **with no door**, which is what `SingularityVoidGuard` is for.

Everything in it is **URP/Unlit pure white** — a 600 m floor slab and a 400 m sphere with its cull
switched off, and nothing else. Unlit is the mechanism, not a shortcut: a *lit* white surface is
shaded by the sky and comes out grey on one side, and the brief is that floor and ceiling are
indistinguishable. Both are the same colour to the last bit, so the seam between them is invisible
and there is no horizon, wall or corner to orient by. Ambient is flat white so that what an occupant
*brings* is lit rather than silhouetted.

## Presentation

`SingularityShell.Show(phase, progress, radius)` runs every frame everywhere and is idempotent, so a
machine meeting the bottle already inhaling gets the pose and none of the one-shots. The sphere is
Unity's unit sphere scaled to `radius * 2` (**diameter is the scale**), coloured through a
`MaterialPropertyBlock` from white `(1,1,1,0.35)` to opaque black; the ring is a generated annulus.

## Gotchas

- **The bottle is born inside the thrower.** `IsLandingHit` skips the thrower's root **and** refuses
  any hit at distance 0 — a `SphereCast` that starts overlapping reports distance 0 with its point
  left at `Vector3.zero`, so without it the singularity opens at the world origin. Skipped **by the
  flight only**; the pull, the swallow and the spit have no exemptions. `ExcuseThrower` also pairs
  `Physics.IgnoreCollision`, or the static well collider punts them sideways.
- **Both halves are generated, not modelled** — `Build Singularity Horizon` and `Build Singularity
  Void` under `Tools > SpaceGame`. Skip either and the well works perfectly and does nothing you can
  see; two wiring tests are all that notice. The void scene must also be **enabled in Build
  Settings** or a client's join dies outright — Netcode resolves scenes by a hash of their path.
  Both materials are set field by field because a `.mat` freezes the defaults it was born with.
- **The veil (chunk scenery only) restores only what it took**, and must be re-asserted every tick
  or LOD and culling write `Renderer.enabled` back mid-hold.
- **`Spit` lifts the veil before it throws** (`BlastPush` is handed a collider), and **`OnDisable`
  gives everything back** — veils, freezes *and* the void — because a well ends on every path that
  is not the spit: retired over budget, chunk unloaded, world quit.
- **`Swallowed` must never be re-applied to a body already in the void.** `InteriorManager` reads a
  second entry as a re-entry and overwrites the return position with a point *inside* the void, so
  that body can never get back to the desert. `SwallowedStatus.CanApply` refuses a refresh for this.
- **A level throw lands inside its own radius**: about 7 m against a radius of 8. Lob it.
- **Being swallowed is ~6 s somewhere else.** Keeping control keeps it off the worst of the
  commitment axis (`GDC-L1-FEEL-0008`), but there is nothing to do in there and no way out — still
  the longest the game takes you out of play. Retune `holdSeconds` before adding an escape.
- **`Live`, `Swept` and `Reached` are statics** and survive a world unload and play mode itself.

## Extending

- **Retuning the shape** is `radius`, `inhaleSeconds`, `pullSpeed`, `pullEdgeShare`; the *drama* is
  `flareSeconds`, `collapseSeconds`, `holdSeconds`, `spitSeconds`. The boundaries are running totals,
  so moving one moves everything after it and no gap can open between two.
- **`flingUpwardTilt` is not a taste knob**: vertical velocity is the half `PlayerMovement` never
  deletes, and too small a tilt is a fling deleted on the tick it is applied.
- **Anything else that eats a body** applies `StatusKind.Swallowed` rather than growing its own flag.
