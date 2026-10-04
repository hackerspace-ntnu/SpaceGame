---
system: Pushables
layer: characters
summary: "A Pushable cart is scenery a body takes hold of: its pose follows the pusher, where it is left is a ledger"
paths:
  - Assets/Game/Scripts/World/Pushables/
  - Assets/Game/Scripts/Characters/Player/Movement/PlayerPushing.cs
  - Assets/Game/Scripts/agents/Residents/Body/ResidentPushing.cs
  - Assets/Game/Scripts/Presentation/Animation/ArmReach.cs
  - Assets/Game/Editor/World/PushableAuthoring.cs
  - Assets/Game/Editor/World/PushablePreview.cs
  - Assets/Game/Editor/Tests/PushableTests.cs
  - Assets/Game/Editor/Tests/CartPusherTests.cs
  - Assets/Game/Editor/Tests/CartPoseSolverTests.cs
  - Assets/Game/Editor/Tests/ArmReachTests.cs
  - Assets/Game/Prefabs/Environment/Decorations/Transport/Deco_Handcart.prefab
  - Assets/Game/Prefabs/Environment/Decorations/Transport/Deco_Handcart_Hover.prefab
  - Assets/Game/Prefabs/Environment/Decorations/Tavern/Deco_FoodCart.prefab
  - Assets/Game/Prefabs/Environment/Decorations/Mining/Deco_MineCart.prefab
symptoms:
  - "a pushed cart floats in the air, is held overhead or waves about in the hands"
  - "I right-click a cart and nothing happens, or the prompt says Handcart but the press is refused"
  - "a cart I pushed is back at its old spot after I reload, or stands somewhere else for a client"
  - "a cart's picture stays where it was baked while its collider moves with me"
  - "the pusher's hands float short of the handles, or the cart is rotated a quarter turn from the way I face"
  - "a pushed cart stands in a wall, or a resident works at a pen with empty hands although a cart stands beside it"
  - "[Pushable] 'X' and 'Y' derive the same id"
reads_with: [Seats, Residents, InteractionSystem, Multiplayer, Persistence, HandTools, PlayerCharacter]
updated: 2026-10-04
---

# Pushables

A cart used to be a *held item*: `Carry_Cart_Hand` scaled to 3.5 m and parented to a hand, its wheels in the air. Now the cart is scenery with two grips and some wheels, and a body that takes hold of it poses it each frame, wheels on the ground, handles in its fists. The pusher is a player or a resident; the cart is the same four decorations a settlement already places.

## Model

| Idea | Mechanism |
| --- | --- |
| A cart is scenery | [`Pushable`](Assets/Game/Scripts/World/Pushables/Pushable.cs) on `Deco_Handcart` / `_Hover` / `Deco_FoodCart` / `Deco_MineCart`: no `NetworkObject`, the same bytes in every machine's chunk scene. Each has a kinematic `Rigidbody`, **no static flags** (in the prefab *and* as overrides on the six NomadSettlement prefabs that nest one), two `Grip_*` markers, a `Handlebar` cylinder, a `Wheel_*` marker under each wheel and, for the handcart only, an `Axle` marker |
| One holder | `TryClaim(who)` refuses while somebody else holds the handles, is idempotent for the holder; `Release(who)` ignores a stranger; a destroyed holder counts as gone. Raises `Released` |
| The pose is derived, never sent | [`CartPusher`](Assets/Game/Scripts/World/Pushables/CartPusher.cs) (on the pusher body, every machine, `LateUpdate`) solves the cart from the body's own transform. Nothing is replicated while a cart moves |
| The solver | [`CartPoseSolver`](Assets/Game/Scripts/World/Pushables/CartPoseSolver.cs), pure: yaw from the body (the cart's nose is the way **from the handles toward its wheels**, so any build faces right); a cart with an `Axle` rests on it and is **pitched until the handles meet the hands**; any other cart rides its contacts, tilted to the ground under them |
| The hands | placed from the body, not a pose: `armReach` of the arm's own length from the shoulders; a tilting cart gets `handsBelowShoulder`, a fixed one the height its model gives its handles (clamped to what an arm reaches). [`ReachingArm`](Assets/Game/Scripts/Presentation/Animation/ArmReach.cs) then turns the shoulder and elbow bones (pure `ArmReach.Solve`) so the **grip-frame palm** lands on the grip. Replaces the `Push` hold pose, which raised a Raxy's hands above its head |
| Where an unheld cart stands | [`PushableLedger`](Assets/Game/Scripts/World/Pushables/PushableLedger.cs): one entry per cart **not at its authored pose**. `Release` rests the cart (wheels stay, shafts come down) and the server records it. Eased into place on every machine |
| Identity | `Pushable.Id` = FNV-1a of `SaveableEntity.DeriveAuthoredId` ([`SceneryId`](Assets/Game/Scripts/World/SceneryId.cs), shared with `Seat`), never 0; `Find(id)`, `NearestFree(point, reach)` |

## Key types

| Type | Role |
| --- | --- |
| `Pushable` | the cart; `IInteractable` ("RMB: push"), `IContextualInteractable` (a player who may grip, within `gripReach` of the handles) |
| `CartPusher` | the body's half, added at runtime by `CartPusher.On(body)`: `Grip` / `Release` / `Follow`; ignores collisions between body and cart, hides the body from the cart's ground probes, tells `BodyLanguage.OccupyArms` |
| `PlayerPushing` | on `PlayerCharacterNetworked`: `NetMsg.PushRequest` (122) / `ReleaseRequest` (123), owner to server; a `NetworkVariable<int>` carries the cart id; the owner's speed is capped (`pushSpeed` 3.5 m/s) and jump / dash are off through `PlayerMovement.StartHauling` |
| `ResidentPushing` | server only, owned by `ResidentRoutine`: an archetype with `pushesCart` takes the nearest free cart within `ResidentTuning.cartReach` while its activity is `Work` at a place; `ResidentPresence.cart` publishes the id; `ResidentHandsRule` empties the hand |
| `PushableLedger` | on the `NetworkGameManager` prefab: a `NetworkList<CartRest>` for everyone, a global saver (key `pushables`) for the file |
| [`PushableAuthoring`](Assets/Game/Editor/World/PushableAuthoring.cs) | `Tools/SpaceGame/Pushables/Author Carts And Wiring`: markers, bar, body, flags, nested overrides, ledger, `PlayerPushing`, the Drover. Idempotent, read back after every save |

## Flows

- **Player grips.** Interact on a free cart → `PlayerPushing.RequestGrip` → server: `Pushable.Find`, within grip reach, `TryClaim`, writes the id → every machine resolves it and `CartPusher.Grip`s. The owner's hotbar is deselected (both hands are busy) and its speed capped. A press that grips is never also the press that lets go (`grippedAtFrame`).
- **Player lets go.** Interact, jump, a drawn hotbar item or death → request → server clears the id → every machine `Release`s; the cart settles where its wheels are. Jump does not also jump (`PlayerMovement` blocks it for the frame after `StopHauling`).
- **Resident.** `ResidentRoutine.Publish` → `ResidentPushing.Sync(archetype.pushesCart && shown == Work && at a place)` → `TryClaim` → id into `Publish(..., newCart)` → `ResidentPresence.ResolveCart` grips on every machine, `hands.Pushing(true)`, and `CueFor` returns no spot loop. Wherever the resident walks the cart goes.
- **Rest and reload.** `Release` → `Pushable.RestPose` (`CartPoseSolver.Rest`) → `PushableLedger.Rest` (server) → list → every machine settles the cart. A cart that appears later (chunk load, late join) calls `PushableLedger.Shown` and is placed at once.

## Multiplayer

- **Why not a `NetworkObject`.** Carts sit inside building prefabs in chunk scenes; a nested scene-placed `NetworkObject` needs a baked hash in every holding scene and a regenerated settlement. The pusher's body already replicates, so a pose derived from it costs nothing and cannot disagree with it. What cannot be derived (where it was left) is the ledger.
- Server decides both grips; only ids travel. A late joiner reads the pusher's id and the ledger list with the spawn; a cart in a chunk not loaded yet is resolved later (`PlayerPushing.Update`, `ResidentPresence.Update` retry).
- **Verified in Play 2026-10-03, offline host:** a cart moved beside the Drover's pen post was gripped within 0.5 s of its next work pass (`ResidentPushing`, id published, hand empty, handlebar in both fists, wheels on the ground), left standing where it was when the Drover's shift ended, and a resident gripped a mine cart and walked it 18 m (`CartPusher`: handles 0.44 m from the body all the way, wheels within 2 cm of the ground, a 26 degree slope followed). The ledger did not record offline (no `NetworkGameManager`, so `PushableLedger` has no instance). **Not run:** any client, `PlayerPushing` in Play (the offline player has no `PlayerPushing`), a save/reload of a pushed cart (`settlement-persist` now leaves a cart off its authored pose before the save and checks `PERSIST_CART_RESTORED`; the player build was not made, see Testing.md).

## Persistence

`PushableLedger.CaptureState` writes `{carts:[{Id, Position, Rotation}]}` only for carts off their authored pose (`null` when none: a restore of `null` puts every cart home). It is a **global** saver, so it needs no cart to exist when the file loads; each cart takes its entry in `OnEnable` (`PushableLedger.Shown`). Grips are not saved: after a load a pusher stands with empty hands and the cart where it was left. A regenerated settlement orphans its entries (the id is the hierarchy's).

## Gotchas

- **A static cart draws where it was baked while its collider moves.** The Deco prefabs were Batching Static, and each NomadSettlement prefab that nests one overrides the flags again on the instance; both were cleared (`NoNomadBuildingHoldsAStaticCart`). Re-flagging or re-exporting a cart brings the bug back.
- **A settlement places carts at 1.33 to 1.8 times the prefab**, so `Pushable.Shape` is measured in metres from the root along its axes (scale taken out). A pose is a position and rotation; anything that multiplies a local point by a rotation without that is wrong by the scale.
- **The shafts are wider than a Raxy's arms reach** (1.6 m at 1.4x; a Raxy's arm is 0.58 m). The hands close on the `Handlebar` between them, grips 0.14 m either side of its middle. A cart with no bar cannot be gripped by two hands.
- **A fixed cart's handles must be where a Raxy can reach**: a straight arm puts the palm about 0.68 m below the shoulder (shoulders are 1.8 m up on a `Drifter_RaxyPoncho`, about 2.1 m on `Raxy_handyman`), so a handle below roughly 1.4 m stops the palm short, silently (9 to 13 cm at the hover and food carts at 1.4x). Place those two at 1.8x; the handcart and mine cart reach at every scale a settlement uses.
- **The nose is derived**, not authored: handles toward wheels. Put the grips at the end the cart is pushed from, or it faces backwards (the food cart's shaft bars are at z -1.2, the mine cart's rim at +X).
- **The cart is solid to everyone but its holder** (`Physics.IgnoreCollision` for the grip) and does **not** collide with the world: it clips walls its pusher is stopped by. The settlement NavMesh keeps the cart's authored footprint as an obstacle.
- **Ground probes skip the pusher and anything on its own physics** (`WalkerGround`, the pusher's colliders registered with `SetSeenByGround`); a miss (chunk not loaded) keeps the last height.
- **`Pushable.Register` captures home**, not `OnEnable`: an edit-mode test registers by hand. A cart's id includes its sibling index, so a test must put the cart at index 0.
- **The Drover holds a lasso, not a cart (2026-10-04).** `RaxyToolLoadouts` has `("Drover", "Tool_Lasso", ...)`, so *Equip Residents* gives it the lasso in the hand and `ResidentHandsRule` stows it on the belt while it pushes; `PushableAuthoring` no longer writes the Drover's held item (it used to null it, and `ThePlayerTheSessionAndTheDroverAreWiredForCarts` pinned null; it now only rejects a `Carry_Cart_*` item).
- **A cart stands in the pen (2026-10-04).** `NomadAnimalPen` nests `Decor/Handcart__01` (`Deco_Handcart` at 1.4x, local to the pen at (-0.64, 1.09, -1.6) in `Decor`, yawed 270 degrees so its nose points east, into the pen, handles toward the gate), on the open dirt floor inside the fence: the Herd spots stand OUTSIDE the fence on a 0.05 m ledge with no room for a 3.7 m cart, and the goods piles are markers within 1 m of the free ground by the gate. Handles are 3.06 m (Herd_1) and 3.31 m (Herd_2) from the spots, inside `cartReach` (4 m, measured to the **body**, which a stand point may shift by up to `standSnapRadius`). Static flags are clear (`FreeNestedCarts`). A cart is placed like the three nested ones (child of `Decor`, last sibling, so no other id moves). Not seen working in Play: the Drover must still grip it, push it to its spot and leave it; the pen's own `Carry_Cart_Hand__01` prop (7.5 m away, carried by haulers) is untouched.

## Extending

- **A new cart:** a decoration prefab, a row in `PushableAuthoring.Carts` (grips on the end it is pushed from, a bar, wheel bottoms, an axle if it tilts), run the menu item, add it to `PushablePreview.Cases`, look at `Temp/PushablePreview/*.png`, add it to the test lists.
- **A resident who pushes:** `pushesCart` on its archetype and a cart within `cartReach` of its post. **Moving a cart on an errand** is not built: a chore stop would call `ResidentPushing.Sync(true)` between stops.
- **Not built:** collision of the cart with the world, a cart that keeps rolling, putting the `Carry_Cart_*` items to a use (`pushesCart` replaces the Drover's), a cart a player places at runtime.
