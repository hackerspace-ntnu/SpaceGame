---
system: Artifacts
layer: items
summary: "Player-held usable items - gadgets, spells, scanners, tools - firing on Use, split Use() vs Present()"
paths:
  - Assets/Game/Scripts/Items/Artifacts
  - Assets/Game/Scripts/Items/Core
  - Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs
  - Assets/Game/Prefabs/Items/Artifacts
  - Assets/Game/Scripts/Gear/JumpingRod
symptoms:
  - "the gadget works for the host but does nothing for a client"
  - "every client's shot follows the host's crosshair instead of their own aim"
  - "the beam never stops burning after the button is released"
  - "the item is removed from the inventory the first time it runs out of charges"
  - "the projectile or rope is invisible to everyone except the shooter"
  - "the character stands in the idle pose instead of the hold pose, or glides while walking"
  - "the effect applies on the server and is silently overwritten a tick later"
  - "an item that moves the player does nothing at all while I am riding something"
  - "a moving part of a held item snaps out of place the moment the item is switched on"
  - "the range rings on the scanner display are ellipses instead of circles"
reads_with: [Inventory, LeashSystem, Portals, Persistence]
updated: 2026-09-03
---

# Artifacts

Player-held usable items — every gadget, spell, scanner, throwable and hand tool that occupies a hotbar slot and fires on the shared `Player/Use` action, or is worn on the body and fires on its own key ([BodyEquipment.md](BodyEquipment.md)).

**Scope:** [Assets/Game/Scripts/Items/Core/](Assets/Game/Scripts/Items/Core/), [Assets/Game/Scripts/Items/Artifacts/](Assets/Game/Scripts/Items/Artifacts/), [Assets/Game/Scripts/Gear/JumpingRod/](Assets/Game/Scripts/Gear/JumpingRod/), [EquipmentController.cs](Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs)
**Related:** [Inventory.md](Inventory.md) · [LeashSystem.md](LeashSystem.md) · [Portals.md](Portals.md) · [WeaponSystem.md](WeaponSystem.md) · [Persistence.md](Persistence.md) · skill [.claude/skills/spacegame-artifact/SKILL.md](.claude/skills/spacegame-artifact/SKILL.md)

## Model

- **One use splits in two.** `Use()` runs only where the item has authority; `Present()` runs on **every** machine, and on the owner's immediately so nothing waits for a round trip. Both live on [`UsableItem`](Assets/Game/Scripts/Items/Core/UsableItem.cs), so every artifact is networked by default and none carries a sync component.
- **`UseAuthority`** picks the machine for `Use()`: `Server` (default — spawns, damage, contested resources) or `Owner` (the effect *is* the holder's own body; their transform is already owner-authoritative).
- **Aim never travels as a recomputation.** `OnRequestUse(ref NetArg)` runs owner-side only — the one machine with a live camera — and the payload comes back to every machine as `UseArg`. Fields: `A` = hotbar slot (**reserved**, the server's stale-slot guard), `B` = int/seed/flags, `P` = Vector3, `R` = Quaternion.
- **Hold streams.** `IsContinuous => true` adds `OnRequestHold` / `Hold` / `PresentHold` on the same three machines, streamed at **15 Hz** (`EquipmentController.HoldSendInterval`). A continuous item still gets the ordinary press first — that press plays the sound and bills `maxUses`.
- **`WantsHold`** keeps the stream running after the button is up, for self-timed bursts (laser staff burns 3 s regardless). `EquipmentController` tracks `useButtonDown` and `useHeld` separately for exactly this.
- **Charges** are `maxUses` on `UsableItem`; depletion raises `OnItemDepleted` → `EquipmentController.ItemDepleted` removes the slot. `RefundUse()` gives one back (net gun); a refilling item **must** also override `OnMaxUsesReached()` to stay silent.
- **Hold pose** is auto-added: `OnEquipped` adds a `HoldAnimator` when absent unless `UsesHoldPose => false` (worn, not gripped) — or unless the instance is `Worn` (a gauntlet on the forearm, the pack on the back), which is set by `BodyEquipmentController` before `OnEquipped`.
- **Seven artifacts are gauntlets** (`InventoryItem.equipKind = Gauntlet`): Grappling Hook, Sucker Puncher, Leash, Item Scanner, Ruin Scanner, Repulsor Gauntlet, Flashlight Gauntlet. They are worn on a forearm and fired on Q (left) / E (right), never held; the Wing Pack is the one `Back` item, deployed on a double tap of Space. Same `Use`/`Present` split, same messages — see [BodyEquipment.md](BodyEquipment.md).
- **Every equipped object is a fresh `Instantiate` of the item prefab, destroyed on unequip** — so per-instance state lives in the hotbar slot's [`ItemState`](Assets/Game/Scripts/Items/Inventory/Core/ItemState.cs) bag, not on the instance.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `UsableItem` | [Items/Core/UsableItem.cs](Assets/Game/Scripts/Items/Core/UsableItem.cs) | Base. `Use`/`Present`, `Hold`/`PresentHold`, `Authority`, `maxUses`, `useSoundId`, equip hooks, `OwnerIsLocal()` |
| `UseAuthority` | same file | `Server` \| `Owner` |
| `ToolItem` | [Items/Core/ToolItem.cs](Assets/Game/Scripts/Items/Core/ToolItem.cs) | Aimed/instant/world-changing. Adds `aimProvider` (resolved on demand so it works inside `OnRequestUse`) |
| `EffectItem` | [Items/Core/EffectItem.cs](Assets/Game/Scripts/Items/Core/EffectItem.cs) | Timed change to the holder's own body. `Use()` is **sealed empty**; override `ApplyEffect()` (owner-gated) and `PresentEffect()` |
| `EquipmentController` | [Inventory/Components/EquipmentController.cs](Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs) | Triggers the hand's artifact. Hand sockets, grip frame, and the hand's `UseChannel` — the request/present/authority/broadcast pipeline plus hold stream, shared with `BodyEquipmentController`'s three worn channels |
| `NetArg` | [Core/Multiplayer/Messaging/NetArg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetArg.cs) | `A`, `B`, `P`, `R`, `.With(go)` |
| `ItemState` | [Inventory/Core/ItemState.cs](Assets/Game/Scripts/Items/Inventory/Core/ItemState.cs) | String bag per slot. `IItemStateCarrier` (capture/restore), `IItemDeferredRestore` (referent not yet in the world) |
| `ItemGrip` | [Items/Equipped/ItemGrip.cs](Assets/Game/Scripts/Items/Equipped/ItemGrip.cs) | Which hand, hold size, offsets, pack size |
| `Effect` / `EffectManager` / `EffectCatalog` | [Artifacts/Effects/](Assets/Game/Scripts/Items/Artifacts/Effects/) | Timed body effects, keyed by item type (a second potion replaces the first); catalog rebuilds them after a load from a registered token |
| `InventoryItem` | [Items/Core/InventoryItem.cs](Assets/Game/Scripts/Items/Core/InventoryItem.cs) | The SO in `Assets/Game/Resources/Items/Artifacts/` — name, icon, `itemPrefab` |

## Catalogue

| Artifact | File | What it does |
| --- | --- | --- |
| Anti-gravity potion | [Gadgets/AntiGravityPotion.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/AntiGravityPotion.cs) | `EffectItem`. Drink and float ~5 s. Server consumes the bottle; drinker's machine kills their gravity |
| Lightning spell | [Gadgets/LightningSpell.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/LightningSpell.cs) | Simplest aimed use — bolt strikes the point the caster's `OnRequestUse` put in `P` |
| Dragon bazooka | [Gadgets/DragonBazookaArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/DragonBazookaArtifact.cs) | Server. Fires a corkscrewing firework rocket that bursts into whelps. One owner seed drives the whole flight ([DragonRocket.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/DragonRocket.cs), [DragonRocketFlight.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/DragonRocketFlight.cs)) — rockets are **local-only**, never network prefabs |
| Gravel blaster | [Gadgets/GravelBlasterArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GravelBlasterArtifact.cs) | Server. Pipe shotgun; one seed in `B` decides pellet spread **and** the 1-in-10 backfire ([GravelBlastMath.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GravelBlastMath.cs), [GravelShotTrace.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GravelShotTrace.cs), [GravelBlastFx.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GravelBlastFx.cs)) |
| Repulsor gauntlet | [Gadgets/RepulsorGauntletArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs) | Server. Instant thundergun cone that ragdolls everything in it ([RepulsorBlast.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlast.cs) math, [Cone](Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlastCone.cs)/[Ring](Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlastRing.cs) cosmetics) |
| Sucker puncher | [Gadgets/SuckerPuncherArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/SuckerPuncherArtifact.cs) | Server. Steam ram: heavy damage + launch on the thing hit, shockwave shove within `shockRadius` |
| Laser staff | [Gadgets/LaserStaffArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/LaserStaffArtifact.cs) | Server, `IsContinuous`, `WantsHold => _lit`. 3 s arc, 10 s recharge; `holdTimeout` kills a stranded beam. The reference continuous item |
| Grappling hook | [Gadgets/GrapplingHookArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs) | **Owner**, `IsContinuous`, `IItemDeferredRestore`. Dart → taut rope → swing/winch ([GrappleRope.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/GrappleRope.cs) is drawing only). Fired while riding, it **tows the vehicle** instead — see [Ornithopter.md](Ornithopter.md) |
| Lasso | [Gadgets/LassoArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs) | **Owner**, `IsContinuous`, `IItemDeferredRestore`. Hold to twirl, release to throw; Verlet rope. Support: [LassoLoop](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoLoop.cs), [LassoRope](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoRope.cs), [LassoTether](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs), [LassoStruggle](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoStruggle.cs), [LassoedBody](Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoedBody.cs) |
| Leash | [Leash/LeashArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs) | **Owner**. Tie a rope between any two things — see [LeashSystem.md](LeashSystem.md) |
| Net gun | [NetGun/NetGunArtifact.cs](Assets/Game/Scripts/Items/Artifacts/NetGun/NetGunArtifact.cs) | Muzzle in `P`, aim in `R`, seed in `B`; every machine draws the same closed-form flight. Recharges via `RefundUse()` + silenced `OnMaxUsesReached`. Support: [SnareCatch](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCatch.cs), [SnareLattice](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareLattice.cs), [SnareMesh](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareMesh.cs), [SnareTether](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareTether.cs), [SnaredBody](Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs), [SnareReceiver](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareReceiver.cs), [SnareIntegrity](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareIntegrity.cs), [SnareDrape](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareDrape.cs), [SnareStruggle](Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggle.cs), [NetGunFlight](Assets/Game/Scripts/Items/Artifacts/NetGun/NetGunFlight.cs) |
| Rocket turret | [Gadgets/RocketTurretArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/RocketTurretArtifact.cs) | Server. Spawns a rocket-launcher turret in front of the player, ground-snapped. Deployable → **must** be a registered network prefab |
| Item scanner | [ItemScanner/ItemScannerArtifact.cs](Assets/Game/Scripts/Items/Artifacts/ItemScanner/ItemScannerArtifact.cs) | **Owner**, all-cosmetic. Use toggles power; finds loose salvage in 50 m from a registry, not an OverlapSphere. [ScanBeacon](Assets/Game/Scripts/Items/Artifacts/ItemScanner/ScanBeacon.cs) registers, [ScanTarget](Assets/Game/Scripts/Items/Artifacts/ItemScanner/ScanTarget.cs) types it, [ItemScannerScreen](Assets/Game/Scripts/Items/Artifacts/ItemScanner/ItemScannerScreen.cs) draws it |
| Flashlight gauntlet | [Gadgets/FlashlightGauntletArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs) | Switches the [`Flashlight`](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs) nested on its own `Emitter` — the game's only torch, and it points where the arm points. Sends nothing: `PlayerViewNetwork.netTorch` already replicates it. See [Flashlight.md](Flashlight.md) |
| Ruin scanner | [RuinScanner/RuinScannerArtifact.cs](Assets/Game/Scripts/Items/Artifacts/RuinScanner/RuinScannerArtifact.cs) | Top-down cone of light; every [`IRuinSecret`](Assets/Game/Scripts/Items/Artifacts/RuinScanner/IRuinSecret.cs) the cone's rays hit is told to `Reveal()`. [RuinSecret](Assets/Game/Scripts/Items/Artifacts/RuinScanner/RuinSecret.cs) is the drop-anywhere marker, [RuinScannerPulse](Assets/Game/Scripts/Items/Artifacts/RuinScanner/RuinScannerPulse.cs) the cone mesh |
| Jumping rod | [JumpingRod/JumpingRodItem.cs](Assets/Game/Scripts/Items/Artifacts/JumpingRod/JumpingRodItem.cs) + [.Bounce.cs](Assets/Game/Scripts/Items/Artifacts/JumpingRod/JumpingRodItem.Bounce.cs) | **Owner**, raw `UsableItem`. Pogo stick: plant it and bounce; deliberately not a mount. Hop math in [Gear/JumpingRod/](Assets/Game/Scripts/Gear/JumpingRod/) (`JumpingRodConfig`, `JumpingRodHopModel`, `JumpingRodSpring`) |
| Ship part | [ShipParts/ShipPartItem.cs](Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartItem.cs) | Server. Carry a hull module and fit it into its socket; [ShipPartHighlighter](Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartHighlighter.cs) paints candidate holes locally |
| Portal spray can | [Portals/PortalGunItem.cs](Assets/Game/Scripts/Portals/PortalGunItem.cs) | **Owner**, hold stream. Sprays paint that *is* the aperture; state packed in the upper bits of `B`. Portal system: [Portals.md](Portals.md) |
| Wing pack | [Items/Equipped/WingPackItem.cs](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs) | Worn, not gripped (`UsesHoldPose => false`), `IItemDeferredRestore`. Outside `Artifacts/` but the same base |

## Flows

**Press**
1. `PlayerInputManager.OnUsePressed` → `EquipmentController.OnUse()` → the hand `UseChannel.Press()` (Q/E/double-Space reach a worn channel the same way).
2. Owner builds `NetArg { A = selected slot }`, calls `usable.OnRequestUse(ref arg)`.
3. Owner calls `PlayUse` **immediately** (sound + `Present()`), before any hop.
4. If `Authority == Owner`, owner also calls `TryUse` now.
5. `NetToServer(NetMsg.UseItem, arg)`.
6. Server (`OnUseRequested`): `Network.Simulates(this)` gate → stale-slot guard (`arg.A != SelectedSlotIndex` ⇒ drop) → `TryUse` if `Authority == Server` → `NetToOthers(NetMsg.ItemUsed, except: sender)`.
7. Peers (`OnItemUsedElsewhere`): `PlayUse` only.

**Hold** (only when `IsContinuous`)
1. The press sets `useHeld`/`useButtonDown`; the first tick goes out on the next `Update`, not from `OnUse` — one code path for start and sustain.
2. Every 1/15 s: `arg { A = slot, B = active }` → `OnRequestHold` → local `PlayHold` → owner `TryHold` if owner-authoritative → `NetToServer(NetMsg.UseItemHold)` — but the send is skipped when the arg is identical to the last one sent, except as a keepalive every `UseChannel.HoldKeepAliveInterval` (0.2 s). An artifact that times a hold out (`holdTimeout`) must keep that timeout above the keepalive; 0.5 s is the tightest shipped.
3. Server mirrors the press path with `NetMsg.ItemUseHeld`; peers get `PlayHold` only.

**Release**
1. `OnUseRelease` clears `useButtonDown`. If the item `WantsHold`, the stream continues; `Update` ends it when `WantsHold` goes false.
2. `EndHold(send: true)` sends a final tick with `active == false` — **every override must treat that as "stop"**, including when it never saw the preceding ticks.
3. `Unequip()` calls `WriteBackHeldItemState()` then `EndHold(send: true)`; `OnDisable` calls `EndHold(send: false)` and relies on the item's own `holdTimeout`.

## Multiplayer

- **Authority split:** `Server` for shared world state (spawn, damage, consumption); `Owner` when the whole effect is the holder's own body — their transform is owner-authoritative, so a server-applied force is overwritten within a tick, silently.
- **Messages:** `NetMsg.UseItem` / `ItemUsed` / `UseItemHold` / `ItemUseHeld`, all handled in `EquipmentController`. Artifacts add none of their own except where a subsystem needs a second channel (net gun's `SnareReceiver`).
- **Network prefab list is [Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset)** — the copy at `Assets/DefaultNetworkPrefabs.asset` is stale and unused. Register: **item prefabs** (dropping routes through `PlayerDropService` → `GameServices.World.Spawn`) and **deployables**. Never register: projectiles, flying arcs, equipped visuals, ropes, nets — those are plain local `Instantiate` from `Present()`.
- **Seeded determinism** is the standard pattern for anything erratic that every machine must draw identically: the owner rolls one seed into `NetArg.B`, and pure static math derives the rest (`GravelBlastMath`, `DragonRocketFlight`, `NetGunFlight`). The authority bills the same trace the peers draw.
- **`Network.Simulates(this)` is true everywhere for a held item** — its NetworkObject is dormant (never spawned). Ask the *owner*: `OwnerIsLocal()` on `UsableItem`, `Network.Owns(owner.transform)` in `EffectItem`.
- Verify on an actual client. Single-player runs as a host of one, so an unregistered prefab or a misplaced effect is invisible solo.

## Persistence

- Per-instance state lives in the slot's `ItemState` string bag, written by `CaptureItemState` / `RestoreItemState` (`IItemStateCarrier`). Always call `base`.
- **Charges** are on the base class under key `"uses"` — never rename. Only written when `maxUses >= 0 && currentUses > 0`, so unlimited items add nothing to the save.
- `EquipmentController.WriteBackHeldItemState()` runs on unequip *and* on save; `ReapplyHeldItemState()` is the second pass after a load, because restoring the hotbar equips the selected item before the per-slot bags are back.
- `RestoreItemState` runs **after** `OnEquipped`, deliberately — several items reset themselves there.
- Items whose state names something else in the world (lassoed creature, grapple anchor) implement `IItemDeferredRestore`; `PlayerInventorySaveable` re-asks `TryCompleteRestore()` per player bind and per late chunk, so it must be idempotent.
- `EffectItem` durations survive via `EffectCatalog`: each item registers a token → factory from its own `[RuntimeInitializeOnLoadMethod]`. The token is written to saves — permanent.

## Gotchas

- **`NetArg.A` is reserved** for the slot code (`UseSlotCode`: bare hotbar index, or `256 + BodySlot` for worn gear) on presses *and* hold ticks; the server reads it as its stale-slot guard. An item that stores its own flags there is silently refused on the server for every slot but the matching one. Pack item flags into the **upper bits of `B`** (`EquipmentController` owns `B`'s low bit on hold ticks as the active flag).
- **An item that moves the player by writing their Rigidbody does nothing while they are riding.** Mounting makes the rider's body kinematic and parents it into the seat, so it is no longer the thing that moves — velocity written there is discarded in silence. An item that must work in a saddle asks the *vehicle* instead, through `ITowable` (the grappling hook is the worked example). And an item that **aims** from a seat must reject hits on the machine it is fired from: `MountModule` waives rider↔mount *collisions*, but raycasts are queries and ignore that entirely.
- **Effect on the holder's body written in `Use()`** → applied to the server's kinematic replica and overwritten within a tick. Use `UseAuthority.Owner`, or `EffectItem` (whose `Use()` is `sealed` for this reason).
- **Recomputing aim on the receiving machine** → `Camera.main` on the server is the *host's* camera, so every client's shot follows the host's crosshair. Aim only ever travels in `NetArg`.
- **For a continuous item, send the ray (origin `P` + rotation `R`), not the hit point** — let each machine trace it.
- **No `holdTimeout` on a held item** → a release is one message and a disconnect sends none, so the beam burns forever. Also record the aim on the `Hold` path: a dedicated server never receives `PresentHold`.
- **Missing network prefab entry fails on clients only.** The host instantiates its own copy and never consults the list.
- **Item asset outside `Assets/Game/Resources/Items/`** never registers (`RegistryLoader` does `Resources.LoadAll<InventoryItem>("Items")`), never shows in the dev browser (`O` in dev mode), and every save slot holding it loads empty. `Assets/Game/ScriptableObjects/Items/` holds unreachable duplicates.
- **`owner` is null exactly once per equip** if you read it before `OnEquipped` — which is why `OnEquipped` sets it, and why `ToolItem.aimProvider` resolves on demand rather than caching.
- **A refilling item that forgets `OnMaxUsesReached() { }`** is removed from the inventory the first time it empties.
- **Hand-editing a prefab owned by a builder script** (`LaserStaffBuilder` and siblings, `Assets/Game/Editor/Items/`) is wiped on the next run — tune in the script.
- **`HoldAnimator.requireStationary = false`** makes the unmasked hold layer animate the legs; the character glides while walking.
- **Deleting a prefab an `InventoryItem` points at** nulls `itemPrefab` silently, with no compile error. Restore by GUID.
- Item scripts compile into `Assembly-CSharp`; there must be **no asmdef** under `Scripts/Items`. Their EditMode tests go in `Assets/Game/Editor/`.
- **Assigning `localRotation` on a part of an imported model destroys how the model was seated.** `_exportlib.export` bakes no transforms, so an FBX node carries whatever rotation and scale the `.blend` gave it — and a hand edit puts the correction there, not in the mesh. Drive an animated part as `rest * Quaternion.Euler(...)` off the rotation captured in `Awake`, never as a bare assignment. `ItemScannerArtifact`'s dial and antenna flattened to identity on the first powered frame until this was fixed (2026-09-03).
- **The same unbaked node scale breaks anything measured off a mesh.** `Mesh.vertices` is pre-scale, so a plate with a non-uniform node scale measures the wrong shape: the item scanner's radar drew its range rings as ellipses. Push local measurements through `renderer.transform.localToWorldMatrix` (and `localBounds` through `lossyScale`). `ItemScannerScreen` also derives the display's handedness this way — the UV gradients crossed against the face normal — instead of leaving `_FlipX` a value somebody guesses and confirms in play. That one is not hypothetical: `ForearmSeat` seats a **left**-arm gauntlet with a negative X scale, which reflects the plate, so a hand-authored `_FlipX` is necessarily wrong on one arm or the other.

## Extending

1. **Mesh** — `blender-model` skill → `Assets/Game/Art/Models/Items/<Name>.fbx`. Skip if reusing.
2. **Script** — `Assets/Game/Scripts/Items/Artifacts/Gadgets/<Name>Artifact.cs`, namespace `SpaceGame.Items`, subclass `ToolItem` (aimed/instant/world) or `EffectItem` (timed change to the holder's body). Big artifacts get their own folder under `Artifacts/`. Pick `Authority`; put world change in `Use()`, everything visible/audible in `Present()`, owner-only knowledge in `OnRequestUse`.
3. **Prefab** — `Assets/Game/Prefabs/Items/Artifacts/Gadgets/<Name>.prefab`: your script, `PickupableItem`, `ItemGrip`, `NetworkObject`, then `ItemWorldPresence.Apply(root)` for the body, the fitted collider, `WorldItem` and the netcode — never write that block by hand, see [Inventory.md](Inventory.md). Prefer an editor builder script when it nests an FBX.
4. **Item asset** — `Assets/Game/Resources/Items/Artifacts/<Name>.asset` (`Create > Items > Item`); set `itemName` and `itemPrefab`.
5. **Back-reference** — set `PickupableItem.item` on the prefab to that asset.
6. **Icon** — run `Tools/Generate All Item Icons`.
7. **Network prefab** — `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`, or append to `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`. Register any deployable too; do **not** register projectiles or equipped visuals.
8. **State** — override `CaptureItemState`/`RestoreItemState` (call `base`) for anything the instance becomes; `IItemDeferredRestore` if it names something else in the world.
9. **Route it in** — `startingItems` on [PlayerCharacterNetworked.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab), an `EntityLootTable`, a `TradeOffer`, or a prefab instance in a chunk scene.
10. **Audio** — pick an existing `SfxId` ([Assets/Game/Scripts/Audio/SfxId.cs](Assets/Game/Scripts/Audio/SfxId.cs)) for `useSoundId`; `PlayUse` plays it on every machine. New FMOD events are not authorable — the Studio project is lost.
11. **Verify** — `Tools/Tests/Run EditMode Tests (headless)` (`NetworkPrefabRegistrationTests`, `HoldPoseTests`), then join as a real client and reload a save.
