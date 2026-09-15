---
system: Flamethrower
layer: items
summary: "A six-metre stylized jet that leaves the ground burning: server sweeps the cone, every machine lays the fire"
paths:
  - Assets/Game/Scripts/Items/Artifacts/Flamethrower
  - Assets/Game/Editor/Items/FlamethrowerJetBuilder.cs
  - Assets/Game/Editor/Items/FlamethrowerReseat.cs
  - Assets/Game/Art/Models/Items/flamethrower.fbx
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/Flamethrower.prefab
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/GroundFire.prefab
  - Assets/Game/Resources/Effects/BodyFire.prefab
  - Assets/Game/Art/Shaders/Effects/FlameBillboard.shader
  - Assets/Game/Art/Materials/Items/FlameCore.mat
  - Assets/Game/Art/Materials/Items/FlameBillow.mat
  - Assets/Game/Art/Materials/Items/FlameEmber.mat
  - Assets/Game/Scripts/Gameplay/Status/BurningStatus.cs
  - Assets/Game/Editor/Tests/GroundFireTests.cs
  - Assets/Game/Editor/Tests/BurningStatusTests.cs
symptoms:
  - "the flamethrower fires but there are basically no flames, just a few specks"
  - "a particle system emits about one particle a second however high its rate is authored"
  - "the flame only comes out of the gun and never hangs in the air"
  - "the jet swings rigidly with the barrel instead of staying where it was thrown"
  - "the ground never catches fire under the jet"
  - "the ground only catches fire when I aim straight down at it"
  - "the flamethrower burns creatures but does nothing at all to crates or props"
  - "the whole terrain chunk caught fire as one object"
  - "the fire looks like a raft of orange bubbles rather than flames"
  - "the flames look like clean vector raindrops with smooth edges"
  - "a burning crate takes damage but shows no flames at all"
  - "I respawned and was still on fire"
  - "[BurningVisual] No fire prefab at Resources/Effects/BodyFire"
  - "creatures take burn damage from the flamethrower but no flames ever appear on them"
  - "the fire on a burning creature is the same size as the fire on a burning crate"
  - "fire patches pile up in a heap when I hold the trigger on one spot"
  - "a patch of fire fires all its flames sideways across the sand"
  - "the fire lights nothing — six metres of flame in a dark room"
  - "every creature standing in one patch takes damage once per player in the session"
  - "the flame is a few dim specks and the console is clean"
  - "the flame leaves from inside the barrel after re-exporting the model"
  - "the supply gauge bar floats in the air behind the grip"
  - "the item is held by the wrong part of itself after a model re-export"
  - "a creature stays on fire for as long as it stands in the flames and never burns out"
  - "the fire on a burning creature is a fireball wider than the creature itself"
reads_with: [Artifacts, Combat, Multiplayer]
updated: 2026-09-09
---

# Flamethrower

Hold the trigger and it throws a six-metre cone of fire. What it touches keeps burning, and so does
the ground it crosses.
**Scope:** [FlamethrowerArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlamethrowerArtifact.cs),
[FlameJet.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlameJet.cs),
[FlameLayers.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlameLayers.cs),
[GroundFire.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/GroundFire.cs),
[GroundFireField.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/GroundFireField.cs).
The original design brief is [Artifacts/Flamethrower.md](Artifacts/Flamethrower.md).

## Model

- **The item never resolves damage.** The cone announces `Burning`; the status bills 4 /s for 5 s. A jet sweeping 15 /s that charged per tick would charge a body once per tick and once per overlap — see [StatusEffects](Artifacts/StatusEffects.md).
- **Five seconds is the whole burn, however long the flame stays on the body.** The cone announces `Burning` 15 /s and a patch announces it for as long as a body stands in it, so `CanApply` refuses the refresh and holds a 3 s cooldown after burnout; without both a creature in a patch never stops burning.
- **Three separate things carry "fire".** The *cone* decides what catches, the *jet* is particles that decide nothing, the *patches* look like fire and announce the status the cone does.
- **Everything burns, and `StatusReceiver.EnsureOnBody` says what "everything" means** — the rule is shared with the cryo sprayer, and [Ignition](Assets/Game/Scripts/Items/Artifacts/Flamethrower/Ignition.cs) adds the `BurningVisual` beside the receiver it returns, so a crate catches like a creature. The line is at bodies — a `HealthComponent`, a `Rigidbody`, or anything authored — or a mask of `~0` lights a terrain chunk as one object.
- **Every flame quad is shaded by one shader,** [FlameBillboard.shader](Assets/Game/Art/Shaders/Effects/FlameBillboard.shader): world-space noise bites tongues out of it, snapped to four bands. World space is the trick — neighbours sample one field, so a clump reads as one mass rather than a stack of identical puffs.
- **The bands are read off the noise and the height up the flame**, never off a radius: banding a radial gradient draws concentric rings, which is how a sphere is shaded. Hot root, soot tip.
- **A burning body draws its own fire, by LISTENING.** `BurningVisual` subscribes to its receiver's `StatusChanged` and fits `Resources/Effects/BodyFire` — a 0.8 m shell — to the body's rendered WIDTH, not its diagonal, which on anything tall drew a fireball a body and a half across. Nothing calls it, so fire lit by the cone or by a patch looks the same, and clearing the status puts it out.
- **The particle's own colour is its heat, not its age** — reading age needs a custom vertex stream, which fails silently. Retuning how a flame cools is the builder's gradient, not a shader edit.
- **The quad is a flame, not a puff.** The shader cuts a candle profile out of every quad's own uv — rounded root (`_Root`), belly (`_Belly`), drawn tip (`_Tip`) — and flame layers start within ±12° of upright (`TongueTilt`), never the usual 0..2π. A radial falloff is round however hard it is chewed, and a sprite spun to any angle has to be round to survive it: that was the raft of bubbles.
- **The noise BREATHES that width; it is never added to the shape.** At the tip the profile has almost no width left to defend itself, so an added bite is the whole signal and the quad fills back in as a square-edged cloud; `_TipBite` above 1 instead pinches the tip off and throws a lick free. A dying flame is drawn thin the same way (`_Wither`) — raising the cut with age eats the eroded edge first and leaves the solid middle standing as a smooth lens.
- **The visible reach is `speed × lifetime`** — no drag, no limit curve — so it reads off two lines of the builder. Measured at **7.1 m** against a 6 m range.
- **Ground fire merges onto a 1.2 m world grid** — a cell either holds a patch to refresh or does not. That makes a swept jet a trail rather than a heap, and keeps machines agreeing where fire is.
- **The whole path is sampled, not the impact point.** The jet is walked in 1 m strides from 2 m out and a ray dropped from each: ground within 1.6 m *under* the flame catches, not just what is aimed AT.
- **Patch lights are budgeted.** The nearest six to the camera light the world; the rest burn without illuminating (`GDC-L1-PERF-0004`).

### Numbers

| Knob | Value | Where |
| --- | --- | --- |
| Range / cone | 6 m, 25° half-angle | `FlamethrowerArtifact` |
| Cone sweep | 15 /s, every machine | `FlamethrowerArtifact.sweepsPerSecond` |
| Burn | 4 /s for 5 s, not extendable, 3 s before it catches again | [BurningStatus.cs](Assets/Game/Scripts/Gameplay/Status/BurningStatus.cs) |
| Body fire | body width x 1.1, clamped 0.4–2 m, off a 0.8 m shell | `BurningVisual` |
| Ground fire rate | 10 /s, after a 0.2 s delay | `FlamethrowerArtifact.firesPerSecond` |
| Ground sampling | every 1 m from 2 m out, 1.6 m down | `groundSampleStep/Start`, `groundReach` |
| Patch life / fade / radius | 5 s / 1.5 s / 1.1 m | `GroundFire` |
| Cell / patch cap / light cap | 1.2 m / 40 / 6 | `GroundFireField` |
| Jet emission | 210 + 100 + 160 /s, ~90 core alive | `FlamethrowerJetBuilder` |
| Flame shape | width 0.72, root 0.55, tip 0.9, sway 0.45, tip bite 2.4, wither 0.8; noise 14/9/22, draw 0.35 | `FlamethrowerJetBuilder` |
| Core speed × life | 12–16 m/s × 0.38–0.46 s | `FlamethrowerJetBuilder` |
| Muzzle light | 12 intensity, 14 m, 2.6 m down the plume; patch ignite 4 /s | `FlameJet`, `GroundFire` |

## Key types

| Type | File | Role |
|---|---|---|
| `FlamethrowerArtifact` | [FlamethrowerArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlamethrowerArtifact.cs) | The lance. `UseAuthority.Server`, `IsContinuous`. Sweeps the cone, lays the ground fire, drives the jet |
| `FlameJet` | [FlameJet.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlameJet.cs) | What "on" looks and sounds like: aim, throttle, four emitter channels, the muzzle light |
| `FlameLayers` | [FlameLayers.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/FlameLayers.cs) | One emitter and its children, throttled as one. **Multiplies the authored rate by hand** — see Gotchas |
| `Ignition` | [Ignition.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/Ignition.cs) | What may catch fire, and the one call that lights it. Shared by the cone and the patches |
| `ConeSweep` | [ConeSweep.cs](Assets/Game/Scripts/Items/Artifacts/ConeSweep.cs) | Which bodies a cone is on: overlap, cone test, one entry per body, line of sight. Shared with the [cryo sprayer](Artifacts/CryoSprayer.md) |
| `BurningVisual` | [BurningVisual.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/BurningVisual.cs) | The flames ON a burning body. Added by `Ignition`, driven by `StatusChanged`, sized to the body |
| `GroundFire` | [GroundFire.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/GroundFire.cs) | One patch: its own clock, its flames, its glow, and the status it announces on the authority |
| `GroundFireField` | [GroundFireField.cs](Assets/Game/Scripts/Items/Artifacts/Flamethrower/GroundFireField.cs) | The cell grid, the pool, the patch cap and the per-camera light budget |
| `FlamethrowerJetBuilder` | [FlamethrowerJetBuilder.cs](Assets/Game/Editor/Items/FlamethrowerJetBuilder.cs) | *Tools ▸ SpaceGame ▸ Items ▸ Build Flamethrower Fire*. Owns the `Jet` subtree, the whole ground-fire prefab and the three flame materials |

The jet's layers hang under `Jet`: **Flame** (core), with **Billows** (slow, fat, buoyant — what
persists after the core has gone) and **Wisps** (fast, hot) under it, plus **Embers**, **Smoke** and
**Pilot** beside it. All world-space, all turbulent.

## Flows

1. **Press.** `OnRequestUse` on the **owner** puts the aim ray in `P`/`R`. `Use()` and `Present()` both call `Ignite`, which is idempotent because a host runs both.
2. **Hold.** `OnRequestHold` re-sends the aim; `Hold()` and `PresentHold()` both record it — a dedicated server never receives `PresentHold`, so the aim must land on the authority path too.
3. **Per frame.** On **every** machine: the throttle ramps, the jet is drawn, the cone is swept at 15 /s and ground fire is laid at 10 /s — one forward ray for what stopped the jet, then a dropped ray per stride along what it crossed.
4. **A patch.** `Kindle` refills its five-second clock; it burns, fades over the last 1.5 s, stops emitting, waits for the flames to go out, and hands itself back to the pool.
5. **Release.** The final hold tick arrives with `active` false and stops the jet; `holdTimeout` (0.5 s) is the net for a release that never arrives.

## Multiplayer

Server-authoritative, riding the existing hold path. Nothing new is on the wire.

- **Ground fire is deliberately NOT a network prefab.** A sweeping jet lays several a second, each living five; every machine lays its own from the aim stream — the rule a tracer follows.
- **The cone and the patches run on EVERY machine, and neither asks who decides.** `StatusReceiver.Apply` returns early where it does not simulate the body, so the fire is billed once however many machines announce it. Running it everywhere is the point: a receiver only the server invented burns for the server alone, because the status arrives on that body's own relay and a relay with nothing subscribed drops it in silence.
- **Nothing puts a fire out on death, and a respawn does.** `StatusReceiver.ClearAll` is called by [`RespawnRelease.Everything`](Assets/Game/Scripts/Gameplay/Game/Spawning/RespawnRelease.cs) on the deciding machine, which announces one `StatusSet` per running condition — the ordinary clear, so every machine puts the flames out through the handler it already runs. A corpse goes on burning on purpose.
- Patches are laid down the **smoothed** direction — the one the jet is drawn along — while the cone sweeps down the raw aim. Fire not quite where the server has it beats fire not where the flame went.

## Persistence

**Nothing here is saved, deliberately.** A patch dies in five seconds, and `Burning` on a body is
not saved either ([StatusEffects](Artifacts/StatusEffects.md)); the tank fill is `SupplyCharge`,
which `UsableItem` already captures. `GroundFire.prefab` therefore carries no `NetworkObject`, no
`SaveableEntity` and no `Rigidbody`: `SaveablePolicy.EnsureSpawned` reads exactly those, and any one
would give it a saveable identity with no stamped prefab id.

## Gotchas

- **`EmissionModule.rateOverTimeMultiplier` is NOT a scale on a constant rate — it IS the constant.** A 0..1 throttle written into it replaces 210 particles a second with 0.21: a handful of specks, clean console. `FlameLayers` multiplies the captured rate by hand; [GroundFireTests](Assets/Game/Editor/Tests/GroundFireTests.cs) keeps it so.
- **A Circle emitter throws its particles RADIALLY OUTWARD in its own plane** — laid flat it fires every flame sideways. Patches use a shallow **Cone** stood on end by `shape.rotation`.
- **A .mat freezes the shader defaults it was BORN with**, so retuning a default in the shader changes nothing on an existing material — `EnsureFlameMaterial` writes EVERY value, matching ones included.
- **A flame layer's `startRotation` is a tuning value.** Spun to a random angle, the only shape that survives is a circle. The smoke layers keep their 0..2π on purpose: smoke IS round.
- **A backgrounded Editor does not recompile**, and the menu item then rebuilds the fire out of the previous assembly with no error anywhere — the materials come back carrying the OLD constants. Focus the Editor, wait for `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` to change, build, then read a value back out of a .mat to prove which code ran. Run it twice after a domain reload.
- **`BodyFire`'s emitters scale on `Hierarchy`; every other emitter here is `Local`.** `Local` is right for the jet, or `ItemGrip` resizing the lance resizes the flame; `Hierarchy` is right for the body fire, or it ignores the scale `BurningVisual` fitted it with.
- **`Play` on a system that is already playing RESTARTS it** — emitters start and stop on the throttle's edge only, or the jet is cleared sixty times a second.
- **`jetRoot` must be a plain empty**: `FlameJet.Aim` writes it a world rotation every frame, which destroys how an imported model's node sits. Every jet layer also simulates in WORLD space, or the jet swings rigidly with the barrel.
- **FMOD has no runtime outside play mode** — `FlameJet.SetThrottle` throws an NRE from an editor script, so drive `FlameLayers` directly there.
- **`Resources/Effects/BodyFire.prefab` is GENERATED, and a missing one is nearly silent** — one warning, then bodies burn with no flames, which reads as a status bug. Check the asset is on disk and committed before touching status code.
- **Do not hand-edit the `Jet` subtree, `GroundFire.prefab` or `BodyFire.prefab`** — `FlamethrowerJetBuilder` replaces all three wholesale, and a run over MCP can execute stale code.
- **`BurningVisual.PrefabWidth` is twice the `Wrap` radius the builder gives `Flame`**, and the fitted scale divides the body's width by it: retune the shell in the builder without the constant and every body fire is drawn at the wrong width, silently.
- **A patch skips anything steeper than `minGroundSlope` (0.45) and any body** — a body has its own fire now, and a disc of flame on a creature's hip would stay behind when it walked off.
- **Sampling starts 2 m out**: the flame at the muzzle is over the holder's own feet, so a stride from zero sets them alight on their own trigger pull.
- **Re-exporting the model moves nothing on the prefab, silently.** `Muzzle`, `Jet`, `Jet/Pilot`, `GripPoint` and the `Gauge_*` trio sit at positions read off the model's `Marker_*` meshes, and a hand edit in Blender does not move those markers either. The order is **markers → export → reseat**: `flamethrower_markers.py`, `flamethrower_export.py`, then `Tools/SpaceGame/Items/Reseat Flamethrower` ([FlamethrowerReseat.cs](Assets/Game/Editor/Items/FlamethrowerReseat.cs)), which also refits the pickup box. Skip it and the flame leaves from inside the barrel and the gauge bar hangs in the air behind the grip.

## Extending

1. **Retune the look:** the `Heat` gradient and shape constants in [FlamethrowerJetBuilder.cs](Assets/Game/Editor/Items/FlamethrowerJetBuilder.cs), then re-run the menu item.
2. **Change the reach:** `CoreSpeed × CoreLife` in the builder and `range` on the prefab, together.
3. **Longer burns:** `GroundFire.lifetime` with `BurningStatus`'s five seconds and its `reigniteDelay` — a burn longer than the gap is a body that never stops burning. For more fire at once, `GroundFireField.MaxPatches`, then profile.
