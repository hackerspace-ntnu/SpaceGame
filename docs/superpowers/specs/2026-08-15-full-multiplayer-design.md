# Full Multiplayer — Design

**Date:** 2026-08-15
**Goal:** every gameplay system works in a multiplayer session — player location, artifacts, and
actions players take on each other and on the world. Anything not yet networked degrades to
single-player behaviour instead of throwing.

---

## 1. Where the existing implementation stands

Reviewed before deciding. The verdict is **build on it, and replace one layer**.

**Keep — this part is correct and hard-won:**

| Area | State |
|---|---|
| Transport (NGO 2.9.1 + Relay/Lobby) | Complete. `SessionLauncher`, `LobbySession`, direct-connect fallback. |
| Session lifecycle | `NetworkBootstrap`, `NetworkGameManager`, per-client spawn-when-world-ready. |
| Player body | `PlayerCharacterNetworked` + NetworkTransform/Animator, `NetworkPlayerController`. |
| Hotbar inventory | `PlayerInventoryNetwork` — NetworkList-backed, genuinely server-authoritative. |
| World streaming | `WorldStreamer` — server-authoritative chunk scenes through `NetworkSceneManager`. |
| Spawn facade | `IWorldService.Spawn/Despawn` — right authority semantics already. |
| Singleplayer = host | One code path. `Network.IsNetworked` is true even solo. |

**Replace — the per-feature sync layer.** Every feature that got networked did so by growing its own
`NetworkBehaviour` with a hand-written Request→Server→Broadcast RPC triple:
`EquipmentNetworkSync`, `MountNetworkSync`, `WingPackNetworkSync`, `GrappleNetworkSync`. That is
40–175 lines *per feature*, and the features still unnetworked (every artifact, all AI, all
vehicles, all weapons, backpack) would cost thousands more of the same shape. Worse, forgetting one
fails **silently** — it works for the host and does nothing for clients.

**The two real gaps, measured:**

1. **No authority model for non-player entities.** `AgentController.Update()` runs on every machine,
   so each client simulates its own private AI. Damage is applied locally to a local copy.
2. **`NetworkedHealthComponent` is wrong.** `NetworkVariableReadPermission.Owner` means a
   server-owned AI's health reaches nobody, and it applies a *diff* rather than the value, so a
   dropped update desyncs permanently.

Good news that shapes the plan: only **one** production chunk scene (`Chunk_7_5`, 7 agents) has
baked AI. The settlement generator is an editor-time tool. So AI is an architecture problem, not a
content-migration problem.

## 2. Architecture

Four primitives. Everything else is applying them.

### 2.1 `NetArg` — one payload for every message

A fixed `INetworkSerializable` struct: `Target` (NetworkObjectId), `A`/`B` (ints, indices, enums),
`P` (Vector3), `R` (Quaternion). Covers every existing call site. No per-feature payload types, no
allocation, no reflection.

### 2.2 `NetChannel` + `NetRelay` — one generic RPC channel, replacing every bespoke sync class

- **`NetChannel`** (MonoBehaviour, auto-added): the handler table and local dispatch. Always
  present, works offline, dies with its GameObject so handlers cannot leak.
- **`NetRelay`** (NetworkBehaviour, needs a NetworkObject): the wire. Three directions —
  `Server`, `All`, `Others`.

Any MonoBehaviour anywhere under the entity, networked or not, talks through extension methods:

```csharp
void OnEnable()  => this.NetOn(NetMsg.UseItem, HandleUse);   // register
void OnDisable() => this.NetOff(NetMsg.UseItem, HandleUse);

this.NetToServer(NetMsg.UseItem, new NetArg { A = slot }); // client → server
this.NetToOthers(NetMsg.ItemUsed, arg);                    // server → peers
```

Message ids are `ushort` constants in one `NetMsg` class — greppable, collision-checked by a test.

**Graceful degradation is the defining property, not a bolt-on:**

| Situation | Behaviour |
|---|---|
| Offline / no NetworkManager | Handler invoked locally. Identical to today's single-player path. |
| Entity has no `NetworkObject`/`NetRelay` | Handler invoked locally, one-time `LogWarning` naming the object. Never an exception. |
| A handler throws | Caught, logged, dispatch continues to the next handler. |
| Message id with no handler | Ignored. One-time warning. |
| Relay arrives before/after despawn | Dropped silently. |

A feature nobody has networked yet therefore behaves exactly as it does in single-player — locally,
on each machine — instead of erroring. That is the "keep the game going as well as possible" rule,
implemented once, in one place.

### 2.3 `NetAuthority` — the server-simulated entity switch

One component on any AI, creature or vehicle root. On the server and offline it does nothing. On a
remote client it disables the behaviours that *drive* simulation (`AgentController`, `IMovementMotor`
implementations, `NavMeshAgent`) and makes the `Rigidbody` kinematic, leaving visuals and animation
to be driven by `NetworkTransform`/`NetworkAnimator`. Drivers are auto-discovered and cached in a
serialized list so the runtime cost is zero and the choice is inspectable.

Entities *without* it keep today's behaviour: every machine runs its own copy. Degraded, but alive.

### 2.4 `NetDamage` — one entry point for actions on each other

`NetDamage.Apply(target, amount, source)` replaces every direct `health.Damage(...)` call:

- offline or server → apply directly
- client → `NetToServer(NetMsg.Damage, ...)` on the **target's** relay; the server applies it
- target has no relay → apply locally, warn once

The server's `HealthComponent` is then the single truth, replicated by a fixed
`NetworkedHealthComponent` (read permission `Everyone`, applies the absolute value). This makes
player→AI, AI→player and player→player all correct with one change instead of three.

### 2.5 `UseAuthority` — which machine runs an item's effect

`UsableItem` splits one "use" into two jobs that belong on different machines:

- `Use()` — what the use *does*. Runs where the item has authority.
- `Present()` — what it *looks and sounds like*. Runs on every machine, immediately on the
  owner's, so nothing feels like it is waiting for a round trip.

`Authority` picks where `Use()` runs. `Server` by default (spawning, damage, anything shared);
`Owner` for tools whose whole effect is the holder's own body moving — grapple, lasso, leash,
potions — because that body is already owner-authoritative and a round trip inside a swing is felt.
Aim travels in the payload via `OnRequestUse`, since only the holder's machine has their camera.

## 3. Applying the primitives

| System | Change |
|---|---|
| **Artifacts (all of them)** | `EquipmentController.OnUse` relays one `NetMsg.UseItem`. **One change networks every artifact — the eight that exist and every one written after.** |
| **AI agents** | `NetworkObject` + `ClientNetworkTransform` + `NetAuthority` + `NetRelay` on 19 agent prefabs, `NetworkedHealthComponent` where health exists, all registered in `DefaultNetworkPrefabs` (41/41). |
| **Damage / PvP** | Every `.Damage(` call site → `NetDamage.Apply`. |
| **Projectiles / turrets** | Server-spawned via `GameServices.World.Spawn`; impact damage via `NetDamage`. |
| **Mounting** | `MountNetworkSync` rewritten on the relay; no longer a `NetworkBehaviour`. |
| **Wing pack** | `WingPackNetworkSync` **deleted**. A launch is now just a server-authoritative item use, and the pilot never names a prefab — the server reads it off its own copy of the pack, which is safer than the whitelist it replaces. |
| **Equipment visuals** | `EquipmentNetworkSync` **deleted**. `PlayerInventoryNetwork` already replicates the selected slot, so every machine reaches the same equip on its own; the second channel only made non-owners rebuild the item twice. |
| **Late joiners** | `PlayerInventoryNetwork` adopts the hotbar as it already stands on spawn, and `EquipmentController` pulls the current selection in `Start`. Both subscriptions only fired on *change*, so a player who joined mid-session saw everyone else empty-handed. |
| **Health** | `NetworkedHealthComponent` read permission `Owner` → `Everyone` (a server-owned AI has no owning client, so its health reached nobody) and applies the absolute value instead of a diff. |
| **Grapple** | Unchanged. Its `NetworkVariable` anchor is continuous state, not an event, and a `NetworkBehaviour` that exists for the variable gets its RPCs for free. |

## 4. Testing

EditMode tests in `Assets/Game/Editor/Tests/` (no asmdef, so they can see Assembly-CSharp):
`NetMessagingTests` and `NetAuthorityAndDamageTests`. They cover id uniqueness, `NetArg`
round-tripping, local dispatch offline, unknown ids, a throwing handler, nested listeners,
addressed delivery, driver discovery, and damage with no relay.

## 5. Known trade-off

`NetAuthority` switches off an entity's `IMovementMotor` on machines that do not own it. For a
procedurally animated rig the motor both moves the body *and* solves the legs, so a remote copy
slides with still feet. The fix is per-prefab, not structural: take the locomotion component out of
that entity's `simulationDrivers` list and the legs keep solving against the replicated body
position. The component's tooltip says so.

## 6. Non-goals

Client-side prediction and rollback beyond what the grapple already does; dedicated-server builds;
anti-cheat beyond the range/ownership checks already present; persistence changes.
