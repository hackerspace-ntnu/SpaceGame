---
system: Glintback
layer: characters
summary: "A spire-nesting scavenger that hoards anything that glints; fight it, sneak past, lure it or tame it"
paths:
  - Assets/Game/Scripts/Agents/Modules/Movement/HoardModule.cs
  - Assets/Game/Scripts/Agents/Modules/Social/CreatureTrust.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/CreatureTrustSaveable.cs
  - Assets/Game/Prefabs/Agents/creatures/Glintback.prefab
  - Assets/Game/Art/Models/Creatures/Organic/Glintback
  - Assets/Game/Art/Models/Environment/GlintSpire
  - Assets/Game/Art/Animations/Creatures/Glintback.controller
  - "Assets/Game/Art/Models/_Source~/models/creatures/glintback.blend"
  - "Assets/Game/Art/Models/_Source~/models/props/glint_spire.blend"
symptoms:
  - "the creature ignores a shiny item lying right next to it"
  - "the creature takes an item and it never appears in the nest"
  - "the creature warns me forever at its nest and never attacks"
  - "the Glintback cannot be petted even though it took my gifts"
reads_with: [AgentSystem, Inventory, Multiplayer, Persistence, ArtPipeline]
updated: 2026-10-26
---

# Glintback

A 7 m long-necked, four-legged scavenger that nests on a 16 m rock spire and hoards anything that
glints. The nest holds a hull module. It rests on the nest, forages round its territory, and fetches
loose glinting items home in its beak. Four ways past it (`GDC-L1-DESIGN-0002`, `GDC-L1-SYS-0003`):

| Route | How it falls out of the rules | Cost |
| --- | --- | --- |
| Fight | trespass fills `ProvocationModule`; `ChaseModule` + `CloseCombatModule` (30 damage, 600 HP) | danger; it is dead for good |
| Sneak | it guards only what it **sees** (`PerceptionModule.IsVisible`) — climb up while it forages | time; if it spots you at the nest it runs home |
| Lure | a glinting item in view pulls it off the nest or off its rounds | the item goes to the nest (winnable back) |
| Befriend | an item it takes while **Calm**, with a player within `giftRadius`, is a gift — away from the nest, where a player is an intruder | several items; tame at `giftsToTame` (4) |

**Placed in:** `Tests/Marius test scene.unity` under `GlintbackSpire` at (200, 0, 40), with a scrap
field of batteries, a tank and a scanner at (248, 0, 6), and boulders for cover.

## Model

- **One new module, one new component.** [HoardModule](Assets/Game/Scripts/Agents/Modules/Movement/HoardModule.cs) decides where to go (nest, loot) and feeds `AggressionInput.Trespass`, which nothing fed before. [CreatureTrust](Assets/Game/Scripts/Agents/Modules/Social/CreatureTrust.cs) holds trust (0–1). Everything else is existing agent machinery ([AgentSystem.md](AgentSystem.md)).
- **"Glints" is item data:** `InventoryItem.glints` (read like `menacing`). Ticked on Battery, OxygenTank, Lantern, ItemScanner, RuinScanner, basicgun, AntiGravityPotion, StormFlask and BottledSingularity. Not on hull modules, which are too big to carry.
- **The carry is the inventory, not a parented item** (a NetworkObject cannot hang off a bone). `Take`: `PickupableItem.ReadState()` (bag + live charge) → `EntityInventoryComponent` slot 0 → `World.Despawn`. `EntityEquipmentController` (hand socket = the `MouthSocket` empty on the `Jaw` bone) shows a copy, and `NpcRandomLoadout` **with no candidates** replicates the slot to clients. At the nest: `ItemDropService.DropItem(nest, item, state)`.
- **Rhythm:** Home (`homeSeconds` 25–45, `MoveIntent.Idle` on the nest) → Forage (`forageSeconds` 40–70, returns null so `WanderModule` roams its 60 m) → back home. It notices loot in either phase, within `noticeRadius` of itself and `territoryRadius` (70) of the nest; items within `nestRadius` (4) are the hoard and are never fetched.
- **Trust is body language** (`GDC-L1-SYS-0006`): the animator bool `Trusting` from 0.5 (head carried low); trespass gain × (1 − trust), so warnings fade; at 1 there are no intruders and `PettableModule` opens (its new optional `trust` gate).
- **Warning display:** `AggressionTelegraphModule.drawnAnimatorBool = "Display"` (crest up, wings flared, beak open). That field and `waryAnimatorBool` are new, for bodies without `CharacterActions`.

## Key types

| Type | Priority / key | Notes |
| --- | --- | --- |
| `HoardModule` | Ambient + 3 (13) | **Above the telegraph (12) on purpose:** it must keep feeding trespass while the telegraph holds the body in Drawn. It hands the frame down once Drawn or targeting |
| `CreatureTrust` | NetworkBehaviour | `NetworkVariable<float>`, server-written; `Gift()` and `Restore()`. `petsToTame` 0 here: petting earns nothing and `PettableModule` stays shut until tame. The ranch's eskos set it and are won by petting instead ([Ranch.md](Ranch.md)) |
| `CreatureTrustSaveable` | `creatureTrust` `{ trust }` | added by `SaveablePolicy` for any `CreatureTrust` |

Animator (`Glintback.controller`): `Locomotion` / `LocomotionTrusting` blend trees on `SpeedY` (Idle
or IdleLow 0, Walk 2 m/s at ×1.35, Run 5.5 m/s at ×1.6), `Display`, and one-shots `Meele` → Attack,
`Hurt`, `Happy`, `Death`.

## Multiplayer

- The module runs on the authority only (`AgentController`). Despawn and drop are server calls.
- Clients see: the body via `ClientNetworkTransform`; the jaws via `NpcRandomLoadout`'s `NetworkVariable`; trust via `CreatureTrust`'s; the display via the existing `AgentAction.Band` relay; petting via `PetRequest`/`Petted`.
- **Verified offline only** (host-of-none, play mode): noticed a lure 24 m away, walked down the ramp, took it (slot plus visible jaw copy) and carried it home; a tank left beside it on the nest was taken and dropped in the nest (a drop-service copy); 4.5 s of trespass → Wary and 8.5 s → Drawn with the `Display` pose on. **Not yet seen in play:** a real player's approach driving the meter to an attack, gifting to tame, and petting. **Nothing is verified on a real client.**

## Persistence

- Standard agent savers plus `EntityInventorySaveable` (a carried item) and `CreatureTrustSaveable`. Dead is `HealthSaveable`'s.
- The phase is not saved: a creature loaded carrying something simply heads home.
- **Gotcha:** `EntityInventorySaveable` stores item ids only, so a battery saved mid-carry comes back at full charge.

## Gotchas

- **Two objects are named `Glintback`**: the creature root and the FBX armature under `Model`. `GameObject.Find("Glintback")` can return the armature; address it by path.
- **Rigid-part rig exported with `scale_all=True`**, so the bones are scale 1 and `MouthSocket` and `PetTarget` need no 1/100 compensation (unlike Appa). `optimizeGameObjects` must stay off.
- **Players are found through `EntityTargetRegistry` plus the player's `EquipmentController`** (the rule `MenaceSensor` uses). `PlayerIdentity.All` misses the non-networked test-scene player.
- **A player at the nest blocks gifting there:** while it sees an intruder it guards and notices no loot, so gifts only work out on its rounds. That is deliberate — otherwise tossing items from inside the nest would lure it off its own hoard.
- The creature walks the spire's spiral ramp; the NavMesh has no jump links. Re-bake the scene's `NavMeshSurface` after moving the spire.

## Extending

1. **Another hoarder:** `HoardModule` + `EntityInventoryComponent` + `EntityEquipmentController` (socket on the jaw) + `NpcRandomLoadout` (no candidates) + a `nest` transform. `CreatureTrust` is optional.
2. **A companion phase** (follows you, sniffs out salvage once tame) is designed but **not built**.
3. **Models:** `glintback.blend` is built from bone-parented low-poly parts with 9 actions; re-export with `_exportlib.export(keep_armature=True, keep_empties=True, animations=True, scale_all=True)`. The spire, nest and boulders are `Coll_*` in `glint_spire.blend`.
