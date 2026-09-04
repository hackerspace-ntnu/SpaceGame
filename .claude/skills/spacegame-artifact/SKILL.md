---
name: spacegame-artifact
description: Use when adding or fixing a usable item in this Unity project — a new artifact, gadget, spell, scanner, throwable, potion, deployable, hand tool or weapon-like equippable that occupies a hotbar slot and fires on the Use button. Also use when an existing artifact works for the host but does nothing for a client, is invisible to other players, logs "[WorldService] Prefab 'X' has no NetworkObject" when dropped, cannot be picked back up, never appears in the dev item browser (O), has a blank or wrong inventory icon, floats in the wrong place in the hand, or stands in the idle pose instead of the hold pose. Covers UsableItem / ToolItem / EffectItem subclasses, InventoryItem assets under Assets/Game/Resources/Items, prefabs under Assets/Game/Prefabs/Items/Artifacts/Gadgets, and their network-prefab registration.
---

# SpaceGame Artifacts

> **Design check:** before deciding how an artifact *feels* or what it costs the player, read the
> relevant `FEEL`, `SYS` and `BAL` principles in `docs/game-development-constitution/INDEX.md` and
> cite their IDs.

## Overview

An artifact is **four assets that point at each other** — a `UsableItem` subclass, one prefab, an
`InventoryItem` ScriptableObject in `Resources/Items`, and an icon sprite — plus **one entry in the
network prefab list**. The single prefab is both the thing in the player's hand and the thing lying
in the sand, so it carries hold, pickup, physics, save and network components at once.

Networking is not per-item work. `EquipmentController`
(`Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs`) already splits every use
into `Use()` (authority only) and `Present()` (every machine). Put the effect in the right half and
the artifact replicates; put it in the wrong half and it still works in single-player, which runs
as a host of one.

Worked examples to read first: `LightningSpell` (simplest aimed use), `AntiGravityPotion`
(`EffectItem`), `GrapplingHookArtifact` (owner authority), `LaserStaffArtifact` (continuous hold),
`ItemScannerArtifact` (all-cosmetic) — all under `Assets/Game/Scripts/Items/Artifacts/`.

## When to use

New or changed **player-held** item: artifact, gadget, potion, scanner, throwable, deployable.

**Not** for: NPC-only guns driven by weapon-profile ScriptableObjects (`SpaceGame.Weapons.Weapon`,
`Assets/Game/Scripts/Weapons/Core/Weapon.cs`), rideable vehicles/mounts, world interactables
(`IInteractable`), or authoring the 3D mesh — that is the `blender-model` skill.

## Build order

No input work is ever needed: every artifact fires on the shared `Player/Use` action, routed by
`PlayerInputManager.OnUsePressed` / `OnUseReleased`. Do not edit `.inputactions` for an artifact —
`InputControls.cs` embeds its own copy of that JSON and is what actually binds at runtime.

Item scripts compile into `Assembly-CSharp`; there is no asmdef under `Scripts/Items` and there must
not be one, because these types reach Assembly-CSharp code. Editor tests for them therefore go in
`Assets/Game/Editor/`, not beside the asmdef'd EditMode tests.

1. **Mesh** — `blender-model` skill; export to `Assets/Game/Art/Models/Items/<name>.fbx`
   (or `Assets/Game/Art/Models/Weapons/<Name>/`). Skip if reusing an existing FBX.
2. **Script** — `Assets/Game/Scripts/Items/Artifacts/Gadgets/<Name>Artifact.cs`, namespace
   `SpaceGame.Items`, subclass of `ToolItem` (aimed/instant) or `EffectItem` (timed change to the
   holder's own body). Big artifacts get their own folder under `Artifacts/`.
3. **Prefab** — `Assets/Game/Prefabs/Items/Artifacts/Gadgets/<Name>.prefab`. Root component list and
   field wiring: `references/prefab-and-builder.md`. Prefer an editor builder script (pattern in
   the same reference) whenever the prefab nests an FBX.
4. **Item asset** — `Assets/Game/Resources/Items/Artifacts/<Name>.asset` (`Assets > Create > Items >
   Item`). Set `itemName` and `itemPrefab`. It **must** be under `Resources/Items`:
   `RegistryLoader` does `Resources.LoadAll<InventoryItem>("Items")`
   (`Scripts/Core/Registry/RegistryLoader.cs`). `Assets/Game/ScriptableObjects/Items/` holds older
   duplicates that no `Resources` scan reaches — do not add there.
5. **Back-reference** — set `PickupableItem.item` on the prefab to the asset from step 4. The two
   files reference each other; this link can only be made after both exist.
6. **Icon** — run `Tools/Generate All Item Icons`. It renders each item's own `itemPrefab` and
   writes `Assets/Game/Art/Sprites/Items/<Name>.png` back into `InventoryItem.icon`.
7. **Network prefab** — run `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`, or append to
   `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`. Mandatory: dropping a
   hotbar slot routes through `PlayerDropService` → `GameServices.World.Spawn`.
8. **Route it into the game** — one or more of: `startingItems` on
   `Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab`
   (`PlayerInventoryNetwork`); an `EntityLootTable` entry; a `TradeOffer`; a prefab instance placed
   in a chunk scene.
   Nothing extra is needed for the dev browser — it lists the whole registry (press `I` with
   `GameSettings.DevMode`).
9. **Audio** — pick an existing value from `SfxId` (`Assets/Game/Scripts/Audio/SfxId.cs`) for
   `useSoundId`; `PlayUse` plays it on every machine for free. Never try to author a new FMOD
   event: the FMOD Studio project is lost, so the banks' 19 shipped events are all there is.
10. **Verify** — run `Tools/Tests/Run EditMode Tests (headless)`; `NetworkPrefabRegistrationTests`
    and `HoldPoseTests` are the two that guard this pipeline.

## The script

```csharp
// Assets/Game/Scripts/Items/Artifacts/Gadgets/SmokeBombArtifact.cs
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>Throws a canister that lands where the holder aimed and blooms into smoke.</summary>
    public class SmokeBombArtifact : ToolItem
    {
        /// <summary>
        /// Server. The canister is shared world state, so exactly one machine may create it —
        /// UseAuthority.Owner is only for tools whose whole effect is the holder's own body.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Tooltip("Spawned where the throw lands. Must be a REGISTERED network prefab.")]
        [SerializeField] private GameObject canisterPrefab;

        [Tooltip("Puff at the hand. Cosmetic, so every machine plays it.")]
        [SerializeField] private ParticleSystem throwPuff;

        [SerializeField] private float range = 40f;

        /// <summary>
        /// Owner-side, before the request leaves. The only machine where an aim is honest: a peer's
        /// copy of this player has an AimProvider with no live camera behind it.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            RaycastHit? hit = aimProvider != null ? aimProvider.GetRayCast(range) : null;

            // Zero means "aimed at open sky". Without this, `?? Vector3.zero` reads as a position
            // and the throw lands at the world origin.
            arg.P = hit.HasValue ? hit.Value.point : Vector3.zero;
        }

        /// <summary>Authority only — the server, or the single machine when offline.</summary>
        protected override void Use()
        {
            if (canisterPrefab == null || UseArg.P == Vector3.zero) return;

            GameServices.World.Spawn(canisterPrefab, UseArg.P, Quaternion.identity);
        }

        /// <summary>
        /// Every machine, and immediately on the thrower's so the item never feels like it is
        /// waiting for a round trip. `useSoundId` is already played for you by PlayUse.
        /// </summary>
        protected override void Present()
        {
            if (throwPuff != null) throwPuff.Play();
        }
    }
}
```

## Quick reference

| Need | Where |
| --- | --- |
| Aimed, instant, or world-changing | `ToolItem` — `Assets/Game/Scripts/Items/Core/ToolItem.cs` |
| Timed change to the holder's body | `EffectItem` — override `ApplyEffect()`, **not** `Use()` (sealed) |
| Acts while the button is held | `override bool IsContinuous => true` + `OnRequestHold`/`Hold`/`PresentHold` |
| Owner's aim, per press / per tick | `OnRequestUse(ref NetArg)` / `OnRequestHold(ref NetArg, bool)`; read it back as `UseArg` |
| Payload fields | `NetArg.P` (Vector3), `.R` (Quaternion), `.A` (int, already the slot code — bare hotbar index, or `UseSlotCode` for a worn slot), `.B` (int), `.With(go)` for a subject |
| Consumable | `maxUses` on `UsableItem`; depletion removes the slot automatically |
| Equip / unequip hooks | `OnEquipped(GameObject holder)` / `OnUnequipped` — always call `base` |
| Worn, not gripped | `override bool UsesHoldPose => false` |
| Hand pose, size, offsets, which hand | `ItemGrip` on the prefab root — `Assets/Game/Scripts/Items/Equipped/ItemGrip.cs` |
| Who may act here | `Network.Simulates(this)`, `Network.Owns(t)`, `Network.Server`, `Network.IsNetworked` |
| Damage | `NetDamage.Apply(GameObject target, int amount, Transform source)` |
| Spawn into the world | `GameServices.World.Spawn(prefab, pos, rot)` |
| Sound | `SfxId` + `Sfx.Play(id, position, overrideRef, sourceKey)` |

## Common mistakes

- **Effect written in `Use()` when the effect is the holder's own body.** The player's
  NetworkTransform is owner-authoritative, so a server-applied force is overwritten within a tick
  and silently. Use `UseAuthority.Owner`, or `EffectItem`, whose `Use()` is `sealed` for exactly
  this reason (`Assets/Game/Scripts/Items/Core/EffectItem.cs`).
- **Recomputing the aim on the receiving machine.** `Camera.main` on the server is the *host's*
  camera, so every client's shot flies along the host's crosshair. Aim travels in `NetArg` from
  `OnRequestUse` only.
- **Sending the hit point instead of the aim ray** for a held/continuous item. Send origin in `P`
  and rotation in `R`, and let each machine trace it (`LaserStaffArtifact.OnRequestHold`).
- **No authority timeout on a held item.** A release is one message and a disconnect sends none;
  without `holdTimeout` the beam burns forever. Also: a dedicated server never receives
  `PresentHold`, so record the aim on the `Hold` path too.
- **Networking a projectile or an equipped visual.** Only what `GameServices.World.Spawn` is handed
  belongs in the network prefab list — item prefabs and deployables. A bullet or a flying arc is
  instantiated **locally by every machine** from `Present()`, with only the authority's copy dealing
  damage; a child model in the hand is a plain `Instantiate` onto a bone and cannot carry a
  NetworkObject at all.
- **Forgetting the network prefab entry.** Fails on **clients only** — the host instantiates its own
  copy and never consults the list, so solo playtesting cannot find it.
- **Adding the item asset outside `Resources/Items`.** It never registers, never appears in the dev
  browser, and every save slot holding it comes back empty.
- **Hand-editing a prefab a builder script owns.** `LaserStaffBuilder` and friends replace the
  prefab wholesale on the next run; tuning belongs in the script's constants.
- **`requireStationary = 0` on `HoldAnimator`.** The player controller has one unmasked layer, so
  the hold clip animates the legs — the character glides while walking. Leave the default true.
- **Deleting a prefab an item points at.** `InventoryItem.itemPrefab` goes silently null with no
  compile error. Restore by GUID rather than re-authoring.

## Related skills

- `blender-model` — authoring the FBX and the shared material palette.
- `spacegame-multiplayer` — `NetRelay`/`NetChannel`/`NetMsg`, authority rules, prefab registration
  in depth.
- `spacegame-persistence` — `SaveableEntity`, `SaveScope`, the saveable prefab registry.
- `spacegame-agent` — giving an NPC an artifact to carry and fire.

Full prefab component table, field-by-field wiring, and the editor-builder template:
`references/prefab-and-builder.md`.
