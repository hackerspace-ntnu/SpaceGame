---
system: Jetpack
layer: items
summary: "Two vectoring motors worn on the back: thrust follows the nozzles, hold Space or fall, heat is the limit"
paths:
  - Assets/Game/Scripts/Gear/Jetpack
  - Assets/Game/Scripts/Characters/Player/Movement/JetpackFlight.cs
  - Assets/Game/Scripts/Characters/Player/Movement/JetpackPose.cs
  - Assets/Game/Scripts/Characters/Player/Movement/JetpackThirdPerson.cs
  - Assets/Game/Scripts/Items/Equipped/JetpackItem.cs
  - Assets/Game/Scripts/Items/Equipped/JetpackNozzles.cs
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/JetpackHeatGaugeSource.cs
  - Assets/Game/Editor/Items/JetpackBuilder.cs
  - Assets/Game/Art/Shaders/Effects/JetFlame.shader
  - Assets/Game/Art/Shaders/Effects/JetSmoke.shader
  - Assets/Game/Prefabs/Items/Equipment/Jetpack.prefab
  - "Assets/Game/Art/Models/_Source~/models/gear/jetpack.blend"
  - "Assets/Game/Art/Models/_Source~/models/gear/jetpack_export.py"
  - "Assets/Game/Art/Models/_Source~/models/gear/jetpack_mirror.py"
symptoms:
  - "a double tap of Space lifts me off and then puts me straight back down"
  - "the jetpack pushes me forward the instant I press W"
  - "holding W does the same thing whether I look up or down"
  - "the motors cut out and never come back on"
  - "the pack cools down while I am hanging in the air"
  - "letting go of Space parks me in the air instead of dropping me"
  - "I take fall damage on every landing"
  - "a quicksave in mid-air cools the pack down for free"
  - "I reload a save and the pack has taken off on its own"
  - "the pods do not move when I steer"
  - "the pods swing but the flames stay pointing straight down"
  - "one pod vectors and the other one does not"
  - "the left pod is inside out, or a different size from the right one"
  - "the nozzle tips never go red however hot the pack gets"
  - "the flames are grey, or there are no flames at all"
  - "there is no smoke at all, however hot the pack gets"
  - "the flames work but the pack has never once smoked"
  - "the flame is a tiny stub at part throttle"
  - "a landed pack stands on the sand with its flames still lit"
  - "the pack burns a small flame while I walk about with it on my back"
  - "I fly along stuck in the jump animation"
  - "I fly along running on the spot in mid-air"
  - "I cannot see the pack, the flames or the tips while flying it"
  - "the camera stays behind me after I land"
  - "the smoke cloud follows me instead of trailing behind"
  - "the flame grows back up into the fuel tank"
  - "there is a second column of smoke standing where the pack is not"
  - "another player's pods do not move, or point somewhere slightly different from mine"
  - "the heat gauge never appears, or fills toward danger instead of emptying"
  - "the motors do not sit on the backpack rig, they float out beside the body"
  - "the flames hang in the air beside the pack instead of at the nozzles"
  - "the jetpack switches itself off in mid-air while I am working the throttle"
  - "I cannot come down without holding another key"
  - "the pack sits crooked across my back, but only while I am flying"
  - "I leash another player and the jetpack cannot get either of us off the ground"
  - "hauling somebody on a rope overheats the pack much sooner than flying alone"
  - "tying my rope to a rock or a hull makes the jetpack far more powerful"
  - "my shots leave from behind me and hit my own back while the jetpack is lit"
reads_with: [BodyEquipment, PlayerCharacter, Wingsuit, LeashSystem, Multiplayer, Persistence, Visor]
updated: 2026-09-09
---

# Jetpack

Two vectoring motors worn on the back. Double-tap Space anywhere — the ground included — and the
player's own body is flown on four rate-limited nozzles. **One key flies it: hold Space to climb,
let go and you fall.** Nothing is
spawned and nothing is mounted, the same bargain [Wingsuit](Wingsuit.md) makes.
Design: [2026-09-07-jetpack-design.md](../../superpowers/specs/2026-09-07-jetpack-design.md).

**Scope:** [Gear/Jetpack/](Assets/Game/Scripts/Gear/Jetpack) (own asmdef), [JetpackFlight.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackFlight.cs), [JetpackPose.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackPose.cs), [JetpackThirdPerson.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackThirdPerson.cs), [JetpackItem.cs](Assets/Game/Scripts/Items/Equipped/JetpackItem.cs), [JetpackNozzles.cs](Assets/Game/Scripts/Items/Equipped/JetpackNozzles.cs), [JetpackHeatGaugeSource.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/JetpackHeatGaugeSource.cs), [JetpackBuilder.cs](Assets/Game/Editor/Items/JetpackBuilder.cs), [JetFlame.shader](Assets/Game/Art/Shaders/Effects/JetFlame.shader), [JetSmoke.shader](Assets/Game/Art/Shaders/Effects/JetSmoke.shader).
**Related:** [BodyEquipment.md](BodyEquipment.md) (the torso slot, the double-Space), [PlayerCharacter.md](PlayerCharacter.md) (the body it takes over), [Wingsuit.md](Wingsuit.md), [LeashSystem.md](LeashSystem.md) (what a lift is lifting), [Multiplayer.md](Multiplayer.md), [Persistence.md](Persistence.md), [Visor.md](Visor.md).

## Model

- **A back item, and the third of them.** `EquipKind.Back`, worn on the pack's lash rail, fired by a double tap of Space through `BodyEquipmentController`'s back channel. One torso slot, so the jetpack, the wing pack and the wingsuit are **mutually exclusive with no rule needed**.
- **It takes off from standing**, unlike the other two — a vertical takeoff is the point, so `CanUse` has no ground test at all. Its one refusal is an overheated pack, and that refusal logs.
- **The double tap lights the pack; it does NOT put it out.** Space is the throttle, so a working flight is press–release–press and a pilot trips the 0.3 s double-tap window constantly. A toggle would read that as "motors off" and drop them out of the sky. Landing ends a flight; letting go of Space is how the pilot gets there.
- **Thrust follows the NOZZLES, never the keys, and that one rule is the learning curve.** `JetpackVector.Command` turns input into a *commanded* deflection; `Advance` swings the real nozzles toward it at 110°/s; `JetpackStep` pushes along where they have got to. A press starts them moving and the push arrives as they arrive — you fly arcs rather than corners and set a turn up before you need it (`GDC-L1-FEEL-0008` for the commitment, `GDC-L1-DESIGN-0005` for depth out of one rule rather than added mechanics). It does not break `GDC-L1-FEEL-0002`: the input is *heard* on the frame of the press and the pods and flames show it; what is deliberate is the resolution time.
- **The keys pick a direction, the look picks how hard.** WASD choose a heading in the wearer's horizontal plane; `PlayerLook.Pitch` scales how far over the nozzles rake, so looking down while holding W goes flat and fast and looking up climbs on the same key. The look is a **multiplier on a key demand, never a term added to it** — added, a burn the player was not asking for would drift wherever they glanced. `NeutralDeflectionShare` (0.7) is the room left for it: full stick alone reaches 28 of the 40 available degrees. Without that share the keys saturate the clamp and where you are pointed means nothing at exactly the moment you are asking for the most.
- **Yaw is the body's, and there is no rudder.** `PlayerLook.SetFlying` stays **false**, unlike the wingsuit: the mouse is still the ordinary look and still yaws the body. The nozzles are in the wearer's frame, so turning the body swings the thrust. That is the whole steering model.
- **One key, two states: Space held is thrust, Space released is a FALL.** Thrust is 6 s from cold; releasing idles the motors and the pilot falls at this world's full 18 m/s², and it is the **only** recovery they can ask for, at 5 heat/s. There is **no key for down**: the crouch cut it replaced was a second way to say the same thing that also happened to be the only way to cool, which made a hidden key mandatory rather than optional (`GDC-L1-UX-0005` on button economy, `GDC-L1-DESIGN-0007` on cutting the rule that does not earn its cost).
- **Nothing catches the pilot, and that is what altitude costs.** There is no descent servo and no hover: a fall is arrested by lighting the motors again, so every metre climbed is a metre to pay for and a landing is a burn the pilot has to aim. It also prices the heat budget in a second currency — a pilot who spends the gauge at 30 m has to spend the fall getting it back (`GDC-L1-SYS-0008`: one resource, two rates, and the flight is whatever they add up to).
- **`JetThrottle.Cut` is not a control; it is what an overheat does to you — and it now falls exactly like a release.** The two differ in the heat budget alone: a release cools at 5/s and relights on the next frame, an overheat cools at 10/s and relights at nothing until the latch clears at 20. What tells them apart on screen is the tips glowing red, the heavy smoke and the visor gauge, never the flames — those are out under both (`GDC-L1-UX-0003`: the flame answers "are the motors lit", and only that).
- **Overheat is a LATCH, not a threshold.** Reaching 100 cuts the motors and sets `Overheated`; nothing relights until heat falls to 20. A bare threshold relights one frame's worth of cooling later and reads as a stutter rather than as a fall. The fall is the whole punishment — `OrnithopterCrash.ImpactDamage` on closing speed prices it, so no damage rule of its own was needed.
- **It flies in third person, and that is a readability fix rather than a preference.** The machine is on the wearer's BACK: in first person the pods vectoring, the flames scaling and the tips going red are all behind the camera, and the visor gauge is the only feedback left (`GDC-L1-UX-0003`). `JetpackThirdPerson` steps the existing lens back, up and **off the shoulder** while flying, and puts it exactly where it found it afterwards. The sideways step is the aiming half of the same fix: dead behind the helmet the player's own body sits under the crosshair, so the target is the one thing the view hides. It has to clear the PACK and not just the body — the pods stand well outboard of the shoulders — which is why `shoulder` is 1.35 m rather than the half-metre a bare third-person shot would need. Serialized off on `JetpackFlight` for anyone who wants the first-person view back.
- **The pilot is posed STANDING while flying, not falling.** `SetGliding` leaves `PlayerMovement`'s animator writes running — correctly, since a body with no animator updates is the bug the tether was written to stop repeating — so the animator is told, truthfully, that the player is airborne. `JetpackFlight.PoseAnimator` overwrites that at order 150. There is no flight clip yet; the idle is the honest stand-in.
- **It will lift somebody on a rope, and it burns harder to do it.** A leash shares one acceleration out between two masses, so an equal-weight passenger would otherwise turn the +12 m/s² climb into a −3 m/s² **sink** — and beating that with raw thrust means a pack that flies nearly twice as fast for every solo pilot too. `JetpackLift` answers the load instead: the pack multiplies its push by what is hanging off it, and bills the **same factor as heat**, so a passenger costs burn time rather than nothing (`GDC-L1-SYS-0008`). A leashed player rises at about 3 m/s² — roughly 25 m on one burn of 4.3 s instead of a solo pilot's 170 m in 6 s. **The clamp is what keeps it a pack and not a crane:** past `MaxLiftRatio` the assist stops growing while the load's real weight stays in the physics, so something much heavier sinks the pair on its own with nothing having to classify it (`GDC-L1-SYS-0007`). **A creature counts too, and until 2026-09-09 was billed for and never left the ground:** `LeashLoad` reads a Rigidbody's mass whether or not it is kinematic, while the rope's branch for a NavMesh creature discarded the vertical half of every pull ([CarriedAgent.md](CarriedAgent.md)). Nothing here changed — a rat is a ratio of 0.5 and rises at about +6 m/s², a nomad is 1.0 and rises like a second player at +3.
- **Heat belongs to the pack, not to the flight.** `End` deliberately does not reset it, and `JetpackFlight.Update` keeps cooling while the pack is worn but stowed, so the walk back from a hard flight *is* the cooldown.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `JetpackConfig` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackConfig.cs) | Every tunable: thrust, drag, vectoring, launch, heat |
| `JetpackHeat` / `JetThrottle` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackHeat.cs) | Pure struct: the budget, the latch, `Resolve` (what the pack will actually do) |
| `JetNozzle` / `JetpackVector` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackVector.cs) | Pure: `Command`, the rate-limited `Advance`, `WorldThrust`, the wire round trip |
| `JetpackStep` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackStep.cs) | Pure: one physics step — thrust, this world's own gravity, two drags |
| `JetpackLift` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackLift.cs) | Pure: `Factor` (what a load does to push AND heat) and `PairClimb` (what the pair actually gets) |
| `LeashLoad` | [Artifacts/Leash/](Assets/Game/Scripts/Items/Artifacts/Leash/LeashLoad.cs) | Kilograms hanging below a body on **taut** ropes. In Assembly-CSharp, which the jetpack asmdef cannot see |
| `JetpackLandingConfig` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackLandingConfig.cs) | `: OrnithopterCrashConfig`. Safe 9 m/s, lethal 30 — the wingsuit's figures |
| `JetpackFlight` | [Player/Movement/](Assets/Game/Scripts/Characters/Player/Movement/JetpackFlight.cs) | Owner only. State, both hand-overs, the landing. Execution order **150** |
| `JetpackPose` | [Player/Movement/](Assets/Game/Scripts/Characters/Player/Movement/JetpackPose.cs) | Every machine. Leans the hips from the NOZZLES. Order **920**, before `PlayerHeadLook` |
| `JetpackThirdPerson` | [Player/Movement/](Assets/Game/Scripts/Characters/Player/Movement/JetpackThirdPerson.cs) | Owner only, on while flying. Moves the EXISTING lens back; sphere-cast wall pull-in. Order **960** |
| `JetpackItem` | [Items/Equipped/](Assets/Game/Scripts/Items/Equipped/JetpackItem.cs) | `UsableItem`, `UseAuthority.Owner`. The gesture, the hold stream, the save bags |
| `JetpackNozzles` | [Items/Equipped/](Assets/Game/Scripts/Items/Equipped/JetpackNozzles.cs) | Every machine. Swings the pods, four flames, tip glow, smoke. `Resolve` is public |
| `JetpackHeatGaugeSource` | [UI/HelmetHUD/](Assets/Game/Scripts/Presentation/UI/HelmetHUD/JetpackHeatGaugeSource.cs) | `IVisorGaugeSource`, inverted: it draws burn REMAINING |
| `JetpackBuilder` | [Editor/Items/](Assets/Game/Editor/Items/JetpackBuilder.cs) | **Tools ▸ SpaceGame ▸ Items ▸ Build Jetpack**. Owns the whole prefab, and verifies it |

## Tunables

| Field | Default | Effect |
| --- | --- | --- |
| `ThrustAcceleration` | 30 m/s² | Against 18 of gravity, 12 of climb. An acceleration, not a force, so feel is not coupled to the player's mass |
| `Gravity` | 18 m/s² | This world's own. Unity's is off for the duration — one source of weight |
| `LiftAssist` / `MaxLiftRatio` | 0.4 / 1.5 | How much of a towed body's weight the pack answers for (0 = a passenger is unliftable, 1 = free; 0.4 rises an equal-weight one at ~3 m/s²), and the load it stops answering for, as a multiple of the pilot's mass. One decision: the second bounds the boost, and the load's real weight is never clamped |
| `HorizontalDrag` / `VerticalDrag` | 0.9 / 0.12 /s | The first sets the top speed; the second is low because a fall should stay a fall. Exponential, so a big step cannot reverse the velocity |
| `MaxDeflectionDegrees` | 40° | How much thrust can be aimed sideways — the top speed as surely as the drag is |
| `VectorRateDegreesPerSecond` | 110°/s | **The learning curve.** Raise it far and the jetpack becomes a hover car |
| `LookPitchShare` / `NeutralDeflectionShare` | 0.7 / 0.7 | How much the look scales the keys, and the room left for it to work in. They move together |
| `LaunchKick` / `SpeedCarry` | 7 m/s / 1 | The boost, as a velocity not an impulse, so a takeoff reads the same however it started |
| Heat: thrust / descend / cut | 16.67 / −5 / −10 /s | 6 s of climb; every second of it bought with about 3.3 s of falling; dead motors cool in ten |
| `OverheatAt` / `RelightAt` | 100 / 20 | The cut, and how far it must fall before the motors take again |
| `WarnFraction` | 0.7 | Where the tips glow, the extra smoke starts and the gauge goes amber. One number, three readouts |
| `JetpackBuilder.SizeScale` | 2 | How much bigger than modelled the pack is worn and carried. Applied in Unity, never baked into the hand-built .blend |
| `JetpackThirdPerson` distance / height | 4.2 / 0.9 m | Where the lens stands while flying. Far enough back to see the pods, which is the point |
| `JetpackBuilder.FlameWidthShare` / `FlameLengthPerRadius` | 1.0 / 7.0 | The plume's size, as fractions of the nozzle disc it comes out of. A base at the full bore and ~1 m of flame on a worn pack — 2.5x the first, restrained cut, which was legible but never dramatic. **Baked into the prefab**, so neither does anything until Build Jetpack is re-run |
| `JetpackNozzles.flameCutoff` | 0.02 | Throttle below which the flames are switched off outright. A threshold, not a taste — see Gotchas: the shown throttle only ever *approaches* zero |
| `JetpackNozzles.smokePerSecond` / `smokeSpread` | 28 /nozzle, 1.2 m/s | The exhaust trail, and how far the four columns billow apart. Only a burn smokes, and burns are six seconds long, so the rate carries the whole trail; puffs are born 0.45–0.70 m and grow past 2 m, thin enough (alpha 0.42) that the depth comes from overlap rather than from any one of them |

## Flows

1. **Launch.** Double Space → back channel → `CanUse` (refuses only an overheated pack, loudly) → owner `Use()` toggles → `Begin()` keeps the horizontal speed the player had, writes `LaunchKick` into Y, turns Unity's gravity off, `PlayerMovement.SetGliding(true)`.
2. **Fly.** `FixedUpdate`: read the load off the ropes (`LeashLoad.HangingMassOn` → `JetpackLift.Factor`, re-asked every step because a rope is tied and cut mid-flight), read the asked-for throttle, correct it through `JetpackHeat.Resolve`, step the heat **at the lift factor**, command and advance the nozzles, step the velocity **at the same factor**, write it to the Rigidbody, check for a landing. The body's yaw is *read* off the transform — this never writes rotation, because `PlayerLook` owns it.
3. **Show.** The item streams throttle, heat and the nozzle rotation at 15 Hz; `JetpackNozzles` swings both pods about their measured gimbals, stretches four flame cones, ramps the tip glow and emits smoke. The owner overwrites the same three values from its live flight every frame, so its own pods move at frame rate.
4. **Trail.** Every lit nozzle emits smoke while the motors run — thrown out along its own flame cone's axis into a WORLD-space system, so the puff hangs where it was born while the player flies out from under it. Past `WarnFraction` a much heavier second rate is added on top; that layer is the warning, the first is exhaust.
5. **Overheat.** Heat hits 100 → latch → `Resolve` forces `Cut` whatever the pilot holds → the player falls, tips red and smoking, until heat is at 20. The fall itself is the one a released key gives; what the latch takes away is the relight.
6. **Land.** `PlayerMovement.IsOnGround` **and descending**, or `OnCollisionEnter` for a cliff face. Closing speed is billed through `OrnithopterCrash`, then `End()` hands the body back with `CarryMomentum()`.

## Multiplayer

- **No new message and no new animator parameter.** `UseChannel.Release` steps aside for an item that is `IsContinuous` and still `WantsHold`, so the double tap's `Press(); Release();` opens a 15 Hz stream that outlives the press and ends when the item says it is done — which the jetpack says when the flight ends. Each tick carries `P = (throttle, heat, 0)` and `R =` the nozzle deflection, which is literally a rotation. `PresentHold` fires on every machine.
- The keepalive re-sends at least every 0.2 s, so a **late joiner is correct within a fifth of a second** rather than never.
- `JetpackFlight` is added **only on the owner** — the player's `NetworkTransform` is owner-authoritative, so a flight simulated anywhere else is a second, divergent one fighting the replicated pose. `JetpackPose` and `JetpackNozzles` run everywhere. `UseAuthority.Owner`, because a server-applied velocity is overwritten by the owner, silently.
- **A lift needs nothing on the wire.** The factor is derived on the pilot's own machine from ropes every machine already builds and positions every machine already replicates, so a client pilot computes the same number the host would. The passenger's end of the rope still resolves on the passenger's machine, through `LeashedBody` as any other pull does.
- The dropped prefab is registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset).
- **Verify on a real client:** take off past the host; watch the pods vector, the flames scale with throttle, the tips go red and smoke at overheat, and the fall when the motors cut.

## Persistence

- The pack is saved by the torso slot (`BodyEquipmentSaveable`, key `body`) like any gear.
- **`heat` and `hot` ride the item's `ItemState`.** Without them a quicksave is free coolant and the whole budget is optional. Applied in `OnEquipped` through `JetpackFlight.SetHeat`. **The lift adds no saved state, deliberately** — its factor is re-derived every step from live ropes, so what must survive a reload is the ropes (`LeashSaveable`) and this heat, both of which already do.
- **`jet` and `noz` carry a flight in progress**, resumed through `IItemDeferredRestore` entering *already flying* — the mistake `OrnithopterSaveable` made first. `Resume` restores an overheat rather than clearing it, which is the difference between saving mid-fall and saving to cool down.
- **`SetHeat` and `Resume` are two calls on purpose: `Resume` LAUNCHES.** They were one call until a pack restored merely hot took off on its own.
- Dropped on the sand it carries `SaveableEntity` + `TransformSaveable` with a stamped `prefabId`.

## Gotchas

- **A vertical takeoff needs "descending" in the landing test, not a timer.** The wingsuit lands on `IsOnGround` alone because it refuses to deploy on the ground; this one launches from standing, so the step after `Begin` the probe still reports ground and a bare check ends the flight before it starts. `body.linearVelocity.y > 0.01f` returns early. Rising is not landing — and the same one rule gets scraping along a dune under thrust right, with no timer to expire at the wrong moment.
- **The FBX flattens the pod hierarchy, so a pod is a SIDE and not a parent.** `_exportlib` writes every mesh as a direct child of the model root with its world transform baked onto its own node; `MOUNT_Jetpack_L`/`_R` arrive as empty **siblings**. `JetpackNozzles` groups by the `_R`/`_L` suffix (`_ItemR`/`_ItemL` on the carried pair). Grouping by `part.parent` gives one pod of ten parts, no differential roll, and a gimbal measured between the two pods.
- **The mirror arrives as a NEGATIVE, non-uniform `lossyScale` on every left-hand part** — (−2.57, −3.02, −2.57) on an exhaust, (±11.93, ±8.94, ±5.10) on a housing. Anything parented under one inherits it mirrored and squashed differently per axis, which is why the flame cones hang off the **model root** (identity scale) and are sized in world metres divided back through the parent's scale. `JetFlame.shader` is `Cull Off` partly for this.
- **The exhaust meshes are DISCS, not plumes, and "the longest axis is the length" fails here.** They measure 5.9 × 2.6 × 5.9 cm: the longest axis is a *tie* between the two across the disc and the real outflow is the **shortest** one, the disc's normal. The wingsuit's spar taught the longest-axis rule and this is where it inverts. `JetpackBuilder` generates a unit cone instead — base y = 0 radius 1, tip y = 1 radius 0 — so the shader holds no measurements at all, and it takes the outflow SIGN from the pod's own housing rather than assuming, or the flame grows back up into the tank.
- **An FBX-embedded material cannot be edited, and that silently defeated the heat glow.** The pods shipped with `Material.002`–`Material.012` inside the FBX (`materialLocation = InPrefab`), where `EnableKeyword("_EMISSION")` appears to work, dirties nothing and is gone on the next import — so the tips never went red, with a clean console. URP/Lit ignores `_EmissionColor` without that keyword, and a `MaterialPropertyBlock` **cannot enable a keyword**. `JetpackBuilder` extracts the materials to `*_Materials/` and arms emission at BLACK, so nothing changes until the pack is hot.
- **`Awake` does not run in the editor, so `JetpackNozzles.Resolve` is public and the builder calls it.** Everything it binds is a NAME in a hand-built model that keeps being re-arranged — the pods have already been yawed 90° once — so a rename would ship a pack whose pods never move and whose flames never light, with a clean console, findable only by flying one. `VerifyPods` instantiates the **saved asset** and asserts 4 pods and 8 flames (two models, two pods each, two flames a pod). A check that only runs in play mode is a check nobody runs.
- **The two angles do not come back off the wire symmetrically.** Unity composes `Euler(x, y, z)` as Z then X then Y, so `JetNozzle.Rotation` sends up to `(sin roll, cos roll·cos pitch, cos roll·sin pitch)`. The pitch falls out of `atan2(z, y)` because the cosine cancels; **the roll needs `asin(x)`**. Reading both the same way inflated the roll by the pitch — 17.25° came back as 18.71° — small enough to look like rounding, large enough to put a peer's pods visibly off the owner's. `JetpackVectorTests.NozzleSurvivesARotationRoundTrip` pins it.
- **An idle flame floor on a SMOOTHED throttle never switches off.** `JetpackItem` eases the shown throttle toward its target exponentially, so after a landing it decays toward zero without ever arriving — and `Mathf.Max(throttle, idleFlame)` then read that hair above zero as "lit" and drew six tenths of a plume on a pack standing on the sand, for ever. The floor is gone and `flameCutoff` puts a real zero under the value instead. Any future floor under a smoothed number needs the same treatment.
- **Landing is now a burn the pilot aims, and the landing config was not retuned for it.** A release falls at 18 m/s², so `JetpackLandingConfig`'s safe 9 m/s is reached about 2.3 m below wherever the key came up. Coming down from height without feathering the motors near the ground is worth real damage, which is the intended cost of free flight — but it is the number to move first if landings read as punishing rather than as demanding.
- **A rope shares an ACCELERATION, so more thrust is not the fix for lifting somebody.** The leash resolves its two ends by mass share, and the pair then moves at the mass-weighted average of the two accelerations: `thrust/(1 + M/m) − g`. Raising `ThrustAcceleration` until a passenger lifts needs it past **twice gravity**, which doubles every solo pilot's climb and cruise as well. `JetpackLift.Factor` is the seam that keeps the load's answer out of the solo numbers (`JetpackStepTests.FlyingAloneIsUntouched`), and it multiplies the push and the thrust heat and **nothing else**: scale the cooling too and a heavy load cools faster than a light one, scale the push alone and the lift is free — which is the one way this becomes the best reason in the game to carry a rope. `JetpackHeatTests.ALiftIsBilledInProportionToTheThrustItBuys` and `ALoadDoesNotChangeCooling` hold both halves.
- **Three tests decide whether a rope counts as cargo, and each closes a free-thrust route.** `LeashLoad.HangingMassOn` requires the rope to be **taut** (a slack coil on the sand is not a load, and putting the load down gives the thrust back with no bookkeeping), the far end to have a **Rigidbody** (bare geometry is an anchor, so tying yourself to a cliff must not read as maximum cargo) and that end to be **below** (a sideways rope is drag, an overhead one is the opposite of a load). Drop any one and the pack has a power-up nobody authored.
- **`JetpackPose` reads the nozzles, not the motion**, the opposite of `WingsuitPose` and right for the opposite reason: a glider's attitude *is* its flight path, but a jetpack hanging still under full rake is leaning hard while going nowhere, and motion would show none of it.
- **`WornVisual` switched the smoke system OFF the moment the pack was worn, which is the only time it flies.** The system is a top-level child of the item root — deliberately, because both models emit through the one system — and `SetForm` hid every top-level child that was not the shown model, asking `GetComponentInChildren<Renderer>` to decide what counted. **A `ParticleSystemRenderer` is a `Renderer`**, so the smoke read as a third model and was switched off with the carried pack. The flames were fine (they are children of the model), nothing logged, and no amount of tuning the rates could have helped: the object was inactive. `WornVisual` now switches only `MeshRenderer` / `SkinnedMeshRenderer` children, and `JetpackBuilder` flies a worn pack for six frames and asserts puffs come out.
- **Nothing outside play mode could reach the emit path, which is why it survived so long.** The editor never calls `LateUpdate` and `Time.deltaTime` is zero there, so `JetpackNozzles.Tick(dt)` is public and takes its own step — the same argument that made `Resolve` public. A check that can only be run by flying is a check nobody runs.
- **Both models are resolved, but only the ACTIVE one smokes.** `WornVisual` swaps carried and worn by enabling a GameObject, so a component that had only found the form that happened to be on at `Awake` would stop vectoring the moment the pack was worn — but emitting from the hidden one leaves a second column of smoke standing where the pack is not.
- **A script-created `ParticleSystem` has a NULL renderer material and draws NOTHING, silently.** No warning, no magenta — the smoke simply never existed, at any heat. That is how the first cut shipped. `JetpackBuilder` now creates and assigns `JetSmoke.mat`; if `Shader.Find` ever fails it says so loudly rather than leaving the field empty.
- **The throttle must shorten the flame in exactly ONE place.** It was spent twice — `JetpackNozzles` scaled the cone's Y by it *and* the shader divided its length coordinate by the same number, discarding the far end of a cone that had already been scaled down. A hover drew a stub of a stub, small enough to read as no flame at all. The transform owns the length now; the shader takes the throttle only as brightness and as how far up the noise may eat.
- **A flame rooted at the exhaust disc's CENTRE is half-buried.** The cone starts at the disc's outer face — half the mesh's thickness along the outflow — or the first few centimetres of every plume sit inside the nozzle geometry, which at these sizes is most of the visible root.
- **The size doubling lives in Unity, not in the .blend.** `jetpack.blend` is hand-built and still being edited, so a scale baked into the model is a change that has to survive the user's next save. `WornSeat` and `ItemGrip` both size a model to a number, so `SizeScale` costs one multiply and touches nothing the artist owns.
- **The SIZE doubling is Unity's, but the SPACING is the .blend's, and the two are not interchangeable.** `SizeScale = 2` doubles the gap between the pods as well as the pods, so a pair authored on the lash rail's tips (±0.8925) stands almost 4 m across a wearer and misses the rig. `jetpack_mirror.py` seats them at HALF the tip span instead. Fixing this in Unity is impossible by construction: `WornSeat` scales the whole model to `WornFit.size`, so a smaller number shrinks the pods along with the gap and the pack sits exactly the same way on the back.
- **A lean is a SWING, not a pitch multiplied by a roll.** `JetpackPose` composed
  `AngleAxis(pitch, right) * AngleAxis(-roll, forward)`, and the product of two rotations about
  different horizontal axes carries a twist about the VERTICAL of roughly pitch·roll/2 — measured
  at 3.6 degrees on both leans at 20, and **13.5 degrees at full stick on both**. Nothing in the
  code asks for yaw, so the pack read as sitting crooked across the pilot's back and only ever in
  the air, which sends anyone debugging it to the worn model and the seat. Lean the body's up and
  rotate onto it by the shortest arc (`Quaternion.FromToRotation`) and the twist is zero by
  construction, with both leans landing exactly where they were asked to. `WingsuitPose` still
  composes its two the old way.
- **Stepping the lens away from the eye moves the AIM with it.** `AimProvider` treats "the view IS
  the eye" as the case with no parallax to correct, and that is decided by comparing transforms —
  which stays true when the jetpack displaces the player's own lens, so the ray started metres
  behind the player and ran through their own back on the way out. `JetpackThirdPerson` hands the
  provider an eye anchor (`SetEyeAnchor`) standing at the captured rest pose for as long as it is
  enabled, and the ray goes back to being built by convergence. The anchor is a child of the LENS
  so it wears the look rotation live; only its position is written back each frame.
- **`JetpackThirdPerson` moves the existing camera; it does not spawn one.** The mount's third-person camera is a whole subsystem — spawned unparented, tagged with `MountRuntimeCamera`, swept by an editor hook because orphans survived domain reloads and rendered over the player's own view for days. The offset is written in `LateUpdate` (after `PlayerLook`'s `Update`) and **always from the pose captured on enable**, never from the current one, or each frame compounds on the last and the lens walks off into the desert. `PlayerLook` still owns the lens's ROTATION and is left alone.
- **`packSize` 1.8 is a design decision, not a measurement.** The model is 0.67 m; the pack costs about what the ornithopter costs (the wing pack's 1.82), because that is what was asked for. `holdSize` 0.674 and `WornFit.size` 1.105 *are* measurements, printed by `jetpack_export.py` — re-pin them after any re-export that moved a spacing.
- **The flame's SIZE is baked into the prefab, not read at runtime.** `FlameWidthShare` and `FlameLengthPerRadius` are consumed once, by `JetpackBuilder.Place`, and end up as eight `localScale`s in `Jetpack.prefab`. Editing either constant changes nothing at all until **Build Jetpack** is re-run — no error, no warning, the old flames simply keep their old size. Measure the prefab's `JetFlame_*` scales afterwards rather than trusting the build: a builder run can execute stale code and still log success.
- **A re-export moves the model but NOT the generated flames.** The four flame cones are built by `JetpackBuilder` at the exhausts' measured positions and saved into the prefab, so re-exporting `jetpack_worn.fbx` with a different pod spacing leaves them standing at the old one — pods on the back, plumes hanging in the air a hand's width outboard, with nothing in the console. Re-run **Build Jetpack** after every export, and re-pin `WornSize` to the exporter's printed longest axis.
- **Worn gear is looked at, not reasoned about.** Tools ▸ SpaceGame ▸ Items ▸ **Preview Worn Gear** stands the jetpack, the wing pack and the wingsuit on a body with the rig shouldered and writes front/back/three-quarter shots to `Temp/WornGearPreview`. Six silent things decide where a worn item lands; a render settles in one look what measuring one of them cannot.
- **`JetpackBuilder` owns the whole prefab.** `SaveAsPrefabAsset` replaces it wholesale, so anything added by hand is stripped on the next run — the trap that cost the wing pack its `NetworkObject`, its `PickupableItem` and both savers, with no error anywhere.
- **`PlayerInputManager.JumpHeld` is new.** Every other consumer of Jump wants the edge; a throttle needs the state, and reconstructing it per call site is how two systems disagree about whether the key is down. Cleared in `OnDisable` like `CrouchHeld`.
- Tests: `JetpackHeatTests` (the durations as *durations*, that only thrust ever overheats, the latch, that a one-in-four duty cycle outlasts holding), `JetpackVectorTests` (the rate limit, the clamp, the look, the wire round trip, that thrust follows the nozzle) and `JetpackStepTests` (letting go falls exactly as a cut does, a fall keeps accelerating, raking the nozzles buys nothing without thrust, no energy is minted, and the whole lift arithmetic: a passenger rises slowly, a hull does not, flying alone is untouched, a load never slows the fall) — in [Editor/Tests](Assets/Game/Editor/Tests), because they touch Assembly-CSharp types.

## Extending

1. **Retune the feel** in `JetpackConfig` on the prefab. `VectorRateDegreesPerSecond` is the biggest single lever on how the machine plays; `MaxDeflectionDegrees` sets how much of the push can be aimed sideways, and the heat rates decide whether a flight is a rhythm or a single burn.
2. **Retune what it carries** with `LiftAssist` and `MaxLiftRatio`, which are one decision: the first is how briskly a passenger rises, the second is what stops being liftable at all. Ask `JetpackLift.PairClimb(pilotMass, loadMass, cfg)` rather than flying it — the player prefab is 80 kg, so two astronauts is a ratio of exactly 1.
3. **Change how it looks, not how it flies:** `JetpackNozzles`' glow and smoke fields, `JetpackPose`'s lean, and `JetFlame.shader`'s bands, taper and flicker.
4. **The model** `_Source~/models/gear/jetpack.blend` is HAND-BUILT and not reproducible from a script — `jetpack_mirror.py` only *arranges* it. Re-export with `jetpack_export.py`, then re-run **Build Jetpack**, which re-measures and re-verifies everything.
5. **Audio is not wired**, and **there is no flight clip**. `JetpackFlight` raises `Overheated` and `Landed` for exactly the first, and nothing listens yet. For the second, `PoseAnimator` forces the idle and `JetpackPose` leans it — a real clip would go on its own layer the way the wingsuit's Glide layer does, and `PoseAnimator` would then be deleted rather than retuned.
