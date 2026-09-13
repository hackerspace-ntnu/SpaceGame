---
system: ResizerRemote
layer: items
summary: "A radio handset that drives a body up or down in size at 25 m, with a rim round whatever it is locked on"
paths:
  - Assets/Game/Scripts/Items/Artifacts/ResizerRemote
  - Assets/Game/Scripts/Items/Artifacts/InflatorNozzle/InflationScalar.cs
  - Assets/Game/Editor/AssetPipeline/ResizerRemoteBuilder.cs
  - Assets/Game/Art/Models/Items/resizer_remote.fbx
symptoms:
  - "a body lights up under the crosshair but the trigger does nothing to it"
  - "the rim round the target is a hairline at range and a slab up close"
  - "double-tapping Use fires twice instead of turning the dial"
  - "the handset hums and drains but nothing ever changes size"
  - "the polarity is back to the prefab default every time I reload"
  - "the resizer works on a crate but never on a player"
  - "the target rim stays behind after the item leaves the hand"
  - "a peer sees no whip, no beam and no lamp while somebody is resizing them"
reads_with: [Artifacts, Multiplayer, Persistence, Inventory, SupplyCharge]
updated: 2026-09-13
---

# Resizer remote

A handset with a whip antenna. Point it at a body and hold Use: it shrinks, or it grows, depending
on which way the polarity dial is turned. Let go and the body comes back on its own. Tap Use twice
to turn the dial. Read [Artifacts.md](Artifacts.md) first, and
[Artifacts/StatusEffects.md](Artifacts/StatusEffects.md) for the property it drives.

## Model

- **It owns where the signal goes, which way it runs, and what it costs — nothing else.** Scale,
  mass, the curve between them and the whole of the decay are `StatusKind.Inflated`'s: one signed
  scalar on the body, presented by `InflatedStatus`, replicated by `StatusReceiver`, derived
  identically on every machine. This item never touches a `localScale` and never touches a mass.
- **It is the inflator nozzle's second sign, not its second implementation.** Both drive the same
  scalar through the same `InflationScalar` arithmetic, and the rule about which bodies may be
  resized at all is `InflationScalar.CanResize` — one function, called by both.
- **Reach is paid for in magnitude** (`GDC-L1-SYS-0005`). The nozzle reaches 5 m and drives the
  scalar to its full ±1: buoyant at one end, leaden at the other. This reaches 25 m and stops at
  ±`signalStrength` (0.6), so nothing it touches floats away or shrinks to a speck. That trade is
  the whole reason the two items are a choice rather than an item and its better sibling — and
  because they share the property, a body one player is inflating is one another can pull down,
  with neither item knowing about the other.
- **The press moves nothing.** A carrier takes a moment to lock, so `Use()` only opens the channel.
  That is also what makes a tap free, and therefore available as the gesture that turns the dial.
- **Nothing about this is saved except the dial.** The target's size lives on the body, whose
  condition is deliberately never saved; the battery's level is written for free by
  [SupplyCharge](SupplyCharge.md).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `ResizerRemoteArtifact` | [ResizerRemote/ResizerRemoteArtifact.cs](Assets/Game/Scripts/Items/Artifacts/ResizerRemote/ResizerRemoteArtifact.cs) | `ToolItem`, `UseAuthority.Server`, `IsContinuous`. Aim, polarity, the per-tick signal, the battery, the state bag |
| `ResizerTargetHighlight` | [ResizerRemote/ResizerTargetHighlight.cs](Assets/Game/Scripts/Items/Artifacts/ResizerRemote/ResizerTargetHighlight.cs) | The rim round the aimed body. Owner's machine only; nothing sent, nothing saved |
| `ResizerRemoteRig` | [ResizerRemote/ResizerRemoteRig.cs](Assets/Game/Scripts/Items/Artifacts/ResizerRemote/ResizerRemoteRig.cs) | Whip, knob, lamp, beam, hum. Told what to show; decides nothing |
| `InflationScalar` | [InflatorNozzle/InflationScalar.cs](Assets/Game/Scripts/Items/Artifacts/InflatorNozzle/InflationScalar.cs) | `Presented` / `Progress` / `Pumped` / `CanResize`. Shared with the nozzle |
| `OutlineShell.BuildAtWidth` | [Items/Equipped/OutlineShell.cs](Assets/Game/Scripts/Items/Equipped/OutlineShell.cs) | The rim at a width the caller worked out, for a target at range |
| `ResizerRemoteBuilder` | [AssetPipeline/ResizerRemoteBuilder.cs](Assets/Game/Editor/AssetPipeline/ResizerRemoteBuilder.cs) | `Tools/Build Resizer Remote Artifact` — prefab, item asset, charge bar, network entry |

## Flows

**Transmit**
1. Owner's `OnRequestUse` runs the double-tap check, writes the aim ray (`P` origin, `R` rotation)
   and ORs the dial into `NetArg.B` bit 1. `Use`/`Present` open the channel; nothing resizes yet.
2. Every 1/15 s the owner's `OnRequestHold` rewrites the same two facts.
3. Server `Hold` traces its own ray from the payload, and moves the body's scalar toward
   `sign × signalStrength` by the seconds that really elapsed. The new value is derived from what
   the body is **presenting**, so two players compose instead of fighting.
4. `PresentHold` on every machine draws the whip, the knob, the lamp, the beam and the hum, and
   reads the reading off the replicated status. That is the target's warning.
5. Release, or a lost connection: `holdTimeout` (0.5 s) closes the channel, and `InflatedStatus`
   eases the body back to its authored size over the condition's own 6 s.

**The dial.** Two presses inside `flipWindow` (0.3 s, unscaled) flip `enlarging` on the owner. The
second press carries the new bit, so every machine learns it from the message it was going to get
anyway. Peers take the bit off the wire in `ReadPolarity`; the owner deliberately does not, or a
stale echo would undo a flip a frame after the player made it.

**The rim.** Owner-side `Update` traces the crosshair, resolves a `StatusReceiver`, and rebuilds the
shell **only when the target, the setting or the quantised width changes**.

## Multiplayer

- **Server authority**, because another body's size and weight is contested state two players can
  push at once (`GDC-L1-MP-0004`).
- **The client decides the sign and nothing else.** There is nothing to validate in the polarity
  bit — a client that forged it has asserted they turned a dial on their own item. What a client
  cannot forge is how far the signal goes: `signalStrength` and `secondsToFull` are read off the
  server's own copy of the prefab.
- **The handset is registered in `DefaultNetworkPrefabs.asset`** by the builder, because dropping a
  hotbar slot routes through `PlayerDropService` → `GameServices.World.Spawn`. Missing, it fails on
  clients only.
- Nothing per-frame goes on the wire. The beam, the whip and the rim are all local derivations.

## Persistence

- The dial's position is the one thing the instance becomes: `CaptureItemState` / `RestoreItemState`
  under `"resizer.grow"`. **Never rename that key** — it is in save files.
- The battery is a sibling `SupplyReservoir`, so `UsableItem` writes its fill into the same bag under
  `SupplyCharge`'s key with nothing to do here. `OnUnequipped` deliberately does not touch it: the
  bag is captured *before* unequip runs.
- `StatusKind.Inflated` is not saved, by design. It drains to zero within seconds, so a world loads
  everything at its authored size.

## Gotchas

- **`NetArg.A` is the slot code and bit 0 of `B` is `EquipmentController`'s active flag.** This
  item's polarity is bit 1, and it is `|=`'d in, never assigned. Assigning `B` on a hold tick clears
  the active flag and the server reads the tick as a release.
- **The battery must be asked once, on the press.** `Prime` arms and stamps the clock; `OpenChannel`
  runs per tick and does neither. Collapsing the two was a real bug in this file's first draft: it
  re-asked `CanStart` every tick (so a flat cell strobes) *and* re-stamped `lastSignalAt` every tick,
  which zeroes the elapsed seconds — the handset hums, drains and changes nothing at all.
- **`OutlineShell.Build` destroys and re-creates one renderer per renderer it traces.** That is
  nothing on a two-mesh socket and a great deal of garbage every frame on a player character.
  `ResizerTargetHighlight` rebuilds only when the answer changes, and quantises the width to 2 mm so
  camera bob does not count as a change.
- **A fixed outline width does not work at range.** `PackDragTint` inflates in world space, and
  `OutlineShell`'s own clamp tops out at 10 mm — tuned for props on the gear wall and a sub-pixel
  hairline on a body 25 m off. The rim's width here is a share of the hit distance, which is why
  `BuildAtWidth` exists.
- **A body that cannot be resized is deliberately left unlit.** A rim on something the trigger then
  does nothing to is worse than no rim: it is a promise the item does not keep. `CanResize` refuses
  anything kinematic or bodiless — which includes a mounted rider, a ragdolled body and a parked
  vehicle.
- **A `StatusReceiver` is never created by this item.** One added by the server alone resizes for
  the server and nobody else: the status message is addressed to the body's own relay, and a body
  with nothing subscribed drops it without a word. A thing that can be resized says so on its own
  prefab — and note that `PlayerCharacterNetworked.prefab` gets its receiver through the nested
  `PlayerCharacter.prefab`, not on its own root.
- **A peer's knob reads the prefab default until the first message.** `OnEquipped` sets `enlarging`
  from `startsEnlarging` on every machine, and a peer only learns the real position from a press or
  a hold tick. Cosmetic and self-correcting on the first pull of the trigger, but it does mean a
  bystander cannot read somebody's setting off their handset before they use it.
- **The whip and the knob are driven as `rest × delta`, never assigned.** `_exportlib.export` bakes
  no transforms, so an FBX node carries whatever the `.blend` gave it; an assignment flattens the
  part on the first driven frame.
- **The beam is not cleared per frame.** The hold stream is 15 Hz and the rig runs at the frame
  rate, so three frames in four carry no tick — a beam that went out on each of them would strobe.
  It goes out when the channel closes, which the artifact says explicitly in `Close`.
- **`maxUses` stays −1.** The battery is the whole cost, and a charge count beside it deletes the
  handset out of the inventory the first time it runs flat. There is therefore no
  `OnMaxUsesReached` override — it could never run.

## Extending

1. **Numbers** — `range`, `signalStrength`, `secondsToFull`, `flipWindow` on the prefab's
   `ResizerRemoteArtifact`; drain and refill on the sibling `SupplyReservoir`. A *third* item
   pumping this scalar needs a niche neither of the two existing ones has (`GDC-L1-SYS-0005`);
   "the same thing at a different range" is not one.
2. **Model** — `models/gear/resizer_remote.py` and its `_BUILD.md` in `Assets/Game/Art/Models/_Source~`.
   Never re-run the generator over the `.blend`; it is the source of truth.
3. **Prefab** — `Tools/Build Resizer Remote Artifact` rebuilds it wholesale from the FBX, so tune in
   the builder's constants, not in the Inspector. Then `Tools/Generate All Item Icons`.
4. **Route it in** — `startingItems` on `PlayerCharacterNetworked.prefab`, an `EntityLootTable`, a
   `TradeOffer`, or an instance in a chunk scene. Nothing is needed for the dev browser.
5. **Verify** — `Tools/Tests/Run EditMode Tests (headless)` for `NetworkPrefabRegistrationTests` and
   `HoldPoseTests`, then join as a real client, resize a second player from 20 m, and reload a save
   with the dial turned to shrink.
