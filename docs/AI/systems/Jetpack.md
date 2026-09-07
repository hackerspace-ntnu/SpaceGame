---
system: Jetpack
layer: items
summary: "Two vectoring motors worn on the back: thrust follows the nozzles, not the keys, and heat is the only limit"
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
  - "a quicksave in mid-air cools the pack down for free"
  - "I reload a save and the pack has taken off on its own"
  - "the pods do not move when I steer"
  - "the pods swing but the flames stay pointing straight down"
  - "one pod vectors and the other one does not"
  - "the left pod is inside out, or a different size from the right one"
  - "the nozzle tips never go red however hot the pack gets"
  - "the flames are grey, or there are no flames at all"
  - "there is no smoke at all, however hot the pack gets"
  - "the flame is a tiny stub while hovering"
  - "I fly along stuck in the jump animation"
  - "I fly along running on the spot in mid-air"
  - "I cannot see the pack, the flames or the tips while flying it"
  - "the camera stays behind me after I land"
  - "the smoke cloud follows me instead of trailing behind"
  - "the flame grows back up into the fuel tank"
  - "there is a second column of smoke standing where the pack is not"
  - "another player's pods do not move, or point somewhere slightly different from mine"
  - "the heat gauge never appears, or fills toward danger instead of emptying"
  - "letting go of Space is a fall, not a settle, so relighting after an overheat is pointless"
  - "the motors do not sit on the backpack rig, they float out beside the body"
  - "the flames hang in the air beside the pack instead of at the nozzles"
  - "the jetpack switches itself off in mid-air while I am working the throttle"
  - "I cannot come down without holding another key"
  - "coming down at full rake holds altitude and there is no reason to hold Space"
  - "the pack sits crooked across my back, but only while I am flying"
  - "my shots leave from behind me and hit my own back while the jetpack is lit"
reads_with: [BodyEquipment, PlayerCharacter, Wingsuit, Multiplayer, Persistence, Visor]
updated: 2026-09-07
---

# Jetpack

Two vectoring motors worn on the back. Double-tap Space anywhere — the ground included — and the
player's own body is flown on four rate-limited nozzles. **One key flies it: hold Space to climb,
let go to sink under power.** Nothing is
spawned and nothing is mounted, the same bargain [Wingsuit](Wingsuit.md) makes.
Design: [2026-09-07-jetpack-design.md](../../superpowers/specs/2026-09-07-jetpack-design.md).

**Scope:** [Gear/Jetpack/](Assets/Game/Scripts/Gear/Jetpack) (own asmdef), [JetpackFlight.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackFlight.cs), [JetpackPose.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackPose.cs), [JetpackThirdPerson.cs](Assets/Game/Scripts/Characters/Player/Movement/JetpackThirdPerson.cs), [JetpackItem.cs](Assets/Game/Scripts/Items/Equipped/JetpackItem.cs), [JetpackNozzles.cs](Assets/Game/Scripts/Items/Equipped/JetpackNozzles.cs), [JetpackHeatGaugeSource.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/JetpackHeatGaugeSource.cs), [JetpackBuilder.cs](Assets/Game/Editor/Items/JetpackBuilder.cs), [JetFlame.shader](Assets/Game/Art/Shaders/Effects/JetFlame.shader), [JetSmoke.shader](Assets/Game/Art/Shaders/Effects/JetSmoke.shader).
**Related:** [BodyEquipment.md](BodyEquipment.md) (the torso slot, the double-Space), [PlayerCharacter.md](PlayerCharacter.md) (the body it takes over), [Wingsuit.md](Wingsuit.md), [Multiplayer.md](Multiplayer.md), [Persistence.md](Persistence.md), [Visor.md](Visor.md).

## Model

- **A back item, and the third of them.** `EquipKind.Back`, worn on the pack's lash rail, fired by a double tap of Space through `BodyEquipmentController`'s back channel. One torso slot, so the jetpack, the wing pack and the wingsuit are **mutually exclusive with no rule needed**.
- **It takes off from standing**, unlike the other two — a vertical takeoff is the point, so `CanUse` has no ground test at all. Its one refusal is an overheated pack, and that refusal logs.
- **The double tap lights the pack; it does NOT put it out.** Space is the throttle, so a working flight is press–release–press and a pilot trips the 0.3 s double-tap window constantly. A toggle would read that as "motors off" and drop them out of the sky. Landing ends a flight; letting go of Space is how the pilot gets there.
- **Thrust follows the NOZZLES, never the keys, and that one rule is the learning curve.** `JetpackVector.Command` turns input into a *commanded* deflection; `Advance` swings the real nozzles toward it at 110°/s; `JetpackStep` pushes along where they have got to. A press starts them moving and the push arrives as they arrive — you fly arcs rather than corners and set a turn up before you need it (`GDC-L1-FEEL-0008` for the commitment, `GDC-L1-DESIGN-0005` for depth out of one rule rather than added mechanics). It does not break `GDC-L1-FEEL-0002`: the input is *heard* on the frame of the press and the pods and flames show it; what is deliberate is the resolution time.
- **The keys pick a direction, the look picks how hard.** WASD choose a heading in the wearer's horizontal plane; `PlayerLook.Pitch` scales how far over the nozzles rake, so looking down while holding W goes flat and fast and looking up climbs on the same key. The look is a **multiplier on a key demand, never a term added to it** — added, a hands-off descent would drift wherever the player glanced and the machine could not be parked. `NeutralDeflectionShare` (0.7) is the room left for it: full stick alone reaches 28 of the 40 available degrees. Without that share the keys saturate the clamp and where you are pointed means nothing at exactly the moment you are asking for the most.
- **Yaw is the body's, and there is no rudder.** `PlayerLook.SetFlying` stays **false**, unlike the wingsuit: the mouse is still the ordinary look and still yaws the body. The nozzles are in the wearer's frame, so turning the body swings the thrust. That is the whole steering model.
- **One key, two states: Space held is thrust, Space released is a powered descent.** Thrust is 15 s from cold; releasing is the descent — the motors idle to a steady 4 m/s sink with the nozzles still lit — and it is the **only** recovery a flying pilot can ask for, at 5 heat/s. Climbing and coming down is unbounded where a held burn is fifteen seconds. There is **no key for down**: the crouch cut it replaced was a second way to say the same thing that also happened to be the only way to cool, which made a hidden key mandatory rather than optional (`GDC-L1-UX-0005` on button economy, `GDC-L1-DESIGN-0007` on cutting the rule that does not earn its cost).
- **The descent is a servo, not a setting.** It solves for the push that cancels gravity *along the direction the nozzles happen to point*, damps the vertical speed toward `DescentSpeed` down, then caps the answer at `HoverAuthority`. So letting go is a lift-off in reverse: the pack settles onto its sink rate from a climb or a dive alike, and lands off it. Sinking at full rake needs a third more thrust than the cap allows, so it **drops faster than it asked to** — nobody wrote that rule, it falls out of solving along the real axis, and it is what stops a descent being free horizontal flight.
- **`JetThrottle.Cut` is not a control any more; it is what an overheat does to you.** Dead motors, dark nozzles, this world's full 18 m/s² — three times the sink of letting go. The dark flames are the only thing that separates the two from inside the helmet, which is why `ThrottleFraction` is zero for a cut and 0.35 for a descent.
- **Overheat is a LATCH, not a threshold.** Reaching 100 cuts the motors and sets `Overheated`; nothing relights until heat falls to 20. A bare threshold relights one frame's worth of cooling later and reads as a stutter rather than as a fall. The fall is the whole punishment — `OrnithopterCrash.ImpactDamage` on closing speed prices it, so no damage rule of its own was needed.
- **It flies in third person, and that is a readability fix rather than a preference.** The machine is on the wearer's BACK: in first person the pods vectoring, the flames scaling and the tips going red are all behind the camera, and the visor gauge is the only feedback left (`GDC-L1-UX-0003`). `JetpackThirdPerson` steps the existing lens back, up and **off the shoulder** while flying, and puts it exactly where it found it afterwards. The sideways step is the aiming half of the same fix: dead behind the helmet the player's own body sits under the crosshair, so the target is the one thing the view hides. It has to clear the PACK and not just the body — the pods stand well outboard of the shoulders — which is why `shoulder` is 1.35 m rather than the half-metre a bare third-person shot would need. Serialized off on `JetpackFlight` for anyone who wants the first-person view back.
- **The pilot is posed STANDING while flying, not falling.** `SetGliding` leaves `PlayerMovement`'s animator writes running — correctly, since a body with no animator updates is the bug the tether was written to stop repeating — so the animator is told, truthfully, that the player is airborne. `JetpackFlight.PoseAnimator` overwrites that at order 150. There is no flight clip yet; the idle is the honest stand-in.
- **Heat belongs to the pack, not to the flight.** `End` deliberately does not reset it, and `JetpackFlight.Update` keeps cooling while the pack is worn but stowed, so the walk back from a hard flight *is* the cooldown.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `JetpackConfig` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackConfig.cs) | Every tunable: thrust, drag, vectoring, launch, heat |
| `JetpackHeat` / `JetThrottle` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackHeat.cs) | Pure struct: the budget, the latch, `Resolve` (what the pack will actually do) |
| `JetNozzle` / `JetpackVector` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackVector.cs) | Pure: `Command`, the rate-limited `Advance`, `WorldThrust`, the wire round trip |
| `JetpackStep` | [Flight/](Assets/Game/Scripts/Gear/Jetpack/Flight/JetpackStep.cs) | Pure: one physics step — thrust, this world's own gravity, two drags |
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
| `HoverAuthority` | 1.25 × g | **Bounded from both sides.** Below ~1.1 letting go is a fall rather than a settle, and a relight cannot arrest it either; above 1/cos(`MaxDeflection`) ≈ 1.31 the servo holds its sink rate at full rake and coming down becomes free flight |
| `HoverDamping` | 2.5 /s | What makes letting go settle onto the sink rate rather than keep the climb it arrived with |
| `DescentSpeed` | 4 m/s | How fast a released Space comes down. A third of a free fall's first second — that gap **is** the difference between letting go and being cut off |
| `Gravity` | 18 m/s² | This world's own. Unity's is off for the duration — one source of weight |
| `HorizontalDrag` / `VerticalDrag` | 0.9 / 0.12 /s | The first sets the top speed; the second is low because a fall should stay a fall. Exponential, so a big step cannot reverse the velocity |
| `MaxDeflectionDegrees` | 40° | How much thrust can be aimed sideways — the top speed as surely as the drag is |
| `VectorRateDegreesPerSecond` | 110°/s | **The learning curve.** Raise it far and the jetpack becomes a hover car |
| `LookPitchShare` / `NeutralDeflectionShare` | 0.7 / 0.7 | How much the look scales the keys, and the room left for it to work in. They move together |
| `LaunchKick` / `SpeedCarry` | 7 m/s / 1 | The boost, as a velocity not an impulse, so a takeoff reads the same however it started |
| Heat: thrust / descend / cut | 6.67 / −5 / −10 /s | 15 s of climb; every second of it bought with about 1.3 s of coming down; dead motors cool in ten |
| `OverheatAt` / `RelightAt` | 100 / 20 | The cut, and how far it must fall before the motors take again |
| `WarnFraction` | 0.7 | Where the tips glow, the extra smoke starts and the gauge goes amber. One number, three readouts |
| `JetpackBuilder.SizeScale` | 2 | How much bigger than modelled the pack is worn and carried. Applied in Unity, never baked into the hand-built .blend |
| `JetpackThirdPerson` distance / height | 4.2 / 0.9 m | Where the lens stands while flying. Far enough back to see the pods, which is the point |
| `JetpackBuilder.FlameWidthShare` / `FlameLengthPerRadius` | 1.0 / 7.0 | The plume's size, as fractions of the nozzle disc it comes out of. A base at the full bore and ~1 m of flame on a worn pack — 2.5x the first, restrained cut, which was legible but never dramatic. **Baked into the prefab**, so neither does anything until Build Jetpack is re-run |
| `JetpackNozzles.idleFlame` | 0.6 | The flame floor while sinking, as a fraction of full thrust. A descent is still under power and the flame is the only thing that separates it from the dark nozzles of an overheat |

## Flows

1. **Launch.** Double Space → back channel → `CanUse` (refuses only an overheated pack, loudly) → owner `Use()` toggles → `Begin()` keeps the horizontal speed the player had, writes `LaunchKick` into Y, turns Unity's gravity off, `PlayerMovement.SetGliding(true)`.
2. **Fly.** `FixedUpdate`: read the asked-for throttle, correct it through `JetpackHeat.Resolve`, step the heat, command and advance the nozzles, step the velocity, write it to the Rigidbody, check for a landing. The body's yaw is *read* off the transform — this never writes rotation, because `PlayerLook` owns it.
3. **Show.** The item streams throttle, heat and the nozzle rotation at 15 Hz; `JetpackNozzles` swings both pods about their measured gimbals, stretches four flame cones, ramps the tip glow and emits smoke. The owner overwrites the same three values from its live flight every frame, so its own pods move at frame rate.
4. **Trail.** Every lit nozzle emits smoke while the motors run — thrown out along its own flame cone's axis into a WORLD-space system, so the puff hangs where it was born while the player flies out from under it. Past `WarnFraction` a much heavier second rate is added on top; that layer is the warning, the first is exhaust.
5. **Overheat.** Heat hits 100 → latch → `Resolve` forces `Cut` whatever the pilot holds → the player falls, tips red and smoking, until heat is at 20.
6. **Land.** `PlayerMovement.IsOnGround` **and descending**, or `OnCollisionEnter` for a cliff face. Closing speed is billed through `OrnithopterCrash`, then `End()` hands the body back with `CarryMomentum()`.

## Multiplayer

- **No new message and no new animator parameter.** `UseChannel.Release` steps aside for an item that is `IsContinuous` and still `WantsHold`, so the double tap's `Press(); Release();` opens a 15 Hz stream that outlives the press and ends when the item says it is done — which the jetpack says when the flight ends. Each tick carries `P = (throttle, heat, 0)` and `R =` the nozzle deflection, which is literally a rotation. `PresentHold` fires on every machine.
- The keepalive re-sends at least every 0.2 s, so a **late joiner is correct within a fifth of a second** rather than never.
- `JetpackFlight` is added **only on the owner** — the player's `NetworkTransform` is owner-authoritative, so a flight simulated anywhere else is a second, divergent one fighting the replicated pose. `JetpackPose` and `JetpackNozzles` run everywhere. `UseAuthority.Owner`, because a server-applied velocity is overwritten by the owner, silently.
- The dropped prefab is registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset).
- **Verify on a real client:** take off past the host; watch the pods vector, the flames scale with throttle, the tips go red and smoke at overheat, and the fall when the motors cut.

## Persistence

- The pack is saved by the torso slot (`BodyEquipmentSaveable`, key `body`) like any gear.
- **`heat` and `hot` ride the item's `ItemState`.** Without them a quicksave is free coolant and the whole budget is optional. Applied in `OnEquipped` through `JetpackFlight.SetHeat`.
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
- **`HoverAuthority` is squeezed between two failures and the window is about 0.2 wide**, and **changing `MaxDeflectionDegrees` moves the ceiling** — 1/cos(50°) = 1.56, 1/cos(30°) = 1.15. The two are one decision.
- **`JetpackPose` reads the nozzles, not the motion**, the opposite of `WingsuitPose` and right for the opposite reason: a glider's attitude *is* its flight path, but a jetpack hanging still under full rake is leaning hard while going nowhere, and motion would show none of it.
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
- Tests: `JetpackHeatTests` (the durations as *durations*, that only thrust ever overheats, the latch, that climbing-and-descending outlasts holding), `JetpackVectorTests` (the rate limit, the clamp, the look, the wire round trip, that thrust follows the nozzle) and `JetpackStepTests` (a cut falls, letting go sinks but does NOT fall, full rake costs extra, no energy is minted) — in [Editor/Tests](Assets/Game/Editor/Tests), because they touch Assembly-CSharp types.

## Extending

1. **Retune the feel** in `JetpackConfig` on the prefab. `VectorRateDegreesPerSecond` is the biggest single lever on how the machine plays; `MaxDeflectionDegrees` and `HoverAuthority` must move together (see Gotchas).
2. **Change how it looks, not how it flies:** `JetpackNozzles`' glow and smoke fields, `JetpackPose`'s lean, and `JetFlame.shader`'s bands, taper and flicker.
3. **The model** `_Source~/models/gear/jetpack.blend` is HAND-BUILT and not reproducible from a script — `jetpack_mirror.py` only *arranges* it. Re-export with `jetpack_export.py`, then re-run **Build Jetpack**, which re-measures and re-verifies everything.
4. **Audio is not wired**, and **there is no flight clip**. `JetpackFlight` raises `Overheated` and `Landed` for exactly the first, and nothing listens yet. For the second, `PoseAnimator` forces the idle and `JetpackPose` leans it — a real clip would go on its own layer the way the wingsuit's Glide layer does, and `PoseAnimator` would then be deleted rather than retuned.
