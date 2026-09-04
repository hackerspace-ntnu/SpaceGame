---
system: Wingsuit
layer: items
summary: A worn membrane that flies the player's own body on the ornithopter's model with the thrust set to zero
paths:
  - Assets/Game/Scripts/Gear/Wingsuit
  - Assets/Game/Scripts/Characters/Player/Movement/WingsuitFlight.cs
  - Assets/Game/Scripts/Characters/Player/Movement/WingsuitPose.cs
  - Assets/Game/Scripts/Items/Equipped/WingsuitItem.cs
  - Assets/Game/Scripts/Items/Equipped/WingsuitWings.cs
  - Assets/Game/Scripts/Items/Equipped/WingsuitRecolor.cs
  - Assets/Game/Editor/Items/WingsuitBuilder.cs
  - Assets/Game/Editor/PlayerGlideLayerSetup.cs
  - Assets/Game/Prefabs/Items/Equipment/Wingsuit.prefab
  - "Assets/Game/Art/Models/_Source~/models/gear/wingsuit.py"
  - "Assets/Game/Art/Models/_Source~/models/gear/wingsuit_worn.py"
  - "Assets/Game/Art/Models/_Source~/models/gear/_wingsuit.py"
  - "Assets/Game/Art/Models/_Source~/models/characters/astronaut/glide.py"
symptoms:
  - "a double tap of Space does nothing while I am standing on the ground"
  - "the wings come out and I drop like a brick anyway"
  - "I cannot move after landing from a glide"
  - "the worn wingsuit is a box on my back instead of wings"
  - "the worn wing has a yoke and spars but no cloth between them"
  - "the worn wings sit at the waist pointing backwards"
  - "the wing billows at the WRIST end instead of at the hem"
  - "the worn wings float off the arms instead of following them"
  - "the worn wing's cloth pokes through the waist"
  - "the worn wingsuit reads as a sleeve rather than as a wing"
  - "the mouse turns my body while the wings are out, and the flight fights it"
  - "the horizon rolls when I bank and it makes me ill"
  - "the wings stay on the pack instead of going onto my arms"
  - "the membrane is stiff as plywood and never billows"
  - "the wings are the wrong colour, or they stay beige when I change my suit"
  - "another player gliding stands bolt upright while sliding through the air"
  - "I get charged fall damage AND crash damage for one landing"
  - "a mid-air quicksave reloads standing still in the sky"
  - "the wingsuit and the wing pack cannot both be carried"
  - "gliding into a cliff at full speed costs nothing"
reads_with: [Ornithopter, BodyEquipment, PlayerCharacter, Multiplayer, Persistence]
updated: 2026-09-04
---

# Wingsuit

A membrane wing worn on the back. Deploy it in mid-air and the player's own body becomes a glider —
nothing is spawned and nothing is mounted. It is [Ornithopter](Ornithopter.md)'s flight model with
the thrust set to zero, run on the player's Rigidbody.

**Scope:** [Gear/Wingsuit/](Assets/Game/Scripts/Gear/Wingsuit) (own asmdef), [WingsuitFlight.cs](Assets/Game/Scripts/Characters/Player/Movement/WingsuitFlight.cs), [WingsuitPose.cs](Assets/Game/Scripts/Characters/Player/Movement/WingsuitPose.cs), [WingsuitItem.cs](Assets/Game/Scripts/Items/Equipped/WingsuitItem.cs), [WingsuitWings.cs](Assets/Game/Scripts/Items/Equipped/WingsuitWings.cs), [WingsuitRecolor.cs](Assets/Game/Scripts/Items/Equipped/WingsuitRecolor.cs), [WingsuitBuilder.cs](Assets/Game/Editor/Items/WingsuitBuilder.cs), [PlayerGlideLayerSetup.cs](Assets/Game/Editor/PlayerGlideLayerSetup.cs).
**Related:** [Ornithopter.md](Ornithopter.md) (the flight model), [BodyEquipment.md](BodyEquipment.md) (the torso slot and the double-Space), [PlayerCharacter.md](PlayerCharacter.md) (the body it takes over), [Multiplayer.md](Multiplayer.md), [Persistence.md](Persistence.md).

## Model

- **A back item.** `Wingsuit.asset` is `EquipKind.Back`, worn on the spine, fired by a **double tap of Space** through `BodyEquipmentController`'s back channel. There is one torso slot, so the wingsuit and the wing pack are **mutually exclusive with no rule needed**.
- **Two models, one item.** Worn, the suit is `wingsuit_worn.fbx`: two cloth panels running from each shoulder out along the arm and down past the hip, on an over-shoulder yoke that laces back to the pack's lash rail. In the hand and on the ground it is `wingsuit.fbx`, the flight suit — a slim spar case with its wings folded away. [`WornVisual`](Assets/Game/Scripts/Items/Equipped/WornVisual.cs) swaps them (see [BodyEquipment.md](BodyEquipment.md)), and `WingsuitWings` swaps the worn wing out again for the length of a glide, because the worn wing and the flight wing are the same wing in two states and exactly one of them may be visible. The worn model is authored at true wearer scale in the **spine bone's** frame — its wing roots are the measured upper-arm joints — so `WornFit.anchorToBone` is set and it ignores the lash rail that back gear normally clips to.
- **The worn wing is built along a 45° arm line**, the same one the gear screen holds the wearer's arms at *for this item* (`InspectStance.DefaultDroop`, asked for by `WornFit.holdsArmsOut`; see [BodyEquipment.md](BodyEquipment.md)). The two are one number and have to move together. Lowering the arm is what forced the panel's trailing edge to be **raked** rather than square: a loft's sections are perpendicular to its span, so at 45° a square chord runs 45° inboard as well as down and walks the cloth into the wearer's ribs within about 10 cm. `wingsuit_worn.py`'s `SWEEP` shears the finished panel along its own span, which puts the root's trailing corner on the flank and makes the free edge run wrist-to-hip the way a real arm wing's does.
- **The flight is `OrnithopterFlightModel.Step`, unchanged**, run on the player's own Rigidbody by `WingsuitFlight`. Same two angles (`Gamma` where you are moving, `Pitch` where you are pointing), same stall, same energy trade. None of the physics is a copy.
- **"It cannot climb" is enforced twice.** `FlapThrust` is 0 *and* `WingsuitControl.Stick` never returns a positive `Flap` — a number somebody types in and an input that would climb at zero thrust fail differently. A **tuck** (LeftCtrl, `Flap < 0`) is still the dive, and zoom-climbing by spending speed is energy rather than thrust, so it is not blocked.
- **Tuned for the sensation, not for a real wingsuit** (`GDC-L1-FEEL-0007`, which glides 2.5:1 and would be miserable): **~3.9:1, stall ~18 m/s, cruise ~23 m/s** sinking ~6. Both are *derived* — read them back from `WingsuitFlightConfig.BestGlideRatio` and `OrnithopterFlightModel.StallSpeed` rather than assuming.
- **It opens along your LOOK, not along your fall.** `WingsuitControl.Deploy` takes the heading AND the flight path from the camera's forward, carrying the speed you had as a magnitude. Reading the velocity instead — which is what the wing pack does, because it spawns a craft you then aim — snapped the view to the ground and made the first second of every flight a recovery from a dive nobody asked for. Rotating a fall onto the look direction converts vertical speed into horizontal: a real and deliberate gift, but the same joules and the same altitude.
- **Steering is "fly where you look" plus A/D.** Mouse Y aims the nose as a **position** (it stays where you put it, so the crosshair means something). Mouse X **banks** you as a **rate**, decaying to centre — banking is what turns a wing, and a mouse that only supplied a weak rudder meant the only real way to turn was A and D, which is not what "fly where you look" promises. A/D still bank directly and sum with it. Whatever share of the swing the bank does not take becomes flat yaw, which is what keeps the wing answering below flying speed. The stick handed to the model is the *error* between commanded and actual pitch, so the model keeps its rate limit and its stall fade.
- **The mouse moves the nose at the player's own look sensitivity**, read off `PlayerLook.LookDegreesPerUnit` rather than tuned here. A control that IS the look has to move like the look: carrying its own smaller number made aiming the wing about 40% of the speed of looking around, and every player read that as input lag rather than as weight.
- **The body only ever yaws.** The capsule is 3 m of upright collider that the ground probe, the crouch and the head look all assume stands up. Pitch and bank are shown on the view (`PlayerLook.SetFlightAttitude`; `viewRollFraction` 0.5, dial-to-zero — `GDC-L1-FEEL-0006`) and on the skeleton (`WingsuitPose`).
- **A glide is a thing you are committed to** (`GDC-L1-FEEL-0008`): while the wings are out the mouse is the stick, not the look. Input is still heard on frame one; what is deliberate is the resolution time.
- **Landing is the ornithopter's rule with a human's numbers** — `OrnithopterCrash.ImpactDamage` on **closing speed**. A flown arrival (~6 m/s) is free, a level dive into a cliff hurts, a held vertical dive kills. Ordinary fall damage is suppressed, so exactly one rule is in play.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `WingsuitFlightConfig` | [Flight/](Assets/Game/Scripts/Gear/Wingsuit/Flight/WingsuitFlightConfig.cs) | `: OrnithopterFlightConfig` with a constructor: the whole tuning, thrust zero. `BestGlideRatio` is derived |
| `WingsuitLandingConfig` | [Flight/](Assets/Game/Scripts/Gear/Wingsuit/Flight/WingsuitLandingConfig.cs) | `: OrnithopterCrashConfig`. Safe 9 m/s, lethal 30; the Recovery fields are inert — nobody dismounts |
| `WingsuitControl` | [Flight/](Assets/Game/Scripts/Gear/Wingsuit/Flight/WingsuitControl.cs) | Pure: `AimNose`, `NoseStick`, `Swing`, `Bank`, `Stick`, `ViewRoll`, `Deploy`. Where "fly where you look" is defined |
| `WingsuitFlight` | [Player/Movement/](Assets/Game/Scripts/Characters/Player/Movement/WingsuitFlight.cs) | Owner only. The state, both hand-overs, the landing, `ITeleportAware`. Execution order **150** |
| `WingsuitPose` | [Player/Movement/](Assets/Game/Scripts/Characters/Player/Movement/WingsuitPose.cs) | Every machine. Tilts the hips from **measured** motion. Order **920**, before `PlayerHeadLook` (950) |
| `WingsuitItem` | [Items/Equipped/](Assets/Game/Scripts/Items/Equipped/WingsuitItem.cs) | `UsableItem`, `UseAuthority.Owner`. The gesture, the rule, attaching the other three, the mid-glide save |
| `WingsuitWings` | [Items/Equipped/](Assets/Game/Scripts/Items/Equipped/WingsuitWings.cs) | Every machine. Straps membranes to arm bones, shows/hides them, drives `ClothWind` from measured speed |
| `WingsuitRecolor` | [Items/Equipped/](Assets/Game/Scripts/Items/Equipped/WingsuitRecolor.cs) | `: PaletteRecolor` with its own one-row table (`WingsuitMembrane`) |
| `FlightLaunch` | [Ornithopter/Flight/](Assets/Game/Scripts/Vehicles/Ornithopter/Flight/FlightLaunch.cs) | `CarryFrom` / `HeadingOf`, extracted from `WingPackItem` when both wings needed it |
| Builders | [WingsuitBuilder.cs](Assets/Game/Editor/Items/WingsuitBuilder.cs), [PlayerGlideLayerSetup.cs](Assets/Game/Editor/PlayerGlideLayerSetup.cs) | **Tools ▸ SpaceGame ▸ Items ▸ Build Wingsuit** and **▸ Player ▸ Build Glide Layer** |

## Tunables

| Field | Default | Effect |
| --- | --- | --- |
| `Mass` / `WingArea` | 110 kg / 4 m² | The two numbers that set the stall. Re-read `StallSpeed` after any edit |
| `LiftSlopePerDegree` / `StallAngle` | 0.075 / 18° | A fabric wing with a body in it: less lift per degree, hangs on longer |
| `DragCoefficientZeroLift` / `InducedDragFactor` | 0.10 / 0.16 | Together these ARE the glide ratio — `1/(2·√(cd0·k))` |
| `FlapThrust` | **0** | Never anything else |
| `PitchRate` / `RollRate` / `MaxPitch` / `MaxRoll` | 150 / 220 °/s, 70° / 70° | Well past an aircraft's on purpose — a wingsuit is a person moving their own arms |
| `TailYawRate` / `FullAuthoritySpeed` / `StalledAuthority` | 35 °/s / 10 m/s / 0.45 | Low authority speed, because a deploy starts near the stall and controls that fade out as the player takes hold read as a suit that ignores them |
| Flight: `spreadDuration` / `minAirspeed` | 0.35 s / 14 m/s | The opening ramp; the floor a deploy starts at |
| Flight: `lookSensitivityShare` / `noseSaturation` | 1 / 2.5° | The nose moves at the player's own look speed; how far off before the stick is hard over |
| Flight: `mouseBank` / `bankShare` / `swingCentring` / `viewRollFraction` | 0.05 / 0.85 / 2.2 per s / 0.5 | How hard the mouse rolls you, how much of that is bank rather than rudder, how fast it rolls level again, how far the horizon leans |
| Landing: `SafeClosingSpeed` / `LethalClosingSpeed` | 9 / 30 m/s | Free arrival ↔ full player health |
| Pose: `bankFromTurn` / `maxBank` / `response` | 0.5 / 60° / 8 per s | How far the body rolls into its turns, and how fast |
| Wings: `fullBillowSpeed` / `maxBillow` / `upwardBias` | 24 m/s / 0.35 m / 0.55 | How hard the membrane bulges, how far the airflow is bent up into it |

## Flows

1. **Deploy.** Double Space → back channel → `CanUse`: allowed if already gliding (a fold is always legal), refused with a log if `PlayerMovement.IsOnGround`. Owner `Use()` toggles; `Begin()` reads the look direction off `AimProvider` and hands it to `WingsuitControl.Deploy`, which opens the wing flying that way at the speed the player had — `Pitch = Gamma`, so angle of attack is zero and the camera does not jump — then turns `useGravity` off and takes the body off `PlayerMovement` and `PlayerLook`.
2. **Fly.** `Update` (render loop, where the mouse moves) accumulates the commanded nose angle and the rudder; `FixedUpdate` advances `Deployment`, runs `Step`, writes velocity and heading, hands the attitude to the view, then checks for ground.
3. **Show.** The flight sets the `IsGliding` animator bool, `ClientNetworkAnimator` replicates it, and every machine reads it back to show the membranes, enable `WingsuitPose` and play the clip.
4. **Land.** `PlayerMovement.IsOnGround`, or `OnCollisionEnter` for a cliff face, which is never underneath you. Closing speed is read from the **flight state** before the glide ends, damage goes through `NetDamage.Apply`, and `CarryMomentum()` stops air control confiscating the speed.
5. **Fold.** Another double Space, landing, death, unequipping or `OnDisable` — all reach `End()`, which is what hands `PlayerMovement` and `PlayerLook` back.

## Multiplayer

- **Nothing new is on the wire.** The player's `NetworkTransform` is owner-authoritative, so the machine flying the body owns the truth. The one thing anyone else needs — are the wings out — is the `IsGliding` animator bool `ClientNetworkAnimator` already replicates, and that bool drives the membranes, the pose component and the animator layer everywhere. Better than the ornithopter's known limit as a result: a bool is state, so a **late joiner sees this right**.
- Use messages are the hotbar's existing four. `UseAuthority.Owner`, because the whole effect is the holder's own body — a server-applied velocity would be overwritten by the owner, silently.
- `WingsuitFlight` is added **only on the owner**; `WingsuitPose` and `WingsuitWings` everywhere, and both *measure* motion off the transform rather than a Rigidbody, because a remote body is kinematic and reads zero velocity anywhere but at home.
- The dropped prefab is registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset).
- **Verify on a real client:** glide past the host and check the prone pose, the wings out, the body banking into turns, and the fold on landing.

## Persistence

- The suit is saved by the torso slot (`BodyEquipmentSaveable`, key `body`) like any gear.
- **A glide in progress** rides the item's `ItemState`: `glide` (airspeed, gamma, heading as a Vector3) and `nose` (pitch). `IItemDeferredRestore.TryCompleteRestore` calls `Resume`, which enters with the wings **already open** rather than spending the spread ramp again — without it a mid-air quicksave reloads standing still in the sky with no speed for a wing to catch, the mistake `OrnithopterSaveable` made first.
- Dropped on the sand it carries `SaveableEntity` + `TransformSaveable` with a stamped `prefabId`.

## Gotchas

- **`SetGliding` is narrower than `DisableGroundSnap` and wider than `SetTethered`, and both halves matter.** A disabled ground snap returns from `FixedUpdate` before doing anything — no grounded state, no animator — which is the bug the tether was written to stop repeating, and the wing *asks `PlayerMovement` where the ground is*. A tether only changes how move input is applied. So `gliding` skips exactly two things: the horizontal write, and fall damage.
- **The execution order between `PlayerMovement` and `WingsuitFlight` is load-bearing** — hence `[DefaultExecutionOrder(150)]`. Movement runs first, sees `gliding` still true, skips fall damage and writes `wasGrounded = true`; the flight then ends the glide and bills the closing speed. Reverse them and one landing is charged twice. There is also **one ground probe and it is `PlayerMovement`'s** — a second would disagree at the edges, and the edges are where a landing happens, so a deploy could open the wings into a state that lands on the next step.
- **`PlayerLook` owns the lens's local rotation, all of it.** The roll goes through it because `Update` reassembles the whole `localRotation` every frame and would delete anything else written there. `ApplyLensRotation` is now the single writer; there used to be three copies.
- **A corpse does not fly.** Death disables `PlayerMovement` and `PlayerLook` and knows nothing about this, so the flight checks `PlayerController.IsDead` itself — `IsDead`, not the enabled flags.
- **`WingsuitPose` writes the HIPS at order 920**, before `PlayerHeadLook` (950): everything hangs off the hips so one bone tilts the body, and a head posed first is dragged off its aim by its own parent. **The prone attitude is in the CLIP and only the deviation is measured** — a clip cannot hold an angle that changes every frame, and a peer whose `WingsuitPose` never ran still sees somebody flying rather than standing up in mid-air.
- **The Glide layer is unmasked and sits ABOVE Upper Body**, because while the wings are out the arms *are* the wing and a hold pose reaching through would fold one in flight. Its default state is empty rather than the weight being animated: a motionless state contributes nothing, and a driven weight is one more thing to get wrong on a remote.
- **`ClothWind`'s anchor is object space; its amplitudes are metres; a Blender FBX lands at a lossy scale of 100.** `_AnchorOrigin`/`_FreeLength` want the former, `_MaxStretch`/`_WindStrength` the latter. Mixing them gave a 3 mm displacement ceiling — a wing pinned rigid, reading as plywood, with a clean console. `WingsuitBuilder.ApplyAnchor` measures both every run, which is what the nomad's cape paid for with a cloak that wrapped round its own front. The **pin axis is Blender's Y even in Unity**: `_exportlib.export` uses `bake_space_transform=False`, so the conversion rides on the node and the vertices keep Blender's frame.
- **The worn wing is built at twice the arm it hangs on** (`wingsuit_worn.py`'s `WING_SCALE`, 2 since 2026-09-04, `WornFit.size` 1.58 → 2.60). The panel used to end exactly at the wrist, which is why it read as a sleeve rather than as a wing; the cloth now runs out about 0.8 m past the hands. The constant scales the WING only — span, chords, camber, skin, spar — because the yoke and the shoulder straps are fitted to a body that did not change size. **The cuff is the seam:** it is a forearm wrap, so it stays at `CUFF_SPAN` (the arm's own reach) and now reads as the spar's mid-span anchor. Carried out to the new tip it would be a forearm wrap closed round thin air past the wearer's hand.
- **The worn wing's shape and the gear screen's stance are ONE decision — and the wingsuit is the ONLY thing that stance exists for.** `Wingsuit.prefab`'s `WornFit.holdsArmsOut` is what makes the screen strike it at all; with the flag off the arms hang in the idle like they do for every other item and the cloth folds into the ribs. `wingsuit_worn.py`'s `INSPECT_DROOP`, `InspectStance.DefaultDroop` and `BodyFocusSession.armDroop` are all 45°, and the cloth is authored along that line. Change the stance without re-authoring the model and the wing hangs in mid-air beside the arm; change the model without the stance and the same. The model's other two shape constants are bounded by real geometry rather than taste — `SWEEP` folds the panel through itself past about 1.35, and `BACK_TILT` below about 15° puts the inboard corner inside the wearer's waist (measured: 0.106 m out at 10°, 0.179 at 18°, against a torso half-width of roughly 0.20).
- **The chord axis is decided by the leading-edge SPAR, not by "the span is the longest extent".** That shortcut held for the flight wing (0.95 m span against a 0.86 m chord) and inverted on the worn one (0.74 m span, 0.78 m chord), which pinned the panel across its span: the shoulder end held and the WRIST end billowing. The spar lies along the span by construction and is fifteen times longer than it is thick, so which of its axes is its length is not a judgement call. A taper test was tried in between and does not work — a wing is a triangle and therefore narrows along both axes, so both assignments score high and the answer is a coin flip.
- **The two wings need two ClothWind MATERIALS, not two uses of one.** `_WindStrength` defaults to 0.22 m in the shader and the flight material never overrides it — the flight wings are driven by a per-renderer property block instead — so a worn membrane sharing that material inherits 0.22 m of displacement, pinned along an axis measured off a differently-shaped mesh. `WingsuitWornMembrane` carries its own anchor and 0.035 m of wind: cloth on somebody standing about, not cloth in a 24 m/s glide. `WingsuitRecolor.Membrane` names **both**, so both take the wearer's suit colour.
- **The airflow handed to the shader is bent upward** (`upwardBias` 0.55). The honest vector looks wrong: a wingsuit meets air nearly edge-on and `ClothWind` displaces *along* the wind, so true airflow ripples the membrane lengthwise instead of filling it. Relatedly, **the trailing edge is free all the way to the hip corner** — `ClothFreedom` is a gradient along ONE axis and this wing is anchored along an L. Small at the shipped amplitude and left; fixing it means a vertex-colour mask.
- **The wings ship SWITCHED OFF on the asset, and that is load-bearing rather than tidy.** `WornSeat` scales a worn item so its measured size matches the fit, and `ItemBounds` measures only the renderers that are on — so a visible pair of 0.93 m spars made the folded suit measure 2.5 m across and the pack on the wearer's back was scaled to a sliver 8 cm tall. That was "I cannot see it when I equip it", twice. `WingsuitWings.Awake` also turns them off and at runtime that does land before the seat, but **an ordering dependency between a MonoBehaviour's Awake and a static call is not something the editor can show you** — Awake does not run there, so every edit-mode check of it lies. `WingsuitBuilder` therefore disables them on the saved prefab, which makes folded the asset's own truth and Awake a belt to the braces.
- **Do not reparent anything inside the nested model.** The spars want to travel with their membranes, and parenting them to one is the obvious way to say so. Unity refuses it: the model arrives as a nested prefab instance, and **the interior of one cannot be restructured** — loudly in the editor, and at build time the reparent appears to take and is gone by the time the asset saves. `WingsuitWings.AttachTo` seats each spar on the same bone at the same fit as its membrane instead. Both are authored on one origin, so they coincide exactly.
- **Two authored fits, not one mirrored.** The membranes are already true mirrors in the model, and a humanoid rig's left and right arm bones are *not* mirror-image frames — the gauntlets pay for that with a negative scale and a hand-derived dorsal axis. **`WingsuitWings.Detach` must run before the item is destroyed**, because a reparented membrane is no longer in the item's hierarchy: two wings would hang off the astronaut for the rest of the session. And **a gauntlet fired mid-glide carries its wing with it** — the better of the two artefacts, but an artefact.
- **The recolour has its own table**, not an entry in `SuitPalette.Relationships`, because `SuitCustomizationTests` asserts every name in that one exists on `astronaut.fbx`. Worn gear is painted by `PlayerIdentity.Repaint`, which the item calls when it seats itself — a suit colour arrives as a NetworkVariable change and gear worn afterwards has simply missed it.
- **`WingsuitBuilder` owns the whole prefab.** `SaveAsPrefabAsset` replaces it wholesale, so anything added by hand is stripped on the next run — the wing pack lost its `NetworkObject`, its `PickupableItem` and both savers exactly that way, with no error anywhere.
- Tests: `WingsuitFlightTests` (no self-made energy; glide ratio and stall in band; a hands-off glide always descends) and `WingsuitControlTests` (the nose is aimed and stays, the rudder is pushed and decays, a deploy carries the pilot's motion, a flown arrival is free and a dive is not) — in [Editor/Tests](Assets/Game/Editor/Tests), because they touch Assembly-CSharp types.

## Extending

1. **Retune the feel** in `WingsuitFlightConfig`'s constructor or its serialized copy on the prefab. After touching mass, area, the lift slope, the stall angle or either drag term, read `StallSpeed` and `BestGlideRatio` back rather than assuming — `WingsuitFlightTests` pins both bands.
2. **Change how it looks, not how it flies:** `WingsuitPose`'s bank and response, `WingsuitWings`' billow and upward bias, and `glide.py`'s pose constants.
3. **A rope while gliding is not composed today.** `Step` takes a `towAcceleration` and would carry a grapple's pull natively, but the hook moves the player by writing velocity, which a glide overwrites. Nothing arbitrates them and deploying while tethered is not blocked. If it bites, fold the wings on `PlayerMovement.IsTethered`, or wire the rope into the tow term properly.
4. **The models:** `_Source~/models/gear/wingsuit.py` (flight) and `wingsuit_worn.py` (worn), with the decompositions and the membrane's own frame in `wingsuit_BUILD.md` and `wingsuit_worn_BUILD.md`. The loft itself — chord falloff, camber, hem, skin taper — is shared in `_wingsuit.py`, so a change to the cloth's shape reaches both. Never re-run either generator over the `.blend` it produced.
