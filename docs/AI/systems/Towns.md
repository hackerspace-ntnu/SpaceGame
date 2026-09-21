---
system: Towns
layer: world
summary: "Drag-in generator that places a whole settlement from settings on the component, plus fetch-quest givers"
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/Town
  - Assets/Game/Scripts/Gameplay/Quests
  - Assets/Game/Editor/World/TownGeneratorEditor.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/QuestGiverSaveable.cs
symptoms:
  - "a generated town lost every saved NPC, quest and dropped item in it"
  - "pressing Generate a second time replaced my whole town instead of moving it"
  - "Generate refuses and says it would orphan save records"
  - "a generated town's scene diff is huge even though I changed nothing"
  - "I added a prefab to a town, pressed Generate, and nothing appeared"
  - "the town generator reported success but placed nothing"
  - "buildings in a generated town stand inside each other"
  - "a generated town placed nothing and the report says it found no ground"
  - "something I parented under the Generated object disappeared"
  - "editing the settings on the town component changes nothing"
  - "an NPC with a questline never offers it when I talk to them"
  - "a quest giver keeps asking for an item I already handed over"
  - "handing an item to a quest giver took the item but the quest did not advance"
  - "a quest step advanced on the host but the client still asks for the old item"
  - "the NPCs in a generated town stand still and cannot walk anywhere"
reads_with: [TerrainGeneration, NavMeshSystem, Persistence, Multiplayer, AgentSystem, InteractionSystem]
updated: 2026-09-15
---

# Towns

Drop `TownGenerator` on an empty GameObject, fill in the settings **on the component**, press
**Generate**. It places buildings, props, scatter and people as children of that GameObject, wires
the town's faction defences, and deals each questline to one of the NPCs it placed.

**Not the `Settlement*` types**, which share the namespace but generate **one monumental ruin**
([TerrainGeneration.md](TerrainGeneration.md)). Everything here is `Town*`.

## Model

- **Edit time only**, forced by three things: the NavMesh is one author-time bake and nothing bakes
  at runtime; a runtime spawn needs a registered network id; a scene object's save id is baked by
  `SaveableEntity.OnValidate`, which never runs in a player. Generate writes ordinary GameObjects
  under a `Generated` child — no prefab, no asset — and **you commit the scene**.
- **The settings are a plain `[Serializable]` class on the component, not an asset.** Authoring an
  asset before you can place one hut is friction with no payoff. `TownRecipeAsset` is a box round
  the same class for towns that must stay identical; assigning one **overrides** the inline settings.
- **Generate is idempotent, not destructive.** A second press matches each new slot against what is
  already standing (`TownReuse`) and *moves* it, so widening `minStructureSpacing` pushes the same
  buildings apart with save identity intact. Only prefabs the settings no longer ask for are
  destroyed — that alone needs `confirmRegenerate`.
- **One recipe, four lists.** Buildings, props, scatter, people are four `List<TownGroup>`; a group
  is "N of these prefabs, in this band, turned this way" — which lets one type describe both a mining
  post and a nomad camp, where `RobotSettlementRecipe`'s noun slots can only describe a compound.
- **People are a prefab and a count. Nothing else** — no task authoring, no module wiring, no
  `WorldSiteMarker`; what an NPC *does* is its prefab's business ([AgentSystem.md](AgentSystem.md)).
  **Layout and reuse are both pure**: no scene, `Physics`, `Terrain` or `UnityEngine.Random`, and
  `TownReuse` creates and destroys nothing. Both EditMode-testable, as `SettlementSiteScore` is.
- **One `System.Random`, threaded. Draw order IS the seed**: sections in `TownSection` order, groups
  in list order, instances in order. **Append only** — inserting or reordering a group moves every
  town that recipe ever produced.
- **A questline is ordered (line, required item, optional reward)** — no objective types, no flags, and progress is one `int`.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `TownRecipe`, `TownRecipeAsset` | [Config/](Assets/Game/Scripts/World/ProceduralGeneration/Town/Config) | The settings — radii, ground, four group lists, questlines, territory. A plain class on the component; the asset is an optional shared copy |
| `TownGroup` | [Config/TownGroup.cs](Assets/Game/Scripts/World/ProceduralGeneration/Town/Config/TownGroup.cs) | Prefabs + count + band + yaw + clearance + pad + scale + cluster. Also `TownSection`, `TownYaw` |
| `TownLayout`, `TownSlot` | [Core/](Assets/Game/Scripts/World/ProceduralGeneration/Town/Core) | **Pure static.** Recipe + seed → slots, which hold no GameObject, deliberately |
| `TownReuse`, `TownInstance` | [Core/](Assets/Game/Scripts/World/ProceduralGeneration/Town/Core) | **Pure static.** Slots + existing children → move / make / destroy. The stamp is the reuse key: section + group + source prefab |
| `TownPlacement` | [Core/TownPlacement.cs](Assets/Game/Scripts/World/ProceduralGeneration/Town/Core/TownPlacement.cs) | Footprint, clearance radius, ground raycast, foundation pads. **Shared with `RobotSettlementGenerator`** |
| `TownGenerator` | [Spawning/TownGenerator.cs](Assets/Game/Scripts/World/ProceduralGeneration/Town/Spawning/TownGenerator.cs) | The MonoBehaviour: `Generate` / `Clear` / `Reroll` / `Precheck` |
| `Questline`, `QuestStep`, `QuestProgress` | [Quests/](Assets/Game/Scripts/Gameplay/Quests) | GUID-stamped asset; a step is text + `required` + optional `reward`/`thanksLine`. `QuestProgress` is the pure step machine |
| `QuestGiver` | [Quests/QuestGiver.cs](Assets/Game/Scripts/Gameplay/Quests/QuestGiver.cs) | On the NPC; `TryOfferQuest`, both `NetMsg` handlers, sole writer of the step index |
| `QuestGiverSaveable` | [Adapters/QuestGiverSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/QuestGiverSaveable.cs) | Key `"quest"`: questline GUID + step |

## Flows

**Generate:** `Precheck` (refuses on a count with no prefabs or a null inside `prefabs`,
non-increasing radii, a malformed questline, or territory flags with no faction) →
`TownLayout.Build` → `TownReuse.Match` → **refuse if the surplus carries save records and
`confirmRegenerate` is off** → destroy surplus → per slot `SampleGround` then move-or-instantiate,
scale, stamp, optional pad → `WireTerritory` → `AssignQuestlines` → `Verify`. Nothing is written
before the refusal point, so a refusal leaves the town as it was.

A slot whose raycast finds no ground is **skipped**, never dropped at a guessed height — but if
something already stands there it is left alone rather than destroyed: an unloaded terrain collider
is transient and orphaned save records are not. `WireTerritory` puts `SettlementAlarm` and
`SettlementPopulation` **on the town root**, not the `Generated` child, so `Clear` does not take
them; the inhabitant table is **derived** from the People groups. `AssignQuestlines` deals from a
separate rng salted `seed ^ 0x51ED21`, so adding a questline does not move the buildings, and
**strips** the `QuestGiver` off any NPC it did not deal to — or a reused NPC keeps last run's errand.

**Talking:** `DialogInteraction.Interact` → trader consult → **then** `QuestGiver.TryOfferQuest`.
Without the item, the step's own line is the reminder and a 45 s cooldown starts; with it,
`AskQuestion("Hand it over" / "Not yet")`. **Handing over:** `onYes` → server (direct call when
already server, else `QuestHandIn`) → `ApplyHandIn` checks the step index matches and `CanHandIn`
passes → takes item, gives reward → broadcasts `QuestStepSet`, applied by every machine including
the server in `OnStepSet`.

## Multiplayer

**The server alone decides a step is complete** and is the only writer of the index. Both ids on
the giver's relay: `QuestHandIn` = 115 (client→server; `A` = step the client believes, `Target` =
player); `QuestStepSet` = 116 (server→everyone; `A` = authoritative step, `B` = 1 success / 0
correction, `Target` = the player who handed over).

**Deliberately not `TraderInteraction.TryExecute`'s local-first order.** Trading contests the
trader's *stock*, and `CanAfford` runs against the client's own bag — authoritative for that
question. A quest contests the *step index*, which a client can be arbitrarily stale about, so
local-first would spend the item on a step that no longer exists and then be refused.

**Not on the wire:** the inventory change. `Network.Simulates` is `true` on the server for
*everything*, so the server writes even a remote player's `PlayerInventoryNetwork` directly and its
`NetworkList` replicates to the owner. The town itself is **not replicated** either — scene content,
same bytes everywhere. Only `SettlementPopulation`'s reinforcements go through
`GameServices.World.Spawn`, so those prefabs must be in the live network prefab list.

## Persistence

- **Quest progress is shared world state**, on the NPC's entity record under key `"quest"` — one
  player hands in, the errand advances for the session, as trader stock already does. The record
  names the questline by **asset GUID**; one about a different questline is discarded with a
  warning, because re-dealing can move a questline to another NPC.
- `CaptureState` returns `null` at step 0, so untouched NPCs write no row.
- **`RestoreState(null)` restores step 0**, it does not return early. Unlike `TraderSaveable`, whose
  `Awake` rebuilds offers from the profile, nothing rebuilds a step index — and a re-hydrated chunk
  can hand back a giver still mid-errand in memory.
- Attached by a clause in `SaveablePolicy.Ensure` (covers prefab, placed instance and runtime spawn
  at once). `QuestGiver : IPersistentEntity` so `NeedsSaving` says yes without health/agent/rigidbody.

## Gotchas

- **Only destruction orphans records, but it orphans them forever.** A destroyed object cannot keep
  its `instanceId`/`GlobalObjectIdHash`, and its records are `authored`, so `DropVanishedRuntime`
  never drops them — they sit in every save file for good. Hence `Generate` moves rather than
  rebuilds, and removing a prefab needs `confirmRegenerate`. `Clear` destroys everything, always.
- **A group's count is a RANGE, and one starting at zero is allowed to roll zero.** "At least 1, at
  most 0" places nothing about half the time and looks exactly like a broken generator — it is what
  seed 1701 did on the first town anyone built. `Verify` fails and names the group; the inspector
  labels both boxes rather than showing X and Y. Put the same number in both for exactly that many.
- **Anything you parent under `Generated` by hand is destroyed on the next Generate.** It carries no
  `TownInstance`, so nothing claims it. Put hand-placed objects beside the town root, not inside it.
  Foundation pads are rebuilt every time for the same reason, deliberately — they hold no save state.
- **`SaveableWiring.Wire` does not bake GUIDs** — its body is `SaveablePolicy.Ensure`, which only
  adds components. Identity comes from `SaveableEntity.OnValidate` via `RecordAsPrefabOverrides()`.
- **A shared recipe asset silently wins over the inline settings**; the inspector greys them out.
  **`Reroll` keeps its new seed**, unlike `RobotSettlementGenerator.Reroll` — do not copy that one.
- **`terrainMask` defaults to Everything and terrain shares `Default` with buildings**, so a later
  placement can land on an earlier one's roof; clearance radii are what keeps things apart. A mesa
  is invisible to it either way — feature meshes are spawned at bake time, so both the raycast and
  `Terrain.SampleHeight` report the flat terrain under them.
- **A new town makes the world NavMesh stale and nothing says so**: NPCs stand still and
  `WorldNavMeshBuildCheck` fails the next build. Run `World > Streaming > Bake World NavMesh`.
- **A `QuestGiver` on a character with no `DialogInteraction` is never asked anything** — nothing
  throws, the quest simply does not exist. Hence `[RequireComponent]`, and `Verify` refuses more
  questlines than eligible NPCs; today only the five `Nomad*` prefabs carry one. And **no prefab
  carries `TraderInteraction`**, so this consult seam had never run in a scene, on a client, or
  through a save before now.
- **A late-joined client may be asked for the wrong item** until someone hands in. It can never
  *lose* anything — the server refuses the stale request and the refusal corrects it. Accepted.

## Extending

**A new kind of town** — fill in the settings on a `TownGenerator`. No code, no asset. Append
groups; never insert or reorder. **A new questline** —
`Assets > Create > SpaceGame > Quests > Questline`, then drop it in the settings; every step needs a
`required` item and a line, or `Verify` refuses it.

**A new placement behaviour** (walls, roads, a plaza) — add a pass to `TownLayout` taking the
existing `System.Random`, called from `Build` **after** the current passes so existing seeds hold.
Keep it pure; scene-touching work belongs in `TownPlacement`.

**A new objective kind** (kill, reach, escort) — there deliberately is none. A step is a line plus
an item; a second kind needs a discriminator that both `QuestProgress.CanHandIn` and `TryOfferQuest`
branch on. Do it when one is wanted, not in anticipation.
