---
system: Hogtie
layer: items
summary: "Rope round a body already down: a 120 s pool on the body, struggled out in ~45, cut by anyone but the captive"
paths:
  - Assets/Game/Scripts/Items/Artifacts/Leash/Hogtie.cs
  - Assets/Game/Scripts/Items/Artifacts/Leash/HogtieSettings.cs
  - Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleReader.cs
  - Assets/Game/Editor/Tests/HogtieTests.cs
symptoms:
  - "clicking a downed player with the leash ropes them instead of tying them up"
  - "the leash refuses to tie anybody, or ties people who are still standing"
  - "a tied player stands up the moment the net on them rots"
  - "a hogtie ends in about eleven seconds however still I lie"
  - "a hogtie lasts the full two minutes however hard I struggle"
  - "tying somebody up freezes them on my screen and they walk around on theirs"
  - "the leash is consumed by a tie and the rope never comes back"
  - "a leash gauntlet ties an unlimited number of people and costs nothing"
  - "a rope pickup appears at a tied body every time a tie ends, out of nothing"
  - "a tied player who dies stays roped and the leash is gone for good"
  - "I respawned still hogtied"
  - "a player loaded from a save cannot move and nothing in the log says why"
  - "a hogtied player cuts their own ropes off by clicking their own body"
reads_with: [LeashSystem, Artifacts, Combat, Multiplayer, BodyEquipment, PlayerCharacter]
updated: 2026-09-09
---

# Hogtie

The leash's fifth verb: rope round somebody who is **already on the ground**, so they cannot get back up.
**Scope:** [Hogtie.cs](Assets/Game/Scripts/Items/Artifacts/Leash/Hogtie.cs), [HogtieSettings.cs](Assets/Game/Scripts/Items/Artifacts/Leash/HogtieSettings.cs), the `Tie` verb in [LeashArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs), [SnareStruggleReader.cs](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleReader.cs), [HogtieTests.cs](Assets/Game/Editor/Tests/HogtieTests.cs).
**Related:** [LeashSystem.md](docs/AI/systems/LeashSystem.md) (the rope this is a verb on) · [Artifacts.md](docs/AI/systems/Artifacts.md) (the net gun, whose pool and meter this reuses) · [Combat.md](docs/AI/systems/Combat.md) (the ragdoll claim set) · [BodyEquipment.md](docs/AI/systems/BodyEquipment.md) (why a worn leash is not consumed).

## Model

- **A follow-up, never an opener.** `Hogtie.CanTie` refuses anybody on their feet, so something else — a net, a repulsor blast — has to put them down first. A tie therefore costs the tier a second action and gives the target a window in which the first one can be answered.
- **Deliberately broader than "netted".** The gate is the ragdoll adapters' `IsHeldOrDown` (`IsHeld || rig.IsLimp`), so a player merely knocked flat is as tieable as a netted one. Refusing the blast case would make the two feel like unrelated systems.
- **One rope, one body, one pool** — unlike a net, whose `SnareIntegrity` pool is *shared* between every captive under it so that a wide shot trades duration for coverage. A tie cannot be wide, so that trade does not apply and the pool lives on the body.
- **The machinery is the net's; only the depth differs.** Same [`SnareIntegrity`](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareIntegrity.cs), same [`SnareStruggleMeter`](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleMeter.cs), same [`SnareStruggleReader`](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleReader.cs) (extracted from `SnaredBody` so a tie and a net cannot disagree about what a struggle is). **120 s against the net's 30** is the whole of "far slower than a net" — see Gotchas.
- **Three ways out, one exit.** The 120 s ceiling, struggling out in ~45 s, and **somebody else** clicking the ropes empty-handed. Death is a fourth. All four funnel through `Hogtie.Untie`.
- **The cut is a bystander's verb, never the captive's.** `LeashArtifact` refuses a `Tie` untie whose subject is the clicker's own body, in the aim *and* again in `Present` on every machine — the same rule and the same shape as `Leash.Restrains` on an ordinary rope. Reachable because a tied body is a ragdoll whose camera can end up looking along its own limbs, and a click that landed would retire the 45 s struggle this whole system is built around.
- **The tie and the net compose with no bookkeeping.** Both ragdoll adapters hold a **set** of claims, so a body stays limp until the last holder lets go: a net rotting off a tied player leaves them tied, and cutting the ropes off a netted player leaves them netted.
- **A tie is meant to consume the leash and give the rope back at the body — and today only a HAND-slot leash does.** `Leash.asset` is `equipKind: 1`, a **gauntlet**, and `BodyEquipmentController.OnWornDepleted` refuses to consume a worn item ("a consumable gauntlet would need a server-side removal the body does not have yet"); `BodyEquipmentNetwork` has no primitive that empties a body slot. `LeashArtifact.Consumable` (`!Worn`) therefore gates **both halves together** — gating one alone mints a rope every two minutes from an item that was never taken.

## Key types

| Type | File | Role |
|---|---|---|
| `Hogtie` | [Hogtie.cs](Assets/Game/Scripts/Items/Artifacts/Leash/Hogtie.cs) | One tied body, `AddComponent`ed on demand and never authored. `CanTie`/`BodyOf`/`Ensure`, `Bind`/`Untie`/`Step`, `IsBound`/`HoldFraction`/`StruggleLevel`. Owns the pool, both meters and the two messages |
| `HogtieSettings` | [HogtieSettings.cs](Assets/Game/Scripts/Items/Artifacts/Leash/HogtieSettings.cs) | `[Serializable]` tuning on the leash prefab: `HoldSeconds` 120, `StruggleMultiplier` 1.96, plus the meter's cap/decay/deadzone/angle |
| `SnareStruggleReader` | [SnareStruggleReader.cs](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleReader.cs) | The captive's own `InputControls`, the menu gate (`MayRead`) and the reversal memory (`Counts`). Shared with [`SnaredBody`](Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs) |
| `PlayerRagdoll` / `AgentRagdoll` | [Ragdoll/](Assets/Game/Scripts/Gameplay/Ragdoll) | `HoldDown(object)` / `ReleaseHold(object)` and `IsHeldOrDown`. A `HashSet<object>` of claims; death clears all of them |

## Flows

1. **Aim** (`LeashArtifact.OnRequestUse`, owner only). `TryAimAtDownedBody` resolves the hit through **`Hogtie.BodyOf`, not `GetComponentInParent<Rigidbody>()`** — a tie only ever lands on a limp body, and a limp body is a built ragdoll whose every bone carries its own `Rigidbody` and `BoxCollider`, so the nearest one to the ray is the target's *forearm*. Encodes `Tie` (verb 4) with the body in `NetArg.Target`.
2. **Order matters.** rope search → **tied-body** search (so a rope knotted to a tied player is still untieable) → terrain refusal → **downed-body tie** → layer mask → self-check → ordinary anchor. Above `leashableLayers` because that mask is documented as excluding the player layer (it is what stops you roping yourself) and a downed player's bones are on exactly that layer; above the anchor path because a body is a perfectly good rope anchor and would otherwise swallow the click.
3. **Present** (every machine) → `LeashArtifact.TieUp` → `Hogtie.Ensure(body).Bind(settings, rope, authority)`. `Bind` claims the ragdoll with `HoldDown(this)` and re-checks `CanTie` on each machine.
4. **`Use()`** runs one call later, on the **owner only** (`UseAuthority.Owner`, and `UseChannel.Press` presents before it runs the use). It calls `Deplete()` if — and only if — that Present landed a tie and the instance is not worn.
5. **Struggle.** `Update` polls the tied player's own keys and hands them to `Step`, which offers them to the send meter; an accepted press becomes one `NetMsg.HogtieStruggled`. The authority pushes its own meter from that and drains the pool by `1 + multiplier × level` per second.
6. **End.** `Untie` gives back the claim, drops the rope on the deciding machine, and broadcasts `NetMsg.HogtieUntied`.

## Multiplayer

- **The split is `SnareCatch`'s.** *Every* machine applies the hold — a peer that skipped it would watch a tied player stand up and walk about — and *one* runs the pool: `Network.Simulates` asked of the **body**, never of the item. An equipped item is instantiated into a hand and never spawned, so its own `NetworkObject` is dormant and every peer in the session would answer yes and drain a pool of its own.
- **There is deliberately no "a tie happened" id.** The target travels in the ordinary `UseItem`/`ItemUsed` pair, and every machine re-checks `CanTie` against ragdoll state that has already replicated — so a fabricated `Tie` verb aimed at somebody standing is refused everywhere, the server included. An announcement would carry nothing the item's own message did not.
- Two ids mirroring the net's pair: [`NetMsg.HogtieStruggled`](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs) (104) tied body's owner → server, `HogtieUntied` (105) server → everyone. Both ride the **tied body's own** relay, unlike the net's report which has to cross to the shooter's: the leash instance is destroyed the moment the tie lands, so nothing is left there to listen. Neither carries a payload — the channel already names the subject.
- **The struggle report carries no magnitude**, because a level computed on the client is the escape the client chose (GDC-L1-MP-0004). `Network.MayActFor(gameObject, sender)` is what stops one player reporting struggles on another's behalf: `NetRelay`'s server RPC is `InvokePermission.Everyone`, so anybody may *send* on any relay.
- **`Untie` is a single funnel and is idempotent.** The authority's own `NetToAll` re-enters it on the host, which the `if (!bound) return` guard absorbs, so the rope is dropped exactly once.
- **Host-based only.** The server learns about a third party's untie through its own `Present`, which reaches it because `NetTo.Others` is `SendTo.ClientsAndHost`. This project never calls `StartServer`, so that is sound; a dedicated server would hold its copy until the ceiling and return the rope late.

## Persistence

**Nothing about a tie is persisted, deliberately.** `Hogtie` implements no `ISaveable` and is never on a prefab, so `SaveableEntity.CollectSavers` — a `GetComponents<ISaveable>()` walk — cannot see it, and `HogtieTests.ATieIsNeverPersisted` pins both halves of that. The failure it avoids is the worst kind this project has: a quit-time autosave that captured a tie reloads a world in which a player cannot move, with nothing in the log to say why (the same shape as the kinematic-save-freeze). Untying by loading is a far better failure.

Nothing leaks in by another door either: `RigidbodySaveable` writes motion and explicitly **not** `isKinematic`, and `PlayerSaveService.EnsureMomentumSaver` skips a kinematic body outright — so being limp at save time is not recorded. The consumed leash is already out of the hotbar, which *is* saved, so a reload cannot duplicate the rope.

## Gotchas

- **The struggle multiplier cannot be derived assuming a saturated meter reads 1.** `SnareStruggleMeter.Push` clamps with `Mathf.Min(1, …)`, so at the 2.5 Hz cap the level is a sawtooth from 1 down to `e^(-1/3) = 0.7165`, whose time-average is `1.2·(1-e^(-1/3))/0.4 = 0.8504`. Solving `120/(1 + m·0.8504) = 45` gives **1.96**; assuming 1 gives 1.667 and a real escape of 50.4 s — ten per cent long, silently. The same arithmetic explains the net: its multiplier of 2 is documented as "about 10 s" and actually measures 11.45 s.
- **The tie is slow because its pool is deep, not because its multiplier is small.** At the net's own multiplier of 2 a 120 s tie already breaks in 44.6 s. 1.96 against 2.00 is a rounding difference — retune `HoldSeconds`, never the multiplier.
- **`Deplete()`, never `maxUses`.** The leash is authored `maxUses: -1` and must stay unlimited, or every click that missed, dropped a rope or untied one would consume it as readily as the one that tied somebody up.
- **`Use()` cannot re-derive whether the tie took.** It reads a flag set by the `Present` one call earlier on the same machine. Asking the target whether it is tied would spend a rope for somebody else's tie that landed between the aim and the press.
- **A respawn unties too, and it is not redundant.** Death already unties through `Hogtie`'s own `OnDeath` hook, so the respawn call in [`RespawnRelease.Everything`](Assets/Game/Scripts/Gameplay/Game/Spawning/RespawnRelease.cs) is a null check on the ordinary path. It is there for the tie that outlives a death that never happened — a body revived by anything other than dying first — and `Untie` is idempotent by its first line, so the two cannot fight.
- **A tied body that dies is untied at once.** The ragdoll adapters already drop every claim on death — that is what stops a permanently un-evictable `RagdollBudget` slot — but nothing there knows about the ropes, so without `Hogtie`'s own `OnDeath` hook the pool drains a corpse for two minutes and the rope never comes back.
- **A knockdown timer cannot expire out from under a tie.** `PlayerRagdoll.Update`'s `if (IsHeld) return;` sits *above* the `downUntil` check, and `ReleaseHold` clears `downUntil`, so a body tied while knocked flat stays down and recovers on the next settled frame after the ropes come off.
- **`OnDisable` releases locally and says nothing.** A broadcast has no relay left to leave from and a rope spawned at a departing object lands in a chunk nobody is loading — so a body destroyed while tied loses its rope. The same trade `SnareReceiver.OnDisable` documents.
- **A tie on a worn leash is free** — see the last Model bullet. One gauntlet ties an unlimited number of people at no cost. A stated balance hole, not an oversight; it closes the moment the body can empty a slot, or the leash moves to `equipKind: 0`.
- **A tie has no rope visual yet**, so a tied body and a netted one read the same on screen, and the untie gesture clicks the *body* rather than a rope.
- **A captive clicking their own body gets silence**, because the aim composes no verb at all — the same nothing a refused untie on an ordinary rope gives. See LeashSystem's Gotchas.
- **A tied creature does not struggle at all.** A creature's fight is its mass (`SnareTether.Mass`) and a tie does not weigh anything, so any creature is held for the flat 120 s. Harmless today because a tie already requires the creature to be down, but it means a tie is not a smaller net for a big animal.

## Extending

- **Retuning:** move `HoldSeconds` on the leash prefab's `tie` block. If you move `StruggleMultiplier`, redo the arithmetic in the first Gotcha — the naive form is 10% out — and move `HogtieTests.StruggledEscape` with it.
- **A new restraint** that holds a body: take a claim with `HoldDown(yourself)`, give it back with `ReleaseHold(yourself)`, and never reach for the limpness directly. Reuse `SnareIntegrity` + `SnareStruggleMeter` + `SnareStruggleReader` rather than a timer, so the struggle behaves the same everywhere.
- **Making the tie cost something:** the missing piece is a server-side "empty this body slot" on `BodyEquipmentNetwork`, plus `BodyEquipmentController.OnWornDepleted` calling it instead of warning. `LeashArtifact.Consumable` then becomes `true` and both halves light up together.
- **A rope visual:** the tie's own `Bind` is the hook. `LeashRope` draws a ribbon between two points and `SnareMesh` winds one along a whole lattice; a wrap round the hips would want the latter's winding with a handful of nodes taken off the ragdoll bones.
