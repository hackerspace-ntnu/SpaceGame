# Networked Doors and Leash — Design

**Date:** 2026-08-21
**Status:** Approved, not yet implemented

## Context

Two-player sessions revealed a set of gameplay actions that do not replicate. A survey of the
reported list found that most of it is already built:

| System | State |
| --- | --- |
| Damage (`NetDamage` + `NetworkedHealthComponent`) | Already networked. `NetDamage.Apply` is the sole entry point and every damage source routes through it. Only `SandstormVictim` bypasses it. |
| Weapons and projectiles (`Weapon : UsableItem`) | Already networked via the `Use`/`Present` split, `ShotDealsDamage` and `projectile.Cosmetic`. `BallLightningWeapon` is on this pattern. |
| `GrapplingHookArtifact` | Already networked. Its class doc describes the split, and records that an older `GrappleNetworkSync` was deliberately deleted. |
| **Doors (`ArticulatedPartInteraction`)** | **Not networked at all.** |
| **Leash (`LeashArtifact`)** | **On the rails but using the wrong half.** |

This spec covers only the two confirmed gaps. Weapons and damage are handled separately, after the
prerequisite below lands and they can be retested.

Note: the doors on `ShipRV` are driven by `ArticulatedPartInteraction`, **not** by
`DoorInteraction`. `DoorInteraction` exists only in `Marius test scene.unity` and is out of scope.

## Prerequisite: the `NetChannel` re-entrancy crash

`NetChannel.Dispatch` copied the handler list into a single reusable **instance field**
(`dispatchBuffer`) before iterating. That defends against a handler mutating the handler list, but
not against re-entrancy — and dispatch re-enters on the same channel constantly:

1. Client sends `NetMsg.UseItem`; the server's `Dispatch` begins iterating its buffer.
2. `EquipmentController.OnUseRequested` answers with `NetToOthers(NetMsg.ItemUsed, …)`.
3. `SendTo.ClientsAndHost` runs the local end **inline on the host** → `Deliver` → `Dispatch` on the
   same channel → `dispatchBuffer.Clear()` under the outer loop.

Result: `InvalidOperationException: Collection was modified` thrown out of an RPC handler, aborting
the rest of the outer dispatch, on every item use.

**Fix (written, pending verification):** rent a copy buffer from a small static pool per dispatch
and return it in a `finally`. Static and unsynchronised is correct because Netcode delivers on the
main thread only, so live buffer count is exactly the call depth. Buffers are cleared on return so
the pool never holds a handler alive.

Both features below ride the same round trip, so this must land and be verified first or they will
appear broken for reasons unrelated to their own code.

## Goals

- A door opened by one player is open for every player, including players who join later.
- A rope created by one player exists, is visible, and exerts force on every player's machine.

## Non-goals

- Persisting door state into the save file.
- Roping non-networked dynamic props (see the addressing table).
- Any change to the weapons, projectile or damage systems.
- Unicast messaging. `NetTo` has only `Server`/`All`/`Others`, and widening it is out of scope.

## 1. Doors

### Shape

`ArticulatedPartInteraction` stays a plain `MonoBehaviour`. It must keep working on doors with no
`NetworkObject` ancestor — interiors, chunk props — where the messaging layer already degrades to
running locally and saying so once via `NetChannel.WarnUnrelayed`. No new `NetworkBehaviour`: the
per-feature `XNetworkSync` pattern was deliberately retired in favour of `NetRelay`/`NetChannel`.

### Message ids

Appended to `NetMsg`, never reused:

```
public const ushort PartToggle = 60; // owner → server: set this part group open/closed
public const ushort PartState  = 61; // server → all: this is the group's state
```

### Addressing

`NetChannel` is per-entity, and `ShipRV` carries two independent groups (`CockpitDoor`,
`GarageDoor`), so a message addressed to the ship must say which group it means.

- `NetArg.Target` — the entity (resolved by `NetChannel.RootOf`, i.e. the `NetworkObject` root).
- `NetArg.A` — the component's index within
  `GetComponentsInChildren<ArticulatedPartInteraction>(true)` on that root.
- `NetArg.B` — the verb:

  | `B` | Meaning | Direction |
  | --- | --- | --- |
  | `-1` | "what is this group's state?" | client → server |
  | `0` | closed, animate | either |
  | `1` | open, animate | either |
  | `2` | closed, instantly | server → all (late-join answer) |
  | `3` | open, instantly | server → all (late-join answer) |

The index is deterministic because every machine in a session runs the same prefab from the same
build, so child order is identical. It is *not* stable across builds, which is fine: ids only need
to agree within a session.

### Flow

1. `Interact()` computes the group's next state exactly as it does today (mixed states resolve
   toward closed), then sends `PartToggle` with that state rather than applying it.
2. Server handler validates `CanInteract()` — this is what stops a client opening a door that is
   locked while mounted or mid-swing — applies `SetOpen(open)`, then broadcasts `PartState`.
3. Every machine's `PartState` handler applies `SetOpen(open)` and animates.

Offline and on unrelayed doors the send falls through to a local dispatch, so `Interact()` behaves
exactly as it does today.

### Late join

On a client, each `ArticulatedPartInteraction` sends `PartToggle` with `B = -1` once its relay is
available. The server answers with `PartState` carrying `B = 2` or `B = 3`, and the joining client
applies `SetOpen(open, instant: true)`.

The answer is broadcast to `All` rather than to the asking client, because the layer has no unicast.
This is accepted: the message is rare and idempotent.

**Required guard:** `ArticulatedPart.SetOpen` must return early when `target` already matches the
requested state. Without it a late-join broadcast snaps a door that another player is currently
watching swing.

## 2. Leash

### The actual defect

`LeashArtifact` declares `UseAuthority.Owner` and overrides `Use()`. Per the `UsableItem` contract,
`Use()` is the authority-only effect and `Present()` is what runs on every machine — and
`LeashArtifact` never overrides `Present()`. So the `Leash` object is constructed on one machine and
nowhere else.

### Shape

Move onto the same three-part split `GrapplingHookArtifact` already uses:

- `OnRequestUse(ref NetArg)` — owner-only, the one machine with a camera. Performs the raycast that
  `Use()` does today and writes the resolved endpoint into the arg. No peer can recompute an aim.
- `Use()` — server: the authoritative rope. Runs the spring/damper constraint.
- `Present()` — every machine: constructs the `Leash` and its `LineRenderer` so peers see a rope
  rather than objects moving under invisible forces.

The two-stage behaviour (first use attaches hand→object; second use terminates the held rope onto a
second object) is carried in `NetArg.B` as a stage marker, mirroring how the grappling hook carries
what its press meant.

### Endpoint addressing

`NetArg.IdOf` only mints an id for a **spawned `NetworkObject`**. This bounds what can be roped:

| Endpoint kind | Addressed by | Online |
| --- | --- | --- |
| `Static` — wall, terrain | `NetArg.P`, the world hit point, identical on every machine | Yes |
| `Object` with a `NetworkObject` — vehicle, NPC, player | `NetArg.Target` | Yes |
| `Object` without one — a per-machine physics prop | nothing available | No; degrades to local-only |

The third row is deliberate. Such a prop's physics already diverges per machine, so a rope to it
could not be made consistent anyway. The existing `WarnUnrelayed` notice covers it.

### Authority

The server runs the constraint and owns the rope. The exception is a player endpoint: the server
must not push a player's body, because the player's `NetworkTransform` is owner-authoritative
(`AuthorityMode: 1`) and any server-side push is overwritten by that owner's next state update —
the same failure documented for server-side teleports.

So `Leash` splits force application in two:

- **Forces the server owns** — every non-player endpoint. Applied directly in `FixedUpdate`.
- **Forces a player owns** — sent to that player's own machine, which applies them to its own body.

**Rate:** this must not be a per-`FixedUpdate` message. The tug is accumulated on the server and
sent as a low-rate impulse (target: 10 Hz, tuned during implementation), carried in `NetArg.P` as a
velocity delta. A per-step force would flood the channel at 50 messages per second per rope.

This is the only genuinely new machinery in the spec and the highest-risk part of it.

## Error handling

- Every handler already runs inside `NetChannel.Dispatch`'s try/catch, so one throwing handler does
  not stop the rest of a message.
- A `PartToggle` naming an index that no longer resolves is dropped, not clamped: clamping would
  toggle the wrong door.
- A rope whose `Target` cannot be resolved on a peer is not built on that peer. It still exists on
  the server, which keeps the physics correct; the peer is missing a visual, which is the lesser
  failure.
- `Leash` already self-destroys when an endpoint disappears or the rope snaps. Server-side disposal
  broadcasts so peers drop their copies.

## Testing

- **`NetChannel` re-entrancy** — a test that registers a handler which dispatches again on the same
  channel and asserts the outer dispatch completes. This is the regression that must not return.
- **Door index resolution** — asserts the index derived on two independently built copies of the
  same prefab agrees.
- **`SetOpen` idempotence** — asserts a repeated `SetOpen(open)` does not restart an in-flight
  animation.
- **Leash addressing** — asserts a static endpoint round-trips through `P` and a networked endpoint
  through `Target`.
- Manual two-client verification for each feature, since neither can be fully covered headlessly.

Note: `Assets/Game/Tests/EditMode` cannot reference `Assembly-CSharp`, which is where all of this
lives. Tests that need these types belong in an `Editor/` folder, or must be driven the way the
`SpawnClearance` fix was verified — through the editor directly.

## Risks

1. **The player-tug impulse is the unknown.** Rate and magnitude both need tuning against real
   latency, and a rope that feels good on a host may feel elastic to a client. If it does not come
   together, the fallback is the simplification already discussed: players anchor ropes but are not
   pulled by them.
2. **The door index is positional.** Reordering children of a prefab between builds silently
   re-points saved-nothing but live messages. Acceptable within a session; worth a comment at the
   derivation site so the constraint is not discovered the hard way.
3. **Verification is slow.** Both features need two live clients to confirm, and the editor's
   assembly reload has already produced one false "the fix does not work" in this workstream.
   Always gate a behavioural check on `Assembly-CSharp.dll` being newer than the changed source.
