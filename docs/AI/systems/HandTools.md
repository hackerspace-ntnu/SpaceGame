---
system: HandTools
layer: items
summary: "89 carried tools, each held by a named stance with its grip solved from the real pose"
paths:
  - Assets/Game/Scripts/Items/Tools
  - Assets/Game/Scripts/agents/Entity/BeltCarrier.cs
  - Assets/Game/Editor/Items/HandTools
  - Assets/Game/Editor/Animation/HoldClipGenerator.cs
  - Assets/Game/Prefabs/Items/Tools
  - Assets/Game/Resources/Items/Tools
  - Assets/Game/Art/Models/Items/Tools
symptoms:
  - "a Raxy carries nothing although its prefab lists tools"
  - "a tool hangs off a Raxy's belt pointing the wrong way, or floats away from the hip"
  - "a carried tool does nothing when used"
  - "a tool is too long to stow on the backpack"
  - "a Raxy carries tools but nothing hangs on it, with a warning about no belt or backpack with mount points"
  - "a Raxy holds a bucket or tool sideways, upside down or pointing at its own back"
  - "a Raxy raises its free hand as if steadying a pistol while carrying a one-handed tool"
  - "a cart is carried on the shoulder instead of pushed ahead of the Raxy"
  - "a carried tool shows in the artifact browser (O)"
  - "[HandTools] X: seated N degrees off its Y stance"
reads_with: [Inventory, Artifacts, AgentSystem, ArtPipeline, HumanoidAnimation]
updated: 2026-10-03
---

# Hand tools

Props a Raxy carries: hammers, shovels, spears, buckets, baskets, tanks, carts, an electric harpoon gun. They are ordinary inventory items with no effect of their own, so they reach the world, the hotbar and a save exactly as [Inventory.md](Inventory.md) describes. They are **not** listed in the artifact browser (O): `InventoryItem.showInDevBrowser` is off for every one.

## Model

- **Source:** rows `Deco_Tools*` and `Deco_Carriers` of `_Source~/models/buildings/decorations.blend` (89 `Coll_Deco_Tool_*` / `Coll_Deco_Carry_*`). The `Root_*` empty **is the grip point**; Blender +Z (the business end) is Unity +Y, Blender −Y (the working face or muzzle) is Unity +Z — measured against the built prefabs, it holds. Exported one FBX each to `Art/Models/Items/Tools/<Category>/<id lowercase>.fbx`.
- **Item:** [CarriedToolItem](Assets/Game/Scripts/Items/Tools/CarriedToolItem.cs) (a `ToolItem` whose `Use()` does nothing) + `ItemGrip` + optional [BeltMount](Assets/Game/Scripts/Items/Tools/BeltMount.cs). `useAction` plays a body action on use; only **one-shot** actions are wired (Stab, Spear Throw, Sword Strike, Lasso Throw, Plant Seedling, Poke Ground) because a Loop action such as Hammer would start and never end.
- **Stance, not numbers.** A roster row names a [`CarryStance`](Assets/Game/Editor/Items/HandTools/CarryStance.cs) and nothing else about how it is held; [`GripFitter`](Assets/Game/Editor/Items/HandTools/GripFitter.cs) turns that into the pose and the `ItemGrip` offsets. A stance is a pose plus two reference axes (the tool's along-axis and working face) and where each should point on the holder; the fitter holds the real pose on a real Raxy, reads the hand frame, and solves the rotation. Nobody types a rotation.

  | Stance | Pose | One or two hands | Meaning |
  | --- | --- | --- | --- |
  | `Wield` | `Ready` | one | short tool, head up-forward at 50° |
  | `Staff` | `Carry` | one | long haft upright at the side, leaning 10° |
  | `Hang` | `Carry` | one | vessel by its handle, rim up |
  | `Aim` | `OneHanded` | per override | gun, spyglass, shield face: item +Z forward |
  | `Push` | `Push` | **both** | cart ahead of the holder, wheels down |

- **Roster:** [HandToolRoster](Assets/Game/Editor/Items/HandTools/HandToolRoster.cs) is the one table: stance, hold size, optional `PoseOverride`, `ItemAlong`/`ItemFace` (a scraper's handle lies along +X), `Nudge` (metres, holder space), belt hang point. [HandToolBuilder](Assets/Game/Editor/Items/HandTools/HandToolBuilder.cs) rebuilds prefab, item, icon and network entry from it; GUIDs survive a re-run.
- **Poses:** [HoldClipGenerator](Assets/Game/Editor/Animation/HoldClipGenerator.cs) writes the one-armed hold clips and points the humanoid profile at them. `Carry` is the library's `Idle_Loop` (arms down), `Ready` is `Sword_Idle`'s right arm with the left arm taken from the idle (`Hold_Ready.anim`), `Push` is `Push_Loop`. **Every older hold style is a gun clip that keys both arms**, which is why a bucket-carrier used to raise its free hand like a pistol grip; these three leave the off arm hanging.
- **Belt and pack:** the hang points are **not** on the Raxy. Each belt and backpack garment prefab carries a [GarmentMounts](Assets/Game/Scripts/Items/Tools/GarmentMounts.cs): a bone (`Hips` for belts, `Spine` for the pack) and one child transform per `BeltSlot` (`HipRight`, `HipLeft`, `Back`, `PackLeft`, `PackRight`). A point is authored in the garment's **mesh space** (what its `RestPose` view shows in Prefab Mode): origin at the loop or ring, +Y up toward the garment, +Z out from the body. [BeltSeat.CreateAnchors](Assets/Game/Scripts/Items/Tools/BeltSeat.cs) carries each point through the garment's bind pose to a bone-parented anchor, so one set of points fits every Raxy whatever its model scale. `BeltSeat.Hang` lays an item's `BeltHang` child on an anchor. [BeltCarrier](Assets/Game/Scripts/agents/Entity/BeltCarrier.cs) hangs every bag slot that is not in the hand and has a `BeltMount`, on the item's preferred slot or the first free one in enum order. Points placed: `Belt.standard` / `Belt.poncho` hips + back, `Work_belt` left hoop + back, `skirt_workBelt` right hoop + left + back, `Backpack` both low back corners; `Work_Belt_Flask` offers none (the flask is the right hip).
- **Residents:** the archetype says what a profession carries (`ResidentArchetype.heldItem`, `beltItems`); [ResidentCarry](Assets/Game/Scripts/agents/Residents/Core/ResidentCarry.cs) fills an empty bag from it before the hand draws slot 0. [RaxyToolLoadouts](Assets/Game/Editor/Items/HandTools/RaxyToolLoadouts.cs) holds the profession table (17 of 20; storyteller, elder, villager carry nothing) and adds the bag, hand, belt, `ResidentCarry` and savers to every Raxy prefab that has a `Resident` (**Equip Residents**).

## Flows

Equip: `EntityEquipmentController` → `EquipItemSocket` seats the grip point in the palm at `handFrame * Euler(rotationOffset)`; `HoldAnimator` writes the item's `Style` into the Upper Body layer. Belt: `BeltCarrier.LateUpdate` notices a changed hand slot or bag and re-hangs.

Build: **Tools ▸ SpaceGame ▸ Animation ▸ Generate Hold Clips** (once, or after changing poses), then **Items ▸ Hand Tools ▸ Build All**. Build All fits every tool, then seats each built prefab through the real `EquipItemSocket` and logs `[HandTools] X: seated N degrees off its Y stance` if the tool's along-axis is more than 1° from the stance.

Look at it: **Items ▸ Hand Tools ▸ Preview Held And Belt** → `Temp/HandToolPreview/<Id>.png`, one row per tool: whole body, hand close, hand side, belt close, belt back, whole body from the side. Headless: `-executeMethod SpaceGame.EditorTools.HandToolPreview.PreviewFromCommandLine -handTools Id,Id`.

## Multiplayer

Every machine builds the same bag from the instance's archetype and draws the same belt; nothing is sent. A bag changed at runtime on the server (a looted tool) is **not** replicated to clients, as `EntityInventoryComponent` is local state. Item prefabs are registered network prefabs like all items. **Not yet seen on a second machine** — the pose and grip are baked data, so there is nothing host-only about them, but it is unverified.

## Persistence

Nothing of its own: the bag is `EntityInventorySaveable`, the hand slot `EntityEquipmentSaveable`, the belt is derived. A dropped tool persists as any dropped item. `showInDevBrowser` is an asset field, not state.

## Gotchas

- **The fitter owes its truth to [HandToolRig](Assets/Game/Editor/Items/HandTools/HandToolRig.cs).** Sampling a full-body hold clip also carries the root and the legs, which the Upper Body layer never applies, and a library clip need not face the way the Raxy does. The rig therefore samples a rest pose first (`Carry`'s idle), turns the body to face +Z from its hip line, and puts the head, legs and root back after every pose sample. Without that, every direction a stance names is relative to a body facing the wrong way: the cart came out behind the Raxy and the hammer pointed at its back, with the numbers looking plausible.
- **Offsets are baked from a pose.** Change a hold clip, a stance, or the Raxy rig and the stored `rotationOffset` is stale until **Build All** runs again. `HandToolGripTests` checks the built pose matches the stance; the residual check in the build checks the aim.
- **Only `Push` and the harpoon gun use two hands.** `HandToolGripTests` fails if another tool does. Growing the list is a decision.
- **The cart is scaled up on purpose.** A Raxy is ~1.5× a human, so a hand cart at its honest 2.4 m puts the wheels in the air at hand height; `Carry_Cart_Hand` holds at 3.5 m and `Carry_Cart_Hover` at 3.0 m with a 0.18 m `Nudge` so the shafts meet the hands. `packSize` stays 0.9.
- The electric harpoon gun is a model and an inert item; it fires nothing. No tool is `menacing` — nothing here fires a shot, which `MenaceSensor` requires.
- Tools longer than ~1.4 m carry `packSize` 0.9 so they can be stowed; `PackSizeTests` skips the `Items/Tools` folder for that reason.
- `export_collections` makes parts flat siblings; sheaths, wire and loaded harpoon are separate renderers, not hidden automatically.
- **A garment's own transform is meaningless once worn.** A skinned garment's root sits at an arbitrary FBX offset that differs per Raxy prefab, so mount points are read through the bind pose, never through the root's world position. `BakeMesh` plus `TransformPoint` over-scales garments with a non-1 root scale; measure by skinning the dominant bone by hand.
- `BeltCarrier` searches the whole character for `GarmentMounts` (garments sit beside the Animator, not under it). A Raxy wearing no belt or pack hangs nothing and logs once per item; the first garment offering a slot wins.
- Belt hang points come from the Blender manifests and have only been looked at for the hammer; others may hang tilted.
- The held grip and the preview use the right hand; a left-handed holder plays the mirrored state, and the mirrored grip has not been looked at.

## Extending

Model a `Coll_Deco_Tool_<Name>` with its root at the grip, export it, add a roster row naming a `CarryStance` and a hold size, run **Build All**, look at the preview, put it in a profession row in `RaxyToolLoadouts` and run **Equip Residents**. A new way of holding something is a new `CarryStance` row in `CarryStances.Of` plus, if no existing pose fits, a hold style and clip in `HoldClipGenerator`.
