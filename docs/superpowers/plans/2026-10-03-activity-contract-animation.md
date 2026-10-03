# Activity contract: animation, tool, place and seat agree

Written 2026-10-03. Nothing here is built. Unity was not connected when this was written, so every
finding is from code and asset data, not from a Play-mode run.

## Problem

An animation, the item in the hand and the place the body stands are three independent things.
Nothing makes them agree. Observed:

- Cooks mime unrelated jobs; farmers and miners swing tools that miss their target, from the wrong spot.
- Fidgets, greets and talk beats play over a held item, so the item waves around.
- Almost everyone walks with a bend at the hip.
- Many tools are held at the wrong rotation or with the hand in the wrong place.
- Residents sit in the air.
- Pushing a cart is the cart parented to the hands and waved about.
- A tool is never put away or put down; an activity that needs empty hands plays with the tool in hand.

## Findings that shape the plan

1. **Grips are per stance, not per tool.** The 89 tool prefabs carry six distinct `rotationOffset`
   values between them (one per stance and hold style), and every `positionOffset` is zero except the
   two carts. All 22 `Staff` tools (pickaxe, shovel, hoe, broom, fishing rod, spears, short bow, signal
   flag) hold one rotation; all 40 `Wield` tools (hammer, saw, trowel, ladle, wrench, knife, signal
   horn) hold one rotation; only the fleshing scraper differs. `GripFitter` solves the tool's *along*
   axis against one idle pose, so a tool whose face or balance differs from its stance's assumption is
   wrong, and nothing checks the rotation around the axis. `GripFitter.Residual` checks the along-axis only.
2. **The grip is fitted to an idle hold pose, not to the work clip.** The hoe is fitted to `Carry`'s
   idle arms; the farming, mining and hammering clips put the hand elsewhere, so the tool points off
   the moment the clip plays.
3. **Nothing in `BodyLanguage` knows what the hands hold.** `Choose` filters by posture, slot and loop
   only. Tags are about cues, not hands.
4. **`cook` is a pool of 15 unrelated upper-body loops**, re-rolled on every re-entry, including each
   time `ResidentAttention` releases the loop. Three of them (`Chop Food`, `Stir Pot`, `Wipe Surface`)
   have no posture limit, so they can play while walking.
5. **No locomotion archetype is assigned to any prefab**, so the hunch is not a walk variant. Cause not
   yet measured; candidates are the Upper Body layer weight with a hold pose, the hips offset on the
   rig, or the default walk clip.
6. **Sitting has no object.** `SettlementSociety` samples a ground height and `SeatedBodyFit` lifts the
   hips to it. Nothing is a seat, so nothing can be missing, moved or claimed.
7. **A pushed cart is a held visual**, scaled 3.5x and parented to the hand (`Carry_Cart_Hand`,
   `Carry_Cart_Hover`). It has no wheels on the ground and no weight.
8. Already built and kept: `Root_*` as the grip point, `GripFitter`, `CarryStance`, `HandToolRig`,
   `BeltMount`, `BeltCarrier`, `GarmentMounts`, `ResidentCarry`, the dropped-item system.

## The design

An activity declares what it needs. The resident prepares before the clip plays. If it cannot, the
activity does not start; it does not fall back to miming.

An activity's **needs** record:

| Part | Says |
|---|---|
| Hands | the tool kind (or `Any`) in a named hand, or `Empty` |
| Stance | what the spine and hips must do; whether the clip is safe while walking |
| Place | a station: stand point, facing, and a target (crop, ore node, stove, chair) |
| Contact | where the tool tip is at the clip's Contact mark, so the stand point can be solved |

The **prepare step** is: walk to the stand point, equip the needed tool from the belt or bag (or stow the
held item on the belt, or put it on the ground, if the activity needs empty hands), play the clip, then
stow or put down.

## Phases

Every phase updates its governing doc in the same change, adds tests, and is verified on a host **and**
a client, then through a save and reload (CLAUDE.md non-negotiables). Prefabs are edited on copies and
never while open in Prefab Mode.

### 0. Diagnose and audit

- Find the hunch. Render idle and walk poses of a resident in a preview scene (Upper Body weight, hips
  offset, default walk clip, each in isolation) and compare with the player.
- **Grip audit (the user reports many are wrong).** For all 89 tools, hold the tool on a real Raxy in
  the pose its stance names *and* in the work clip that uses it. Record per tool: along-axis residual,
  rotation around the axis, palm-to-`Root_*` distance, whether the working end reaches the contact point.
  Output one table. Start with the shared-rotation groups (`Staff` 22, `Wield` 40, `Hang` 17) because
  one wrong value there is 22 to 40 wrong tools.
- List every cue pool with slot, playback, posture and hands, so the next bad tag is visible.
- Exit: the hunch has a measured cause; every tool is graded right / wrong / needs per-tool values.

### 1. Hands and grip

- Per-tool grip: each prefab states its own along and face axes (the roster already has `ItemAlong` and
  `ItemFace`) and a palm offset along the shaft where the hand belongs; the fitter solves per tool and
  also checks rotation around the axis. Re-fit against the work clips, not only the idle pose.
- One canonical hand socket per humanoid rig, matched to the Raxy hand, so a tool fitted once fits
  every rig. Astronaut hands are 1.7x the human's (see Worn Item Hand Fit) and need an explicit scale.
- Actions gain a **hands** field (`Free`, `Tool kind`, `Either`) and an arm. `Choose` skips an action
  whose arm is occupied. Fidgets, greets, talk beats and gestures default to `Free`.
- A held-tool state exposed from `EntityEquipmentController` to `BodyLanguage`.
- Exit: no gesture or fidget plays on an arm holding an item; the audit table is all green.

### 2. Seats

- `Seat` component and three prefab variants (`Coll_Deco_Seat_Clay`, `_Pillow`, `_Wood`): sit point,
  facing, foot drop, one occupant, claim and release. Networked, saveable by identity, movable,
  placeable. The models are not in `Assets/` yet and must be exported first.
- Residents: a `seated` spot claims a free `Seat` and sits on its sit point, facing it. No seat, no sit.
  A seat that moves ejects the sitter. The claim replicates as a seat id, derived on every machine like
  `ResidentPresence`'s place.
- Players: an interact prompt on a free seat seats the player (`NetworkedTeleport`, owner-gated),
  releases on jump or interact.
- Settlement decoration batches place seats at every `seated` spot; an editor check fails any seated
  spot with no seat.
- Replaces the sampled `SeatSurfaceY` and `SeatedBodyFit`'s height lift.

### 3. Prepare protocol

- The needs record and the prepare step (equip, stow on belt, put down, pick up). Put-down reuses the
  dropped-item system; a put-down item is a world entity and persists.
- Failure is explicit: no tool, no belt room, no seat means the activity is skipped and logged once.
- `ResidentPresence` holds the cue only after the prepare step reports ready, and releases through the
  stow step. Attention pauses keep the same pick (see phase 4).
- Server decides; every machine derives the held item and the shown state.

### 4. Stations

- Measure each work action's tool-tip position at Contact (and the root offset) once, offline; store it
  on the action as its reach.
- A station gives a stand point `reach` away from its target along its facing, so the tool lands on the
  target. Farm plots, ore nodes and stoves get targets.
- Split `cook` into station cues (pan, wok, grill, prep, wash). A spot's `holdCue` matches its props.
  The pick is made once per spot from a seed and kept across attention pauses.
- Any loop with no posture limit gets one, or is retagged `Standing | Seated`.
- Exit: a worker's tool tip lands within a tolerance of its target at the Contact mark, measured.

### 5. Push and carry

- Real carts: `Deco_Handcart`, `Deco_Handcart_Hover`, `Deco_FoodCart` and `Deco_MineCart` get a
  `Pushable` component with two handle points, a ground contact and a forward direction.
- A pusher (player or resident) grips both handles; the cart follows the body, wheels on the ground,
  turning with heading. Networked and saveable; releasing the handles leaves it standing.
- `Carry_Cart_Hand` and `Carry_Cart_Hover` become the pick-up path or are retired, once their uses are listed.

## Progress (2026-10-03)

Done and compiling (not yet run in Play mode, on a client, or through a reload):
- Phase 1, gestures: `BodyArms`/`BodyHands`, `CharacterCue.needsFreeHands` (36 cues), `CharacterAction.ArmsUsed`, `BodyLanguage.Choose(..., occupied)`.
- Phase 4, partial: 12 stationary work loops limited to `Standing|Seated`; `BodyLanguage.Hold(cue, salt)` makes a resident's pick stable per spot.
- Phase 0, hip bend: measured, see HumanoidAnimation.md Gotchas. The cause of the walking stoop was the hold clip's spine overriding the walk's (the Upper Body mask has the Body part): fixed (below). The Raxy avatar's pelvis retarget (idle 81 degrees empty-handed) is separate and unresolved.

- Phase 3, hands only (2026-10-03): `ResidentHands` + the pure `ResidentHandsRule` own the resident's hand slot. Work, chore, patrol and trip draw the archetype's tool; everything else (walking and all travel, errand legs between spots, sitting, hearth, stroll, amble, talking, sleeping) stows it on the belt so gestures and fidgets can play and the walk is upright (hips-head 89 degrees with no hold pose vs 72 Carry and 46 Ready). A carried errand item wins the hand. The held loop waits for `ResidentHands.Ready`, and a work activity with a missing or undrawable tool is skipped and logged once instead of miming. `BeltCarrier.CanStow` refuses a tool with no `BeltMount` or no anchor left, which then stays in the hand. Verified live in Play (host): stow/draw cycle, gesture gate, 70 residents. Every tool but the two carts can be stowed (87 of 89, 2026-10-03): `BeltHangs` derives where each hangs from its stance and bounds (short tools on a hip head down, vessels by the handle, long tools slung on the back, `BeltMount.slots` keeps a long tool off the hips, `BeltSeat.Plan` is the shared planner); all 87 looked at on contact sheets (host render only). Left in the hand: the Drover's cart and anyone on `Drifter_RaxyClassic` (no belt); a few belt items of two-long-tool professions have no anchor and stay in the bag unseen (HandTools.md Gotchas). Not done: put-down on the ground, equipping from the bag for a non-archetype tool, client and reload runs.

- Phase 2, seats (2026-10-03): `Seat` + `Deco_Seat_Clay/Pillow/Wood`, `SeatPlacer` (30 seats on every sit spot), `ResidentSeating`, `PlayerSeating`, replicated seat ids, `SeatTests`. Replaces `SeatSurfaceY` and the clamped hip lift. See Seats.md. Residents observed sitting in a host-offline Play run; not run: client, player in Play, save/reload, runtime-placed seats.

- Phase 5, pushable carts (2026-10-03): `Pushable` on `Deco_Handcart`, `_Hover`, `Deco_FoodCart`, `Deco_MineCart` (handlebar, grips, wheel contacts, kinematic body, no static flags there or on the six nomad prefabs that nest one); `CartPusher` poses the cart from the pusher's body on every machine and reaches both arms for the handlebar (`ArmReach`, no hold pose: the `Push` clip raises a Raxy's hands above its head); `CartPoseSolver` (wheels on the ground, shafts lifted to the hands); `PlayerPushing` (NetMsg 122/123, speed cap, jump off) and `ResidentPushing` (`pushesCart` on the archetype, `ResidentPresence.cart`); `PushableLedger` (NetworkList on `NetworkGameManager` + global saver `pushables`) remembers where a cart was left. The Drover holds no cart item any more. See Pushables.md. Verified in edit mode: 58 tests (solver, arms, claim, ledger and save round trip, a real Raxy and the astronaut gripping every cart, residents), preview renders in `Temp/PushablePreview`. Not run: Play, a client, save/reload of a real pushed cart. Left: no cart stands by a pen post so the Drover works empty-handed; `RaxyToolLoadouts` still lists `("Drover", "Carry_Cart_Hand")`; the `Carry_Cart_*` items, their `carryItems` entries and the pen's `Carry_Cart_Hand__01` prop are not retired; carts do not collide with the world; an errand that moves a cart is not built.

- Straight hold poses (2026-10-03): `Carry` and `Ready` are arms-only on a new `Hold Arms` layer (`HoldPose.armsOnly`, `Arms.mask`); walking hips-to-head is 88.2 degrees with either, the same as empty-handed (was 71.7 and 45.7; standing 81.3 = empty-handed, was 87.5 and 61.5). The aimed styles keep the torso on purpose and still bend a walker (75, 71, 46; `Push` 79). `ArmsOnlyHoldTests`. Edit mode only: no Play, no client, no reload.

- Phase 0 grip audit and phase 1 grips (2026-10-03): all 89 tools audited ([hand-tool-grip-audit.md](../../AI/systems/hand-tool-grip-audit.md)). Findings: the Raxy's grip frame had fallen to the forearm path (three fingers, no little finger) and sat 15 cm short of the fist, so every tool lay along the wrist; the fit rig kept the hold clips' hips, so every non-`Carry` fit was made against a leaning body (`Wield` 10 degrees off, `Push` 46); `Lantern` and `SignalFlag` raised the free arm. Fixed: ring-finger fallback in `HandGripFrame`, `HandToolRig.FistCentre` as the canonical hand socket for tools, hips restored in the rig, `Staff` leans 30 degrees (wrist 36 instead of 49), the two pose overrides dropped, `GripShift` (hand position along the shaft) and the face-axis residual added, `HandToolGripTests` extended. 87 of 89 tools are right by the audit; the two carts (`Push`, hands at head height in the faithful body) are superseded by the pushable carts above. Not done: re-fitting a grip against a work clip (one grip cannot serve every clip, see the audit), the astronaut seating a tool at its own palm, the left-hand mirror, Play, client and reload runs.

- Phase 4, stations (2026-10-03, edit mode only; [Stations.md](../../AI/systems/Stations.md), [StationTable.md](../../AI/systems/StationTable.md)): done for every kind of spot, not only cooking. 12 station cues (stir, cookpan, cookwok, grill, chop, wash, blend, plate, season, wipe, weave, rummage) plus the job cues trimmed to exact loop lists and stripped of their `work` fallback; each kind of spot holds one cue (`SpotUse.holdCue`, `SettlementSpot.stationCue` per prop: 8 prop overrides); `CharacterCue.Tools` / `BareHands` and `ResidentHands.Station` draw the tool a station's clips are made for; `CharacterAction.Reach` measured for 31 actions (the hand's grip point, the tool end estimated for strikes) and `StationStand` derives the stand point of a spot with a target; the station table is generated from the data and tested. No clip yet (the resident stands): pens, shrines, gate and tower, errand stops. Not run in Play, on a client, or through a reload. Follow-ups: `RaxyToolLoadouts` rows for Brewer and Apprentice; re-measure reach once the grips are refit to the work clips; no pan, wok or spatula tool and no wok, basin or blender spot exists.

Not started: held-tool state beyond the animator, the rest of the prepare protocol (needs record, put-down).

## Open questions

- Should a resident put a held tool on the belt before sitting? Assumed yes.
- Retire the `Carry_Cart_*` items, or keep them as a way to pick up a cart? Not decided: the pen prop and `carryItems` still use `Carry_Cart_Hand`; see Pushables.md.
