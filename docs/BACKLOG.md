# Bug & Polish Backlog

Reported 2026-09-06. Each entry has a stable ID; group prefixes mark the area.
IDs are never reused — when an item is fixed, mark it done rather than deleting it.

| Area | Prefix | Count |
| --- | --- | --- |
| Worn gear & hand placement | `GEAR` | 2 |
| Item models & visual state | `MODEL` | 2 |
| Inventory presentation | `INV` | 5 |
| Interaction & HUD | `UI` | 3 |
| Ship interior wall | `SHIP` | 2 |
| Animation & ragdoll | `ANIM` | 3 |
| Artifact behaviour | `ART` | 4 |
| Enemy AI & pathfinding | `AI` | 1 |

---

## GEAR — worn gear & hand placement

### GEAR-01 — Artifact scanner sits on the wrong side of the wrist — **DONE 2026-09-06**
The scanner gauntlet is mounted on the wrong side of the arm. Rotate it 180° and move
its attachment point so it sits on the opposite side of the wrist.

Fixed as a half-turn about the arm axis — `GauntletFit.rollDegrees` 180 on
`ItemScanner.prefab`, which swings the console to the far flank at the same distance
from the bone and moves nothing along the arm. The number lives in
`GauntletReseat.ItemScannerRollDegrees` because the reseat tool rewrites that field;
the `.blend` was not touched. Not yet seen on an arm in play.

### GEAR-02 — Portal sprayer is not visible in the hand — **DONE 2026-09-06**
The portal sprayer does not line up with the hand and is effectively invisible while
held. Seat it in the hand properly (grip point, hold size, orientation).

*Fixed:* the size was the whole of it. An `ItemScaleLadder` bracket is a **reach**;
`holdSize` is a **longest axis**, and on a fire extinguisher gripped by the handle on
top of it those are not the same edge — the Gun bracket's 1.25 m hung a 1.25 m bottle
out of the fist to below the knee. Now `BigTool` **0.73**, and `HoldStyle.OneHanded`
(there is no second handle on the model; the two-handed pose folded the empty left arm
across the chest in front of the gun). Grip point and orientation were already right and
were left alone. `packSize` is authored, so the backpack, gear wall and world sizes did
not move. See `ItemScaleLadder.VesselWhy`.

---

## MODEL — item models & visual state

### MODEL-01 — Battery and oxygen tank do not show fullness — **DONE 2026-09-06**
Both must display their charge/fill **on the model itself**, readable in the world:
- a progress bar that fills 0–100 like a phone battery icon
- colour ramp with the level: green when full → orange → red when low

Both now carry a real bar. `OxygenGearBuilder` generates a dark `Gauge_Track` and a
`Gauge_Anchor`/`Gauge_Fill` pair onto each prefab, measured off the model's own emissive
gauge submesh; `SupplyGauge.Paint` scales the anchor to the charge and ramps the fill
through the palette's indicator triad — `Mat_Emissive_Green_CRT` → `Mat_Emissive_Amber`
→ `Mat_Emissive_Red_Warn`, two lerps meeting at 50%. The charge itself already existed
and is untouched.

The **bar** is deliberately the reading and the colour only confirms it: green→red is the
axis a colourblind player loses, which is exactly what the old flat-tint gauge encoded it
in (`GDC-L1-UX-0003`, `GDC-L1-UX-0006`). The battery had no working gauge at all before —
its five-bar ladder is baked into the mesh with three permanently lit, so the track exists
to *cover* that, and the bar is measured off the lit three and mirrored to all five about
the gauge mesh's middle. Measured off the built prefabs: tank 56 mm centred on its plate,
battery 247.8 mm covering the whole ladder.

Reads the same in all three places, which cost the most of the work: the live item, the
bottle filling in the plant's collar, and every copy on the pack mat and the ship's gear
wall — the last two are script-stripped display copies, so the bar is bound by child name
rather than by component, and the pack now paints each copy from its *placement's* charge
instead of the prefab default. Neither `.blend` was touched. New doc:
`docs/AI/systems/SupplyGauge.md`. Not yet seen in play.

### MODEL-02 — Lasso loop is a perfect square — **DONE 2026-09-06**
The lasso's end loop reads as a hard square and looks unrealistic. It needs an actual
rope-loop shape.

Clarified on the report: not square but a **perfect circle**, and it "doesn't connect to
the rope properly". Both were true and they were one bug — `LassoLoop` drew 28 segments of
`cos`/`sin` centred on exactly the point `LassoArtifact` drew the cable to, so the rope
died in the middle of the hole with an unattached hoop around it.

Fixed by making the loop a lariat with a knot. The profile is now pinched into a throat at
`phi = 0` and bellied opposite it (`hondaPinch` 0.55, `belly` 0.12 on `Lasso.prefab`), it
sags once the spin winds down (`droop` 0.2, so the collar hangs and the twirl does not),
and `Twirl`/`Fly`/`Ride` each **return that knot's world position** — which is what the
four rope-drawing call sites now pass as the cable's far end. `LassoRope.Simulate` also
pins its ends outside the fixed-90 Hz substep loop, or above 90 fps the join lags a tick
behind a knot moving at 620°/s. On the ridden catch the drawn rope's length has the
collar's own radius subtracted, so ending it a radius nearer does not buy it free slack.

The throat costs mouth: the largest circle inside the drawn hole is 84% of `Radius` while
the catch sphere is still the full `Radius`, so the catch errs generous. `ThroatPeak()`
normalises the pinch precisely so sharpening the knot cannot silently shrink the loop while
`LassoAim` goes on drawing the old ring. Three tests pin it in `LassoTests.cs`
(`TheRopeEndsOnTheLoopAndNotInTheMiddleOfIt`, `TheLoopIsPinchedAtTheKnotAndOpenOppositeIt`,
`PinchingTheKnotDoesNotNarrowTheMouth`). Shape chosen by rendering the pinch × belly grid,
and the numbers verified against a transliteration of `Offset`. **The EditMode tests have
still not been run** — the Editor is open and the Test Runner needs it. The blocker
originally recorded here (the editor assembly broken by in-flight ANIM-02 work) is gone:
ANIM-02 landed 2026-09-07 and both `Assembly-CSharp` and `Assembly-CSharp-Editor` compile
clean, re-verified through Bee's response files. Not yet seen thrown in play.

---

## INV — inventory presentation (backpack + wall)

### INV-01 — Laser staff is rotated wrong — **DONE 2026-09-06**
In the inventory it pokes out at 90°. Rotate it so it lies flat like the other items.

Authored data, not a seating bug: the pack draws every item with its own up still up
(`ItemFootprint.FootprintOf` *is* `(size.x, size.z)`), and the FBX importer left the
staff's length on prefab +Y — so it reserved a 2 × 1 cell stub and stood 1.42 m out of
the mat. Fixed as `LaserStaffBuilder.LieDown`, `Euler(0, 90, 90)`, composed with the
importer's own −90 X rather than assigned over it. That cycles the axes so the length
lands on +Z, crown forward, and the **thicker** cross-section stands up — which is the
whole decision: 0.1199 × 0.1030 m across a 1.5468 m mesh is 1.163 cells by 0.999, so
this choice gives **1 × 15** and the 18 × 1 `LongGoods` lash line takes it square on,
where the other gives 2 × 15 and evicts it onto the rack. That lash line was cut for
this staff and nothing else, so the pack has been waiting for this. The rack still
takes it too, ski-fashion (clamped to 9 × 1); every other face refuses it, which is the
honest price of a pole that lies down.

The pose in the hand is unmoved (verified at 0.0000°): `rotationOffset` is set to the
**quaternion** inverse, `Euler(-90, -90, 0)`. `-LieDown` — the negated euler, which is
right for `JumpingRodBuilder`'s single-axis turn and looks like the idiom — is 120° away
from the real inverse here. Muzzle, collider and grip all re-measure off the laid-down
bounds. `PackOrientationTests.LaserStaff_{LiesDownAndFitsTheLashLine,KeepsItsPoseInTheHand}`
pin both halves; the 1-cell width is won by 0.1%, so any move in `PackScale.Factor`,
`PackGrid.Cell`, the staff's `holdSize` or the mesh fails there loudly.

Multiplayer and save/load both come out free, and both were checked rather than assumed.
It is authored prefab data, identical on every machine, and the prefab is still in
`DefaultNetworkPrefabs`. An existing save carries a *spot*, not an orientation, so a staff
stowed as the old 2 × 1 stub no longer fits where it was — `AdoptPlacements` then falls
through to `StowAuthored`'s first-fit exactly as it is designed to, and the staff lands on
`LongGoods` at yaw 90 rather than being lost. Verified against the rebuilt prefab and the
rig's real faces in the Editor; not yet seen on the mat in play.

### INV-02 — Net gun is far too large — **DONE 2026-09-06**
Much too big in both the backpack and the wall. Shrink it substantially.

Fixed by giving the prefab an `ItemGrip.packSize` of **0.63 m** — the model's own
0.629 m rounded up to the next 0.09 m webbing pitch. It was following `holdSize`,
and the Gun bracket's 1.25 m is 2.0x life size for a capture pistol, so the mat, the
gear wall and the sand all drew it 1.31 m long: **7 × 14 = 98 of the rig's 255 cells**
for one gun. It now measures 0.288 × 0.465 × 0.661 m — **4 × 7 = 28 cells**, and a
dropped gun shrinks 2.39 m → 1.20 m with it, because `ItemWorldScale` is derived from
what the wall draws. The roster's usual extra cell of margin is deliberately left off
for the same reason the battery leaves it off: 0.72 measures exactly the leaf's eight
cells, where a float division landing on an integer decides at random whether the item
fits at all. Nothing about the gun in the hand moved (`holdSize` is still 1.25). The
number lives in `NetGunBuilder.PackSize` as well as on the prefab, because the builder
replaces that prefab wholesale; `PackSizeTests` and `NetGunWiringTests` pin both halves.
This reverses a decision written down twice — "guns stay at the anchor on the mat,
because big gear goes on the rack with overhang" — which holds for a launcher whose
true size really is about a metre and not for this. Measured off the shipped prefab
through `ItemFootprint` in the Editor; not yet seen on the mat in play.

### INV-03 — Ruin scanner is too large — **DONE 2026-09-06**
Should be roughly **0.6×** its current inventory size.

Fixed by giving the prefab an `ItemGrip.packSize` of **0.225 m**, against the 0.389 m
the artist built it at — 0.58×, and the first gauntlet to diverge from the family's
"drawn at true size" default. On the mat and the ship's gear wall it now costs
**2 × 3 = 6 cells** instead of 4 × 5 = 20. Not the exact 0.6× (0.2334), because the
binding axis is the device's 0.301 m **width**, not the length `packSize` names: it
crosses into a third cell at 0.2326, so 0.2334 would buy a whole extra column for 0.3%
of overflow. The number lives in `GauntletReseat.RuinScannerPackSize` as well as on the
prefab, because the reseat tool rewrites that field; `PackSizeTests.ScannerWhy` and
`PackOrientationTests.RuinScanner_StandsDeckUpAndCostsTwoByThree` pin it. Nothing about
how it sits on the arm moved (`holdSize` is still 0). A dropped scanner also shrinks,
0.572 m → 0.429 m, because `ItemWorldScale` is derived from what the gear wall draws.
Verified by arithmetic against the shipped FBX's own bounds; not yet seen on the mat in
play.

### INV-04 — Cannot rotate items on the inventory wall — **DONE 2026-09-06**
On the wall, an item that has no valid spot left (shown red rather than green) must be
rotatable by clicking — the same behaviour the backpack already has.

A press on red at the wall now turns the held item a quarter instead of sending
nothing, which is the backpack's own rule (`WallAimController.Turn`). The wall had
deliberately refused to do this — its comment argued the wheel was already the turn and
that one button should not mean two things — and that is the decision reversed: the
cells under the crosshair have already said which of the two the press will do, so the
button is never ambiguous when it is pressed, and one inventory should not need two
gestures (`GDC-L1-UX-0004`). The wheel still turns the item without a refusal. The
crosshair now reads `Will not fit — RMB / LMB: turn` when there is a turn on offer,
because the wall's ghost sits under a crosshair the player is aiming rather than a
cursor they are already moving.

An item with **no** turn to offer sends nothing and stays put: a shape symmetric under a
quarter turn lands on the cells it was refused on, and a shape library row with
`allowRotation` off is straightened back to zero by the server on the way in, so turning
it would draw a ghost the placement then contradicts. That second half is why the wall
consults `allowRotation` where the backpack's click deliberately ignores it — the pack
turns what is in the *hand*, which nothing straightens. The shared question is
`PackShape.QuarterTurnChangesCells`, extracted from `PackHandController`, with the
item+library overload on `PackShapes`. Pinned by
`PackShapeTests.OnlyAShapeThatWouldLandOnDifferentCellsOffersATurn` and
`WallInventoryTests.ARefusedPressOffersATurnOnlyWhenThereIsOneWorthMaking`; 145 of the
`Wall|Pack|Backpack` EditMode tests pass, the 2 failures being INV-03's in-flight work
on the ruin scanner. Not yet seen at the wall in play.

### INV-05 — Backpack opens in the wrong state and the camera is wrong — **DONE 2026-09-06**
Pressing **B** should spawn the backpack below the player **closed**, not open.
The default camera also needs work: higher position overall, and in the closed state
slightly higher again with a better angle.

The rig now lands shut. `BackpackController.StartDeploy` calls the new
`BackpackObject.ResolveRackForDeploy` on the frame the toss begins, which is free:
a folded pack is already at the angle the rack holds, so the leaf and both wings turn
**0.00°** while the kickstand panel does its full 25° — measured on the shipped
`ExpeditionRig`, not just asserted. The click on the board (or R) is what lays it flat,
and both already existed. The closed/open flap now survives a save as one `racked` bool.
Landing spot unchanged at 2.96 m in front, per your call.

Camera: `HeightUp` 1.5 → **1.9** over the flat mat and **2.2** over a shut pack, lerped
on the flap's own `RackProgress` so the lens rides the board instead of jumping. Pitch
had to follow the height — 38° → **44.7°**, from the new `PitchForHeight`, which keeps
the optical axis on the same spot of the mat, so it reads as the same shot from higher
up. Raising the lens alone would have put the mat's front lip 5.2° below the bottom of
the frame; as shipped there is 1.5° of headroom against the old 0.6°. Not yet seen in
play — the numbers are the ones most worth re-tuning by eye.

---

## UI — interaction & HUD

### UI-01 — Place/pick-up bindings are inconsistent — **DONE 2026-09-06**
The lantern and every pickupable item should use the same bindings:
**left click = place down**, **right click = pick up**.

Only one half was wrong. Left click already placed — `PlaceableItem` is a `UsableItem`, so the
lantern goes down on `Player/Use` like every other held thing — and loose salvage already came up
on right click, because `PickupableItem` is a plain `IInteractable`. What diverged was the thing
the player had *just put down*: `PlacedObject` implemented `IRetrievable`, and `IRetrievable` was
bound to a **key of its own, Q**. So the two halves of one verb sat on LMB and Q, and which button
picked a thing up depended on whether that thing had been placed or found — a fact the player
cannot see. Worse, **Q is also the left gauntlet's trigger** (`Player/GauntletLeft`, since the
2026-09-02 gauntlet rework): pocketing a lantern fired whatever was worn on the left forearm at
the same time, and neither action knew about the other.

Retrieval now rides the interact button. `Interactor.Interact` operates what it can operate and
otherwise takes the thing back, decided by one new static — `Interactor.PressPicksUp(interactable,
canUse)` — asked in exactly two places: the press, and `IsActionable`, which is what lights the
crosshair. That is deliberate: the same question drives the prompt and the click, so the crosshair
can never offer a pick-up the press then refuses. `Player.Retrieve` is no longer read by the
`Interactor` at all, and `PlayerInputManager.OnRetrievePressed` is gone with it. The `Retrieve`
action itself stays in the input asset because `SaddleQuickRelease` still uses it for the
*unaimed* strip-the-saddle gesture from up close, which is a different verb and out of scope here.

**The primary verb wins the button.** A placeable that *does* something therefore puts that verb
on LMB (`ISecondaryInteractable`) — the same button that placed it — rather than taking the
interact press back and leaving the player nothing to press to get the item home. Nothing in the
project is in that position today (`PlacedObject.CanInteract` is `false`, and no subclass exists),
so this cost no migration; it is written down in `Placeables.md` Gotchas and in `PlacedObject`
itself because the failure is silent. The saddle is the one thing that is both an `IInteractable`
and an `IRetrievable`, and both meanings of the press call `TakeOff`, so it does not care which
one wins.

The words moved with the binding: `InteractionPromptResolver.RetrieveSuffix` ("   Q: pick up",
appended after "RMB: interact") is now `RetrievePrompt` — "RMB: pick up", used **instead of** the
default, because one press cannot honestly be offered twice on one line. `PlacedObject.Prompt`
reads that same constant rather than keeping a second copy of the string, and `SaddleRemover` went
from "E or Q: take saddle off" to "RMB: take saddle off" (its "E" had been stale since interact
moved to right mouse on 2026-09-03).

Design: this is `GDC-L1-UX-0004` (honour the convention the player already has — right-click *is*
"take that", learned from the salvage they meet in the first minute) and `GDC-L1-UX-0005` (input
space is scarce, and Q was carrying two unrelated verbs at once; the fix removes a binding rather
than adding one). Against those sat the reason the split existed, recorded in `IRetrievable`: one
key for both makes the player guess which way the next press goes. That reasoning is sound and is
*preserved* — the two verbs are still on two buttons, LMB and RMB — it was the mapping of which
verb to which button that was arbitrary.

**Multiplayer:** unchanged and unweakened. Retrieval still goes `PlacedObject.Retrieve` →
`NetMsg.RetrieveRequest` (102) → server, which gives the item and then despawns; only the local
key that starts it moved, on a path that was already local-to-the-presser. Two clients clicking
one crate on the same frame still produce one crate. **Persistence:** no new state — a binding is
not state, and the placed object's own record (`TransformSaveable` + prefabId) is untouched.

Verified: `Assembly-CSharp` and `Assembly-CSharp-Editor` both compile clean through Unity's own
Roslyn against the Bee response files, with the new test file appended to the editor one. Seven
new tests in `PickUpBindingTests.cs` pin both halves — which of the two meanings a press takes, and
that the prompt names the same button. **The EditMode suite has not been run**: the Editor is
contended by six other sessions and the editor assembly is intermittently broken by in-flight work
that is not mine. **Not yet clicked in play, and not seen on a client** — the change is
client-symmetric by construction (same code path, same message) but that is an argument, not an
observation.

### UI-02 — Leashes can be released by anyone — **DONE 2026-09-06**
Players can click a leash they are not attached to and free it. The interactable
should live on the connected area as a whole (both ends). If *you* are the one
attached, releasing must go through the struggle system instead of a click.

Fixed with one question — `Leash.Restrains(GameObject)` — asked in `LeashArtifact`
at aim time *and* again in `Present` on every machine, so the refusal is not one the
clicking client is trusted to have run. It matches **both** ends (the untie travels
addressed to the anchor nearest the click, so a check that followed the address
would let a captive walk to the far knot) and uses `Transform.IsChildOf` rather than
an identity test, because a rope thrown at a downed player anchors to the ragdoll
**bone** it hit, not to their root. The captive's only exit is now the struggle
`LeashedBody` already runs; cutting somebody *else* loose is untouched. The same rule
covers the hogtie's `Tie` untie, where a ragdoll camera can look along its own limbs.
Three tests in `LeashConstraintTests`; all three scenarios also run green against the
live compiled code in the Editor (far end True, bystander False, ragdoll-bone True).
**Not yet seen in play**, so the refusal itself has not been clicked by a person. A
refused click is silent; a "you must struggle" signifier is noted in `LeashSystem.md`
Gotchas and is not built.

### UI-03 — Interact prompt should revert to the old layout — **DONE 2026-09-06**
Go back to the previous interact UI, but restyled with the new HUD's colouring
(blue this time) and kept as a sticky UI component fixed in place.

The old layout is `InteractionPromptUI`, deleted on 2026-09-04 (commit `f3d8a158`, the gauntlet
rework) when `VisorReticle` absorbed it: a panel pinned at a **fixed fraction of the screen**,
0.5 / 0.34 from the bottom-left with a top-centre pivot, 340 px wide, holding the label, a
right-aligned value, the prompt line and a fill bar. What replaced it kept all four rows and threw
away the pinning — `InfoBoxAt` placed the box above the bracket every frame, so the only element
on the visor that is pure *text* moved to a new spot on every target, flipped from above the
bracket to below it near the ceiling, and slid sideways with every head turn. Reading a name and a
prompt is the one thing on that layer that takes longer than a glance, and text you have to find
before you can read it is text you read late (`GDC-L1-UX-0003`: rank by position and let the eye
learn one place).

Recovered from git rather than re-invented: `git show f3d8a158^:…/InteractionPromptUI.cs` is the
source of the anchor and the width. `VisorReticle.BuildInfoBox` now sets
`anchorMin = anchorMax = infoAnchor` (new serialized `Vector2`, default **0.5, 0.34**) with pivot
(0.5, 1) and `anchoredPosition` zero — an anchor *fraction*, so it holds its place on any aspect
ratio — and `infoWidth` goes back to the old **340** from 300. `InfoBoxAt` and the `infoGap` field
are deleted, and `LateUpdate` no longer writes a position: the box is placed once, at build.

Deliberately **not** reverted: the bracket, and the type ramp. The bracket answers a different
question — it says *where*, which is why it still moves and still snaps onto the arbitrated
collider — and only the box says *what*, which is why only the box is nailed down. The old panel's
26/22/20 pt text came from a type ramp that no longer exists; the rows keep `VisorStyle.BodySize`
(17) and `MicroSize` (13), which is what "restyled with the new HUD's colouring" asks for and what
`Visor.md` requires of anything drawn on that layer. That is also why `infoHeight` stays at **76**
rather than returning to the old 96: 96 px was sized around 26 pt type, and the visor's smaller
ramp fills 76 exactly (label band 24, prompt 18, a 6 px track 12 px off the bottom). The colours
were already blue and are untouched — `Ink` label, `InkDim` prompt and value, `InkFaint` backdrop
and track.

**Multiplayer:** none. The visor is pure local presentation and adds nothing to the wire.
**Persistence:** none — a panel position that is a constant in code holds no state worth saving;
the only visor value that persists is the H detail level, which is untouched.

Verified: `Assembly-CSharp` compiles clean through Unity's own Roslyn, and `VisorReticle` is
`AddComponent`-ed at runtime by `HelmetHUDController`, so the changed serialized defaults are the
ones that actually take effect — no prefab holds an override of them (which is the usual trap; see
INVARIANTS). **Not seen on screen.** The Editor is contended, so nothing about this has been
looked at: the anchor, the 340 width and the 76 height are the numbers most worth re-tuning by
eye, and `infoAnchor` is serialized precisely so that can be done in the Inspector.

---

## SHIP — ship interior wall

### SHIP-01 — Ship parts on the interior wall are too small and wrongly shaped — **DONE 2026-09-06**
Make the parts much bigger, and change their shapes so they actually resemble the
objects they represent instead of generic blocks.

Both halves were one cause, and it was the **footprint on the grid**, not the meshes.
`ShipPartItemBuilder` stamped every module a solid **9 × 9** row in `PackShapes.asset` —
the backpack rack exactly, so hauling one cost your whole rack — and an authored row
beats the derived silhouette outright, so a spar, a plate and a stubby turbine were the
same rectangle. It also gave all seven one flat `packSize` of 0.80 m, which the wall
draws at 1.53 m: every module identical, and shorter than the bazooka beside it.

The rows are **deleted** (`ClearPackShapes` — a removal, since the builder is re-runnable
and they were already on disk), so each module is shaped by its own outline again, and
`packSize` is now a four-rung **haul ladder** on `ShipPartItemBuilder.Haul`: `Spar` 2.10
(NuclearMotor, 4 × 24 cells, drawn 4.01 m) · `Long` 1.70 (AntiGravity 5 × 19,
LongTurbine 6 × 19, 3.24 m) · `Medium` 1.40 (Gun 2 × 16, ReactorCore 4 × 16, 2.67 m) ·
`Compact` 0.95 (AirIntake 11 × 2, SmallMotor 9 × 11, 1.81 m). Brackets and not a
multiple of true size because the family spans 6.2 : 1 — one ratio that keeps the 11 m
motor inside the wall's 30 cells drops the two small modules *below* the 0.80 they
started at. `Compact` is 0.95 and not 1.00 because the belly turbine's **cross** axis
binds: at 1.00 it crosses into a tenth cell and fits the nine-cell rack at no yaw.

Cost of hauling survives: all seven are distinct shapes, every one fits the 30 × 22 wall
and is still rack-carryable, and the motor lashes across the rack with the overhang rule
— which applies to **rectangles only**, so it was unreachable while the mask was there.
The thin ones (gun, intake) are genuinely cheaper to carry now; that is the honest price
of a footprint that tells the truth. Nothing in the world moved: the family is
`ItemWorldSizing.Authored`, so the module in the sand keeps true ship scale, and
`holdSize` is untouched.

Fallout fixed on the way: `ItemFootprint`'s implausible-measurement ceiling was a typed
2.10 m stated against the **rig's** widest face, so the 2.205 m motor tripped a warning
telling its author to size an item that was already sized. It is now
`PackGrid.WidestFaceCells × PackGrid.Cell` (the gear wall's 30 cells, 2.835 m), pinned
against both shipped containers.

Verified in the live Editor against the real `ItemFootprint`/`PackShapes`/`PackOverhang`:
7 distinct shapes, all fitting wall and rack, ladder order matching true-size order, no
warnings. `GlobalObjectIdHash` and `prefabId` unchanged on all seven, so saves and
network registration survive the rebuild. **The EditMode suite was not run** — the Test
Runner needs UI that MCP will not drive and the Editor was open, so
`ShipPartsTests.{EveryModule_IsShapedByItsOwnOutline,
EveryModule_IsDrawnBiggerThanTheOldSharedSize, TheHaulLadder_KeepsTrueSizeOrder,
EveryModule_FitsTheGearWallAndTheRack}` and
`WallInventoryTests.TheWidestFaceConstantMatchesTheShippedContainers` are written and
type-check but are unrun. Not yet seen on the wall in play.

### SHIP-02 — Remove the cube object — **DONE 2026-09-06**
A leftover cube object is still present and should be deleted.

The debug `Cube` — a primitive left in the item registry, so it was a real pickupable item
that showed up in the dev browser and could be put on the gear wall. Removed everywhere it
was registered: prefab, `Resources/Items/Debug/Cube.asset`, its generated icon, a row in
**both** network prefab lists, and one placed instance each in Emil's and Marius's test
scenes. `Sync Network Prefabs` only ever adds, so the two list rows had to come out by hand —
now written down in [Inventory.md](AI/systems/Inventory.md)'s Gotchas, next to a new
"remove an item" counterpart to the add checklist. Nothing in the project references any of
the three GUIDs any more, and both edited scenes still resolve every local `fileID`.
`Items/Debug/Sphere.prefab` is the same kind of leftover and is still there — not in scope
for this item, worth its own decision.

---

## ANIM — animation & ragdoll

### ANIM-01 — Active flashlight gauntlet does not hold the item pose — **DONE 2026-09-06**
While a flashlight gauntlet is active, the same hold animation that a held item
triggers should stay applied. Currently it does not stick. Must work for **both**
the left and right arm.

Two separate faults. **"Does not stick"** was a layer WEIGHT bug: `PlayerAimRig`
wrote the torch's `HoldStyle` into the Animator but eased `holdT` from `heldStyle`
alone, so with empty hands the state machine entered the pose on a layer weighted 0
— right parameter, right state, arm still hanging, clean console. Both now come off
one `Posing` property. **"Both arms"** needed the pose mirrored: measured off the
clips, `HumanM@Gun_Aim01` puts the right hand 0.19 up and 0.19 forward of the body
centre and leaves the left at the hip, so a lamp on the left forearm raised the empty
arm. `PlayerAimRig` now tracks a torch style per arm and writes a `HoldMirror` bool;
`PlayerUpperBodySetup` builds a `Hold <style> Mirrored` twin of each hold state with
`state.mirror`, the same trick `Raise Left` already used. The arm reaches the artifact
as `UsableItem.WornOn`, set from the body slot beside `Worn`. Verified against the live
compiled types and by reading the rebuilt state machine back; `TorchPoseTests` pins the
seven cases. The reason recorded here for not running them — `RagdollRig.cs` failing to
compile mid-ANIM-02 — no longer applies: that work landed 2026-09-07 and the editor
assembly is clean again (re-verified through Bee's response files 2026-09-07). **The tests
themselves have still not been run**, because the Test Runner needs the contended Editor.
Not yet seen on a body in play.

### ANIM-02 — Ragdoll breaks on multi-mesh models — **DONE 2026-09-07**
Single-mesh models ragdoll fine; models built from several meshes collide with
themselves and blow apart. The ragdoll must be driven off the model's **rig** using
traditional ragdoll technique. Items must still be able to knock others into ragdoll
without dealing damage — that path exists already, but the new ragdoll system needs
to be straightened out.

One mistake with three faces: `RagdollRig` built the skeleton on the model's **meshes**
instead of its **rig**. Meshes are leaves, so nothing could find a parent to joint to and
the graph collapsed into a star — the golem had 17 of its 18 joints on the pelvis, the
crab 19 of 20 on the carapace — while the `Bone_Hips → Bone_Spine` and `Coxa → Hip → Knee`
chains the models were rigged with sat unused. Every *dropped* mesh also stayed parented to
a bone nothing drove any more, so the golem shed 13 of its 31 pieces into mid-air. A skinned
model physically cannot do that, which is exactly why single-mesh ones looked fine.

Now `RagdollSkeleton.SelectRigNodes` picks the nodes that articulate the model and
`NearestRigNode` says which one carries each piece of geometry. Two further mechanisms fell
out of the same measurement: importance is now a **volume** (`CarriedVolume`) rather than a
vertex count, because weight only stands in for mass at one density and the ostrich's
eleven densely-tessellated neck meshes outvoted the whole bird — its ragdoll was 11 bones
of neck and nothing else; and the ragdoll's root is the heaviest **branch**
(`SubtreeBulk`), not the heaviest bone, because a thigh outweighs a chest and PatrolRobot 1
came out rooted at its right leg. Self-collision now covers every collider a body owns, not
one per bone: the crab's 22 authored `COL_*` boxes were adopted by the ragdoll and never
filtered, and `Diagnose Wired Prefabs` shared the blind spot exactly, reporting
`unfiltered: 0` on a body tearing itself apart.

Measured across all 16 wired prefabs. Worst joints-on-one-bone **19 → 6** (and that 6 is the
crab's six legs on its hull); every prefab roots at a sensible bone (Golem `Bone_Hips`,
crab `crab_walker_6`, ostrich `Spine` with legs, PatrolRobot 1 `Chest` at 20 bones where it
used to manage 2 and was written off as an asset limit); 0 bones without a collider;
0 unfiltered self-overlaps. The knockdown path (`NetMsg.Knockdown` → `AgentRagdoll`) was not
touched. 12 unit tests in `RagdollSkeletonTests`. **Not yet seen falling over in play, and
not yet checked on a client.**

### ANIM-03 — Lightning rod aim pose puts the hands straight up — **DONE 2026-09-06**
Holding the lightning rod produces a very odd aim pose with the arms in the air.
The lightning rod does not need the aim pose at all — drop it.

The pose was not a lightning-rod pose at all: the rod's `ItemGrip` declares `Relaxed`,
and every hold style on the Upper Body layer is a firearm clip — `Relaxed` is
`HumanM@Gun_Aim02`, despite the tooltip calling it "carried rather than wielded". So
the rod struck a pistol aim with nothing in the other hand. There is no "just hold it"
style to switch to, so the fix is `LightningSpell.UsesHoldPose => false`: the arms stay
on the Base Layer and idle and walk with the rod in hand. `UsableItem.OnEquipped` now
checks that opt-out **before** it looks the `HoldAnimator` up rather than only before
adding one — gating the `AddComponent` alone left the opt-out silently undone by any
authored component on the prefab. No prefab or animator asset was touched.
`HoldPoseTests` pins both (9/9 green; 7/9 with the fix reverted, and the two that go red
are exactly the new ones). Not yet seen on a body in play.

---

## ART — artifact behaviour

### ART-01 — Portal sprayer needs tuning — **DONE 2026-09-06**
- Increase sprayer pressure so it reaches noticeably further.
- Emit much more material per spray so covering an area is easier.
- The portal and the portal outline do not line up; the "tiles" are probably too small.

**Pressure.** `jetSpeed` 13 → **24 m/s**, `jetFlightTime` 1.6 → **2 s**. The reach figures in
the sources were all Earth calculations while this project's `Physics.gravity` is **18** — the
comment claiming "about 17 m lobbed" described a stream that actually made 9, and 5 held level,
which is why the far wall of a room was unpaintable. Measured against the real number the hose
now makes **32 m lobbed and 10 held level**. The flight budget has to outlast a full 45° lob
(1.89 s) or the far end of the arc is abandoned in mid-air, which is what the extra 0.4 s buys.
Speed, gravity and lifetime are also the droplets' own ParticleSystem settings; the builder
writes all three from one set of constants and `TheDropletsFlyTheArcThePaintIsTracedAlong` now
fails if they drift, which they already had — the prefab still carried 13 after the code moved.

**Material.** `dabRadius` 0.62 → **0.85 m** (a blob covers 1.9× the wall) and `paintPerDab`
0.045 → **0.03**, so a full tank buys 33 blobs instead of 22. Together that is about 2.8× the
area per tank. A single tap is still not a hole a player fits through — sweeping is still the
only way to a walkable portal, which is the design the spray can was built around.

**The outline.** Three separate faults, none of them the tile size:
1. `PortalRim`'s `_Radius` is a **gap measured in stroke radii**, and the shipped material
   carried **0.62** — a leftover from when the property was normalised, before the shaders went
   metric. The halo was being drawn 0.38 m clear of the aperture: a ring around empty wall. It
   is 0.06 now, and `PortalContentBuilder` writes `_Radius`/`_Thickness`/`_Crawl` on every run so
   a `.mat` can never again hold a number whose meaning has changed underneath it.
2. The aperture's crawling edge and the halo's wobble were **two different noises** at different
   frequencies, so the outline shimmered around an edge the hole did not have. There is now one
   `PortalStencilCrawl` in `PortalStencil.hlsl` and both shaders call it; the halo's `_Churn`
   varies the band's *width* instead of its position.
3. Both quads are sized in **metres** around the shape rather than by a fixed ratio of its box,
   and each material is handed half of its own quad. The halo's corner fade is a box rather than
   an inscribed circle, which used to erase the ring at exactly the ends of a diagonal sweep.

Verified by the portal suites — **73 passed, 0 failed** (`SprayedPortalTests`,
`PortalGunWiringTests`, `PortalLifecycleTests`, `PortalTraversalTests`), both shaders reported
clean by `ShaderUtil`. Not yet sprayed by hand in play: the reach and coverage numbers above are
arithmetic against the project's own gravity, and are the kind of thing that wants a playtest
before they are called final.

(Hand placement for the same item is tracked separately as **GEAR-02**.)

### ART-02 — Lightning rod effect only renders from one side — **DONE 2026-09-06**
Over roughly 180° of viewing directions the lightning renders; from the other 180° it
does not render at all.

Back-face culling, as suspected — but not on `LightningBeam.mat`, which belongs to the
laser staff and was already `Cull Off`. The rod's `Present()` instantiates
`Prefabs/VisualEffects/Lightning/Lightning.prefab` → `Art/VisualEffects/Lightning.vfx`,
whose five quad outputs are camera-facing and two-sided, and whose **mesh** output draws
the bolt itself on the built-in **Plane** — one flat, single-sided quad grid with no
`Orient` block, so it keeps a fixed world facing. That output is shaded by
`Art/Shaders/Effects/LightningVFX.shadergraph`, and the cull came from there, not from
the VFX: `VFXComposedShading` filters `cullMode` out of a ShaderGraph-shaded output
entirely, and with `m_AllowMaterialOverride` off URP bakes the target's
`Render Face = Front` into the generated shader as a literal `Cull Back` that no
material can reach. Front hemisphere: the bolt. Back hemisphere: nothing.

Fixed by setting that graph's `m_RenderFace` to `0` (Both → `Cull Off`) — one value in
`LightningVFX.shadergraph`. Winding, the billboard math and the camera-facing basis were
all ruled out first: the quad outputs already carry `Orient` blocks and default to
`Cull Off`, and the bolt's shading is Unlit and purely UV-space (a `Rectangle` mask
distorted by `Simple Noise`), so the back face is indistinguishable from the front and
needs no normal flip. Blast radius is one other asset: `LaunchArea.prefab` puts the
graph's default material on four meshes, and it is placed only in the two "Aleksander
test scene" files. Added `Assets/Game/Editor/Tests/LightningVfxTests.cs` to pin
Render Face = Both and to assert the VFX still shades an output with that graph.

No netcode and no state: the shader is an asset, so host and client resolve the same
render state, and the strike VFX is a local `Instantiate` from `Present()` on every
machine. Nothing here is saved and nothing needs to be.

Verified: the change is the whole cause chain, traced through the shipped VFX Graph and
URP sources (`VFXComposedShading.kAlwaysFilteredOutSettings`,
`UniversalTarget.RenderFaceToCull`, `UberSwitchedRenderState`); the built-in mesh id
`10209` confirmed as `Plane` against a scene that names it; the ShaderGraph JSON still
parses (69 blocks) and reports `renderFace 0`; `Assembly-CSharp-Editor` type-checks with
the new test file via Bee's `.rsp`; `python3 tools/docs_check.py --index` clean apart
from the pre-existing `SupplyCharge.md` budget error. **Not** verified: nobody has seen
the bolt from behind in play — the Editor was contended and this was not opened. Also
untouched and still true: the plane is unoriented, so it still thins to nothing within a
few degrees of exactly edge-on, the same as it always did on the side that worked; and
`LightningSpell.Present()` never destroys the VFX instance it spawns, so every cast
leaks a GameObject. Both are separate from ART-02 and were left alone.

### ART-03 — Net gun does not work — **DONE 2026-09-06**
Completely non-functional. (Its inventory size is tracked as **INV-02**.)

**"Does not work" was: the net lands on its edge, so it catches nothing.** Not a compile
error, not a client-only failure, not a missing prefab reference — everything in the chain
is wired and `Assembly-CSharp` builds clean. The net flies, lands, drapes, and holds
nobody, on every machine, with an empty console.

`SnareLattice.Deploy` lays the sheet out perpendicular to the aim — the net leaves the
barrel edge-on, the way a thrown cast net does — and `SnareCatch.FaceAlongFlight` was
supposed to tip it face-down before it arrived. Its own comment claimed following the
closed-form velocity did that. It does not, and has not since the flight was retuned from
26 m/s under 16 m/s² for 1.6 s to **32 m/s under 7 m/s² for 0.85 s**: that arc bends by
`atan(7 × 0.85 / 32)` — **ten and a half degrees, end to end**. So the net arrived within
10° of vertical and buckled. Two consequences, and the second is the bug the user saw:

1. It reads as a crumpled hank of rope rather than a net.
2. **`SnareReceiver`'s capture `OverlapBox` is sized from `SnareCatch.Footprint`**, which is
   the box the nodes occupy. An edge-on net hands it a slab **1.2 m deep** where the net is
   6 m across, so almost nothing is ever inside the query. `SnareCatch.Capture` is only ever
   reached from that pass, and it returns silently — no log anywhere in the chain.

The 6 m net also **ploughed**: fired level from a ~1.45 m muzzle its hem is 1.5 m *below*
the sand within 0.15 s, and the `Flight` branch runs the drape and `GripGround` every frame,
so half the net was clamped and friction-gripped against the ground for 22 of its 27 m of
flight. Turning it face-down fixes that too — a flat net has almost no vertical extent — so
this is one fix, not two.

**The change** is `SnareCatch.FaceAlongFlight`: the turn is now *driven*. The facing swings
from the travel direction toward straight down over `FaceDownShare` (0.4) of the carry,
about the horizontal hinge across the flight so a shot fired straight up still has a
well-defined axis. The old doc comment claiming the velocity did this is deleted. 0.4 is
measured, not guessed (below); the unfurl finishes at 0.28 s and the turn at 0.34 s, so the
net is open just before it is flat.

**Measured, by replaying `SnareCatch`'s own flight loop offline** — the real
`SnareLattice`, `SnareDrape`, `SnareCinch` and `NetGunFlight` sources compiled against a
stub `UnityEngine`, a flat floor, the shipped tuning, a level shot from 1.45 m. Landed
footprint **along the flight axis**:

| facing rule | 15×15 (shipped) | 9×9 (test bed) |
| --- | --- | --- |
| velocity only (shipped, broken) | **1.19 m** | **2.50 m** |
| driven, share 1.0 | 2.25 m | — |
| driven, share 0.6 | 3.24 m | — |
| **driven, share 0.4 (shipped fix)** | **4.54 m** | **5.19 m** |
| driven, share 0.25 | 4.14 m (loses width) | — |

The old flight tuning was tested too and is *not* the fix: at 26 m/s under 16 m/s² the net
lands 1.59 m deep — worse. The arc has never bent enough; the aim retune only made that
visible.

`NetGunTests.AFiredNetComesDownFlatRatherThanOnItsEdge` pins it, over a real floor, firing
level, asserting the settled footprint is deeper than half the net's own width (3 m —
between the 2.50 m broken and 5.19 m fixed numbers above). It measures the **flight** axis
deliberately: the across axis is free, because `Deploy` lays the sheet out that way at the
muzzle and nothing takes it away again, so a check on the wider axis passes for a curtain
standing on its edge. That is exactly why 100+ existing tests missed this — they ask where
the net went and whether it stopped moving, never what shape it is when it arrives.

**Verified:** `Assembly-CSharp` and `Assembly-CSharp-Editor` both compile clean (Bee `.rsp`
replayed through Unity's Roslyn, both `.rsp` files confirmed to list the changed files); the
numbers above, from the offline replay of the shipped solver sources. **Not verified:** the
EditMode suite has **not** been run — the Editor is open and contended — so the new test has
never executed; and the net gun has **not** been seen fired in play, on a host or a client.

**Also found, not changed.** Three things that are real but are not this bug:
- `DEFECTS.md`'s `Camera.main` row was wrong on both halves and is corrected in the same
  change: the player camera *is* tagged `MainCamera`, and `Net_Cord.mat` is `_Cull: 0`, so a
  reversed ribbon winding cannot hide the net. What is true is that `Camera.main` is null
  **while mounted**, which flattens the cord's ribbons against a world axis.
- `NetGun.prefab`'s serialized `struggle:` block is stale — it still carries `shuffleRadius`,
  `thrashFrequency`, `thrashShare` and `dragInfluence`, none of which exist in
  `SnareStruggle` any more, and is missing all five struggle-meter fields. Harmless (Unity
  keeps the field initialisers) but it means nothing on the prefab is authored. `lattice:`
  is likewise missing `cinchStiffness`. Re-run `Tools/Items/Build Net Gun` to re-serialise.
- **The capture chain is silent end to end.** `SnareTether.Bind` / `SnaredBody.Bind` return
  false when the ragdoll refuses, `Capture` returns false, and `ResolveLandedNets` just
  `continue`s. A creature with no `NavMeshAgent` to hobble (ostrich, crab walker, humanoid
  robot, bounty hunter) and a rig that will not go limp is therefore uncatchable with nothing
  in the console, since the net's whole post-rework effect is downstream of
  `RagdollRig.GoLimp`.

  **Updated 2026-09-07, after ANIM-02 landed.** The refusal was probably never the ragdoll:
  all four of those creatures already built a skeleton before that rework (ostrich 11 bones,
  crab 20, humanoid robot 8, bounty hunter 19), so `HasSkeleton` was true and `GoLimp` was
  not declining. What they had was *nonsense* — the ostrich's 11 bones were all neck
  vertebrae with no body or legs, the crab carried 19 of its 20 joints on its carapace, and
  the humanoid robot was rooted at its own left thigh — so anything reaching for a hips or a
  named bone was reaching into rubbish. Post-ANIM-02 every wired prefab is rooted sensibly
  (worst joints-on-one-bone is now 6). The conditional above still holds as written; what
  needs re-testing is whether it was ever the branch being taken. Note also that
  `Diagnose Wired Prefabs` had a blind spot (`GetComponent<Collider>()` on the body's own
  object) that hid 22 adopted colliders on the crab, so its historic "unfiltered: 0" proved
  nothing.

### ART-04 — Jumping rod has no landing boost — **DONE 2026-09-06**
Hitting jump exactly on landing should boost the next jump, scaling up so big heights
are reachable — but hard enough to time that it is not automatic every jump.

Built as a **rhythm you play**, not a bonus you accumulate: hit Jump on the beat of a
landing and that hop is multiplied; keep hitting it and the multiplier compounds; miss
once and the whole chain is gone. Rule set is one press per hop, judged inside a window
around touchdown.

The numbers, all serialized on `JumpingRodConfig` (Inspector, and written onto
`JumpingRod.prefab` — a `[SerializeField]` keeps the value the asset was saved with):
**early window 0.12 s, late window 0.08 s, 1.12 speed per link, 5 links, whole chain
lost on a miss**. Speed compounds, so height — `v²/36` at this project's -18 gravity —
goes up as its square: 3.4 m cruise → 4.2 → 5.3 → 6.6 → 8.3 → **10.4 m** at five links,
and the boost multiplies the take-off *clamp* so it lifts `maxHopSpeed` with it rather
than being invisible above two links. Airtime grows with the chain (1.22 s → 2.15 s), so
the higher you are the more time you have to prepare the next press and the more one
miss costs.

Why those numbers. The window is asymmetric because forgiveness should go where it
cannot be seen (**GDC-L1-FEEL-0003**, contextual): the early half is a plain input
buffer — human timing scatters by tens of milliseconds and a press the player felt was
on the beat must not be thrown away — while the late half is paid out by topping the hop
up *after* it has left the ground, so every millisecond of it is a visible surge, and it
is narrower for that reason (and kept under `rebounceLockout`, pinned by a test). A late
press adds the *difference* between the hop given and the hop earned, which lands the
player on exactly the arc a punctual press would have. Total 0.2 s of a 1.22 s cycle at
cruise, falling to 9% of the cycle at the top — under human reaction time, so the press
has to be *anticipated*, which is the skill (**GDC-L1-DESIGN-0003**, contextual: a
pattern worth mastering, staged over five links). Two presses in one hop spoil that
landing at **both** ends of the window, so mashing Jump — which would otherwise buy the
boost every landing, since a mash always lands a press inside a fair buffer — loses the
chain instead. A miss is a full reset because the top of the ladder should be a run the
player is currently making. Feedback: a boosted landing plays a different sound
(`boostSoundId`, `PlayerDash`) — the rhythm has to be audible or nobody learns it
(**GDC-L1-FEEL-0004** objective, **GDC-L1-UX-0001** contextual: taught by doing, not by
text; the coil's existing squash already telegraphs the approaching window for free).
Read `GDC-L1-BAL-0004` too and did **not** apply it — counterplay/intransitivity is
about competing options, not a single skill ladder. **GDC-L1-BAL-0005** is why the
numbers above are flagged as a starting point, not a verdict: math finds, play decides.

Changed: `JumpingRodConfig` (5 tunables), `JumpingRodHopModel.TakeoffSpeed(..., links)`
+ `BoostFactor`, new `JumpingRodChain` (pure window/chain state machine, clock injected
like `DoubleTap`), `JumpingRodItem{,.Bounce}.cs` (binds the holder's Jump, owner-gated at
the press; chain resets on plant), and `PlayerMovement.OnJump` now stands aside while
`SetBouncing` is on — without that, the 7 m/s leg jump *sets* `velocity.y` in the same
physics step and overwrites the 11 m/s hop, so timing the beat made the player go
**lower**. Netcode: nothing added — owner's own key, owner's own body, replicated by the
transform that already replicates. Persistence: the chain is deliberately **not** saved
(a rhythm worth a few hundred ms; a mid-air quicksave restoring five links would hand
back a run nobody was making) — the slot still saves `planted`.

Verified: `Assembly-CSharp`, `SpaceGame.Gear.JumpingRod` and `Assembly-CSharp-Editor`
type-check clean through Unity's Roslyn on Bee's response files (gear dll rebuilt and
`-r:` repointed, so the new signature is checked against the real contract); 17 new
EditMode tests in `JumpingRodBoostTests` plus a prefab-tuning test in
`JumpingRodWiringTests`; every one of those scenarios also re-run against a mirror of
the state machine outside Unity. **Not** verified: the tests were not executed — the
Editor is contended and `-runTests` needs it closed — nor was any of it played. Whether
0.12/0.08 s is a fair window and 1.12/link the right climb **needs a human on a pogo
stick**; I could not feel it.

---

## AI — enemy AI & pathfinding

### AI-01 — Enemy pathfinding needs work
Reported 2026-09-16 by the user, after extensive play testing of the faction work. Enemies
do not move around the world well enough. Not yet decomposed — the symptom has not been
pinned to one cause, and the candidates sit in three different systems:

- **The bake.** One author-time bake of the whole world into `WorldNavMesh.asset`
  ([NavMeshSystem](AI/systems/NavMeshSystem.md)); nothing bakes at runtime. Holes, missing
  carve-outs around settlement buildings and stale bakes all present as "the enemy will not
  come at me".
- **The agent.** `NavMeshAgentMotor` + `NavMeshAgent` radius/height/`stoppingDistance` per
  prefab, and `AgentGroundConform` on top of it. A radius wider than a doorway is a path that
  silently does not exist.
- **The decision.** `ChaseModule` walks to `AgentTargeting`'s target and nothing else;
  `SearchModule` handles lost sight. Neither replans around an obstacle, and there is no
  repath-on-stuck anywhere in the stack.

**Before working this, get one reproducible case** — which enemy, where, and what it did
instead — because the three causes above need different fixes and the same sentence describes
all of them. `NavMeshAgentMotor` already publishes `stuckVelocityThreshold`, so a stuck-detector
is the cheapest probe.
