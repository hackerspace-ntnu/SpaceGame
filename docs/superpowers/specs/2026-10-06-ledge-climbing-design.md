# Ledge climbing — design

Date: 2026-10-06 · Status: approved in conversation, awaiting spec review

## Goal

Pressing **Jump** in front of something too tall to jump onto climbs it — onto the top if there is room
to stand, over it if it is too thin to stand on. The grappling hook ends the same way: reeled to a hook
near an edge, the player hangs below the lip and **Jump** pulls them up. On top of that, a separate,
removable layer lets a player **holding Jump in the air** catch a ledge as they pass it.

The main functions (wall, obstacle, grapple) must stay predictable; the mid-air grab is an add-on that
can be switched off without changing them.

Design principles applied: `GDC-L1-FEEL-0003` (Jump at an unjumpable wall *means* climb; hold-to-grab
forgives timing), `GDC-L1-FEEL-0002` (the climb starts on the press), `GDC-L1-FEEL-0008` (the climb is
short and committed: movement keys are ignored until it ends, look stays free).

## Decisions

> **Revised after play-testing (2026-10-06):** a ledge is now *an edge the hands can take* — a walkable
> top with room above it for hands and head — not "a top a standing body fits on". The grapple finds that
> edge at its hook point (`LedgeProbe.FindAtHook`) instead of from the feet. The "up vs. over" row below and
> probe steps 3–5 are superseded; see docs/AI/systems/LedgeClimbing.md.

| Question | Decision |
| --- | --- |
| Interaction style | Short scripted climb, owner-side; not a free hang/shimmy system |
| Highest ledge (ground, rope) | `maxReach` ≈ **4.5 m** above the feet — jump apex (1.36 m) + arms' reach on a 3 m body |
| Highest ledge (mid-air) | `airReach` ≈ **3.3 m** above the feet — arms only, no jump bonus; lip must be above the feet |
| Lowest ledge that climbs | `minClimbHeight` ≈ **1.0 m**; lower stays a normal jump |
| Up vs. over | **Always climb on top if a standing body fits there**; vault only obstacles too thin to stand on (≤ `maxVaultThickness` ≈ 1 m) |
| Grapple end | Winch arrival with an edge in reach → **hang** below the lip; **Jump** climbs. No edge → today's release boost, unchanged |
| Jump on a swinging rope | Climbs if an edge is in reach; otherwise does nothing (as today) |
| Mid-air trigger | **Jump held** while airborne grabs the first ledge that comes into reach |
| Mid-air vs. back-gear double tap | **The ledge wins** when one is in reach; elsewhere the double tap is unchanged |
| Body motion | Velocity-steered dynamic body (the `LadderClimber` way), never a teleport |
| Animation | Proper Mixamo clips as `CharacterAction`s; climb timing taken from the clip |
| Testing | Manual, by the user, host and client. No automated tests in this change |

## Components

### `LedgeProbe` — detection (shared)

A pure query against physics: given an origin (the feet), a facing direction (flat), a reach and the
body capsule, return one of `ClimbUp(standPoint, lipHeight)`, `Vault(exitPoint, lipHeight)` or `None`.

1. **Wall.** Sphere-cast along the facing ≈ 1 m (`wallProbeDistance`). Must hit a steep surface
   (normal steeper than `maxWalkableSlope`). Triggers, the player's own hierarchy and the ridden machine
   are ignored (same filtering as `AimProvider.NearestOutside`).
2. **Lip.** Downward sphere-cast just past the wall face (`lipInset`), from feet + reach down to
   feet + `minClimbHeight`. Must land on a walkable surface (slope < `maxWalkableSlope` ≈ 45°).
3. **Headroom.** The column from the current body up to the lip height plus a body height is clear.
4. **Stand on top.** A standing capsule fits at the lip just past the edge (`standInset`) → `ClimbUp`.
5. **Vault.** Otherwise walk the top forward up to `maxVaultThickness` to find the far edge; a capsule
   fits just past it at lip height → `Vault`. Otherwise `None`.

All distances are serialized tunables on `LedgeClimber` and passed into the probe.

### `LedgeClimber` — the main feature

On `PlayerCharacter.prefab` beside `LadderClimber`. Owner only (disabled with the rest of movement on
remote copies). Owns the climb motion and the two predictable entry points:

- **Ground:** `PlayerMovement.OnJump` calls `LedgeClimber.TryClimb()` **first** and only jumps if it
  returns false. An explicit call, not a second `OnJumpPressed` subscriber — two subscribers fire in
  undefined order and the leg jump could leave the ground in the same step the climb starts.
- **Rope:** while a rope holder is registered (`OfferRopeClimb(facing)` / `WithdrawRopeClimb()`),
  `TryClimb()` probes from the hanging body with `maxReach` and that facing instead of the body yaw.
  `TryClimb()` returns false when neither grounded nor offered a rope.
- **`TryClimbFrom(origin, facing, reach)`** is the entry the mid-air layer uses.

Public surface: `TryClimb()`, `TryClimbFrom(...)`, `OfferRopeClimb(Vector3 facing, Action onClimb)`,
`WithdrawRopeClimb()`, `RopeLedgeInReach(Vector3 facing)`, `IsClimbing`, `Feet`, `LetGo()`.

**Motion** (FixedUpdate, owner, `PlayerMovement.SetClimbing(true)` for the duration):

1. **Pull-up.** Hold the body `wallStandoff` off the wall, rise until the feet are `lipClearance` above
   the lip. Duration = the action's time to its **lip mark** (`CharacterAction.SecondsTo`); speed =
   height ÷ that duration, so the hands meet the lip on every screen whatever the height.
2. **Over.** Climb-up: move onto `standPoint`, stop. Vault: move across to `exitPoint`, then let go with
   `vaultExitSpeed` forward and `PlayerMovement.CarryMomentum()`; the far-side drop is ordinary gravity
   with ordinary fall damage. Duration = the rest of the action.
3. **Hand back.** `SetClimbing(false)`.

Movement keys are ignored during the climb; look stays free. **Aborts** — death (`PlayerController.IsDead`),
ragdoll/knockdown, teleport (`ITeleportAware`), mounting, `OnDisable`, or no progress for `stallTimeout`
— all go through one `LetGo()`, as the ladder does.

### `LedgeAirGrab` — the mid-air layer (separate, removable)

Its own component beside `LedgeClimber`. Each physics step while **Jump is held**, airborne, and not
tethered / gliding / jetpacking / on a ladder / already climbing, and past the first moment of a jump
(so the press that started a jump does not also grab): call
`LedgeClimber.TryClimbFrom(feet, bodyYaw, airReach)`, accepting only lips above the feet.

**The ledge wins over the double-tap deploy.** The double tap is `PlayerInputManager.OnBodyActivatePressed`,
consumed only by `BodyEquipmentController.OnBodyActivate`. `LedgeAirGrab` exposes `TryGrabNow()` (the
same checks and probe, run immediately rather than on the next physics step); `OnBodyActivate` calls it
first and returns without deploying when it returns true. `BodyEquipmentController` looks the component
up with `GetComponent` and treats a missing one as "no grab", so removing the layer restores today's
double tap exactly.

Disabling or removing `LedgeAirGrab` changes nothing else.

### `GrapplingHookArtifact` changes

- **Arrival** (`dist <= arrivalDistance` while winching): probe with `LedgeProbe` via the player's
  `LedgeClimber` — facing = flat direction to the anchor, falling back to the flattened hit normal when
  the anchor is nearly overhead; reach = `maxReach`.
  - Edge in reach → **hang**: stop winching, lock the rope at its current length, ignore the stall timer
    while hanging, `OfferRopeClimb(facing)`.
  - No edge → today's `ReleaseInto(radial, arrived: true)`, unchanged.
- While the rope holds the owner (swinging or hanging) it keeps `OfferRopeClimb` current, and
  `WithdrawRopeClimb()` in `StopGrapple`.
- The offer carries a callback — `OfferRopeClimb(facing, onClimb)` — that the climber invokes when a
  rope climb starts: release the rope **without** a boost — `AnnounceRelease()` then `StopGrapple()` —
  so peers see the rope vanish as the pull-up starts. A callback rather than an event, so there is no
  subscription to leak when the item is destroyed mid-swing.
- `LedgeClimber` knows nothing about grapples. The lasso and leash do not offer rope climbs.
- Not offered when standing in a ladder volume — the ladder owns Jump there.

## Animation

The user downloads from Mixamo (Humanoid, without skin, in place where offered) into
`Assets/Game/Art/Animations/Humanoid/`:

| Use | Mixamo search | Required |
| --- | --- | --- |
| Pull up onto a ledge | "Braced Hang To Crouch" (or "Freehang Climb") | yes |
| Vault over | "Jump Over" / "Vault" | yes |
| Hanging on the rope below the lip | "Hanging Idle" | optional |

Each becomes a `CharacterAction` (Create Actions From Selected Clips → Rebuild) with a **lip mark** at
the moment the hands reach the edge. `LedgeClimber` holds serialized references to the climb-up and
vault actions and plays them with `CharacterActions.Play` on the owner; `ClientNetworkAnimator`
replicates them. Until the clips exist the climb still runs, with a serialized fallback duration, and
logs a one-time warning naming the missing action.

## Multiplayer

Owner-only motion on an owner-authoritative `NetworkTransform`: peers see the climb as the body moving.
Animation rides the existing network animator. The rope release reuses `AnnounceRelease`. No new
messages, RPCs, `NetworkVariable`s or network prefabs. Verified manually on an actual client by the user.

## Persistence

Nothing new is saved. The climb lasts about a second; a save mid-climb reloads the player at that spot,
where they drop or press Jump again. Hanging on the rope is already covered by the grapple's saved hook
point and rope length; on reload the player dangles and Jump still climbs because the probe runs from
wherever they are.

## Documentation (same change)

- New `docs/AI/systems/LedgeClimbing.md` (Model → Key types → Flows → Multiplayer → Persistence →
  Gotchas → Extending), with `paths:` for the three new scripts.
- `PlayerCharacter.md`: `OnJump` consults the climber; new components on the prefab; `SetClimbing` now
  has a third user.
- `Artifacts.md` (grapple section): arrival hang, rope climb, `ClimbStarted` release.
- `BodyEquipment.md`: the double tap yields to a ledge grab.
- `docs/Human/the-systems.md`: a plain-language entry.
- Regenerate with `python3 tools/docs_check.py --index`.

## Out of scope

Shimmying along ledges, hanging from a ledge without a rope, NPCs climbing ledges, climbing while
crouched, ledge-climb sound effects beyond what the clips' footstep events give.
