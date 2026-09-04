---
system: Visor
layer: presentation
summary: The helmet's projected blue readout layer — one design language, two sublayers, gauges bound to sources
paths:
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/
  - Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs
  - Assets/Game/Scripts/Gameplay/Oxygen/
symptoms:
  - "the health bar is gone after I pressed H"
  - "my health gauge shows another player's health"
  - "a gauge draws a full bar before the player has finished spawning"
  - "a gauge flickers between two colours while the value sits on a threshold"
  - "there are two health bars on screen, one warm and one blue"
  - "the visor is a flat overlay and does not feel like it is drawn on glass"
  - "the visor comes back offset to one side after a menu closes"
  - "a new gauge I added is invisible and never draws anything"
  - "the oxygen gauge is missing but health shows fine"
  - "my air never runs out anywhere in the world"
  - "I keep the shelter of a building after walking out of it, or after the chunk unloads"
  - "a hint or seat prompt stopped appearing during the crash landing"
  - "the damage arc always lights both sides and never points at anything"
  - "a client's damage arcs never point anywhere, but the host's do"
  - "two players' visors both announce OXYGEN CRITICAL when only one is low"
  - "the warning banner shows no symbol at all, just the text"
  - "pressing H does nothing at all — no gauges, no bracket, no message-stack change"
reads_with: [UI, Combat, PlayerCharacter, Multiplayer, InteractionSystem]
updated: 2026-09-04
---

# Visor

The blue layer drawn on the inside of the helmet: the readouts the player lives by, plus the AR
annotations that describe the world. One design language, built entirely in code, no imported art.

**Scope:** [Assets/Game/Scripts/Presentation/UI/HelmetHUD/](Assets/Game/Scripts/Presentation/UI/HelmetHUD) + [VisorStyle.cs](Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs)
**Related:** [UI.md](UI.md) (the rest of the HUD, the canvas rules), [Combat.md](Combat.md) (health, damage), [Multiplayer.md](Multiplayer.md) (whose state this shows), [InteractionSystem.md](InteractionSystem.md) (what the reticle's info box reads).

## Model

- **Blue is the language; warm is the alarm.** [VisorStyle](Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs) draws every normal readout in one colour (`Ink`, `#7AD4FF`). `Alarm` (`#FFB347`) and `Critical` (`#D9541F`) are spent **only** on danger — nothing else on the visor is ever warm, which is what makes an alarm unmissable without being loud. Both warm values are copied from the model library's `PALETTE.md`, so the visor's amber is the amber that glows on the rig.
- **Colour is never the only signal** (`GDC-L1-UX-0003`, `GDC-L1-UX-0006`). Every alarm state changes **shape** (a hatched danger zone appears on the gauge track) and **wording** (`LOW` / `CRITICAL`) as well as hue. A player who cannot separate amber from blue still reads the state.
- **`VisorStyle` is the third design language, and it has absorbed the fourth.** `UITheme` is menus you stop and read; `MenuEntry` is the navy over the live menu set; `VisorStyle` is light projected on glass a few centimetres from the eye. [HotbarStyle](Assets/Game/Scripts/Presentation/UI/HUD/HotbarStyle.cs) used to be a warm expedition palette for anything over the world — its colours now delegate to `VisorStyle` and only its GEOMETRY and refusal shake remain local, because an item tile is drawn on the helmet glass like everything else and a hotbar in amber would read as a permanent warning.
- **Two sublayers.** [HelmetHUDController](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs) builds `Vitals` (things you play by — gauges, damage arcs) and `Annotations` (things that describe the world — the target bracket, its look-at info box, and — per the H gotcha below — the message stack). **H** cycles Full → Vitals only → Off. The middle state exists because health lives on the visor now: a two-state toggle would let the player hide their own health bar, which the old one deliberately never could.
- **The controller spawns and forgets.** It builds the layers and the modules, and binds only the health the gauge reads. Every other module resolves its own source in its own `OnEnable`. That division is the whole reason this class has not become the place every HUD feature ends up.
- **Gauges are one component bound to a source.** [VisorGauge](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs) draws label + track + fill + hatch + number + suffix; [IVisorGaugeSource](Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs) is what it reads. Integrity and oxygen are two instances, not two copies of the drawing code.
- **Motion is a signal, not a texture.** One ambient effect ([VisorSway](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs), a few pixels of lag behind a head turn) and one event effect (a 0.16 s bloom when a gauge changes state). Nothing else idles. `GDC-L1-FEEL-0004`'s recorded disagreement is the constraint: reflexive juice obscures game state, and a visor with a sweeping radar would make the damage arc compete for the player's eye.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `VisorStyle` | [Theme/VisorStyle.cs](Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs) | Palette, type ramp, geometry, motion constants, runtime-generated sprites cached per parameter. |
| `HelmetHUDController` | [HelmetHUD/HelmetHUDController.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs) | The visor root. Builds `Vitals` / `Annotations`, spawns the modules, resolves the wearer's health. |
| `VisorGauge` | [HelmetHUD/VisorGauge.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs) | One readout. All decisions are static helpers so they hold without a canvas. |
| `IVisorGaugeSource` | [HelmetHUD/IVisorGaugeSource.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/IVisorGaugeSource.cs) | What a gauge reads: current, max, label, thresholds, availability. |
| `HealthGaugeSource` | [HelmetHUD/HealthGaugeSource.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HealthGaugeSource.cs) | Adapts `HealthComponent`. Holds the component, not its numbers. |
| `HelmetOverlayVisibility` | [HelmetHUD/HelmetOverlayVisibility.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs) | **H** cycles the detail level; persists it through `GameSettings`. |
| `VisorSway` | [HelmetHUD/VisorSway.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorSway.cs) | The parallax lag that makes the layer read as glass rather than overlay. |
| `VisorBoot` | [HelmetHUD/VisorBoot.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorBoot.cs) | The power-on rise. Purely visual; never gates input. |
| `HelmetDangerVignette` | [HelmetHUD/HelmetDangerVignette.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetDangerVignette.cs) | Curved arcs that grow per hit and decay. `HitFrom` splits a hit across them by bearing. |
| `VisorReticle` | [HelmetHUD/VisorReticle.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorReticle.cs) | Corner marks snapped around the `Interactor`'s hovered target, plus a look-at info box beside them from `InteractionPromptResolver` (label, prompt, live value). Absorbed `InteractionPromptUI`. The crosshair third of the design spec's `VisorReticle` is not built yet. |
| `SystemMessages` | [HelmetHUD/SystemMessages.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/SystemMessages.cs) | The game's one system-voice channel. Static, id-addressed, four severities. |
| `VisorMessageStack` | [HelmetHUD/VisorMessageStack.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorMessageStack.cs) | Draws `Info` / `Notice`, upper left. `DontDestroyOnLoad`, NOT under the visor canvas. |
| `VisorWarningBanner` | [HelmetHUD/VisorWarningBanner.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorWarningBanner.cs) | Draws the single highest `Warning` / `Alarm`, top centre. |
| `OxygenGaugeSource` | [HelmetHUD/OxygenGaugeSource.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/OxygenGaugeSource.cs) | Adapts `SuitOxygen`; thresholds come from the suit, not the gauge. |
| `SuitOxygen` | [Gameplay/Oxygen/SuitOxygen.cs](Assets/Game/Scripts/Gameplay/Oxygen/SuitOxygen.cs) | The air in the suit: drain, refill, suffocation, warnings. Server decides. |
| `BreathableVolume` | [Gameplay/Oxygen/BreathableVolume.cs](Assets/Game/Scripts/Gameplay/Oxygen/BreathableVolume.cs) | A trigger marking air you can breathe. Shelter is a property of space, not of an object. |

## Flows

**Building the visor:** `HelmetHUDController.Awake` → stretch the root, attach `CanvasGroup` + `VisorBoot` + `VisorSway` → build `Vitals` and `Annotations` → `VisorGauge.Create` the integrity gauge on `Vitals` → spawn the vignette on `Vitals` and the reticle on `Annotations`.

**Binding the wearer:** `OnEnable` → `RebindHealth()` → `GameplayMenuScope.FindLocalPlayer(this)` walks the parent chain → `HealthGaugeSource.Bind`. Allowed to fail; `Update` retries every frame until it lands.

**Cycling detail:** **H** (qualified by `GameplayMenuScope.AcceptsGameplayInput`) → `NextDetail` → `GameSettings.VisorDetail` → `Apply` switches the two sublayers. The root stays active at every level.

**A gauge changing state:** `StateOf` crosses a threshold → colour, hatch and suffix all change together → a `BloomSeconds` window brightens the readout once.

**Looking at something:** `Interactor` resolves `HoveredInteractable` each frame → `VisorReticle.LateUpdate` projects its renderer bounds to screen space, snaps the bracket onto it, and (separately) asks `InteractionPromptResolver.TryResolve` for the info box's label/prompt/value, fading it in and out on its own timer — see [InteractionSystem.md](InteractionSystem.md) for the resolver itself.

## The system-message channel

One static bus, [SystemMessages](Assets/Game/Scripts/Presentation/UI/HelmetHUD/SystemMessages.cs), for the whole game — **system voice only**, never a character (that is `NpcDialogPopupUI`) and never another player (that is chat).

`Post(id, text, severity[, seconds])` / `Clear(id)`. Messages are **addressed by id**: posting the same id replaces rather than stacks, and a clear only ever takes down its own, so two systems announcing the same condition cannot fight. `PlayerHints` is now a thin adapter over this — its API is unchanged, so every existing caller compiles untouched.

| Severity | Surface | Behaviour |
| --- | --- | --- |
| `Info` | stack, `InkFaint` | 4 s |
| `Notice` | stack, `Ink` | 6 s, or until cleared. **Never player-dismissable.** |
| `Warning` | banner, `Alarm`, `!` | holds while true |
| `Alarm` | banner, `Critical`, `!!`, pulsing | holds while true |

## Suit oxygen

[SuitOxygen](Assets/Game/Scripts/Gameplay/Oxygen/SuitOxygen.cs) is the consumer the oxygen plant never had — see [Oxygen.md](Oxygen.md) for the plant that fills the bottles.

- **Drains** whenever the wearer is not inside a `BreathableVolume`. Shelter is a property of *space*: an interior, a tent and a cave all become breathable by containing one, with no code knowing what any of them are.
- **Refilled** by *using* a charged bottle. `DockableSupply.Use` tops the suit up and swaps the item for its drained twin in the selected slot — the bottle stays the unit air is spent in, which is what keeps its charge an item identity.
- **Suffocates** at zero: `suffocationDamage` every `suffocationInterval`, server-side, never instant death.
- **Warns** through `SystemMessages` at `warnFraction` / `alarmFraction`, raised by each machine for its *own* player only.

## Multiplayer

- **The visor is pure local presentation.** It draws only the local player's state and adds nothing to the wire.
- **Whose health it shows is resolved by `GameplayMenuScope.FindLocalPlayer(this)`, never by a `"Player"` tag search.** Every player object carries that tag, so a tag search returns an arbitrary one — which is how two of three players once watched a stranger's health bar.
- `FindLocalPlayer` returning null is legitimate for a frame or more: NGO publishes the local player object *after* `OnNetworkSpawn`. Never cache the miss.
- **Suit oxygen is server-authoritative**, exactly like health: a `NetworkVariable` written only by the server, and the drain and the suffocation damage applied only where `Network.Simulates` is true. A client cannot suffocate itself.
- **Oxygen warnings are raised per-machine for its own player** (`IsOwner`), never server-side — announcing them on the authority would put one player's failing suit on everybody's visor.
- **Damage direction reaches a client by its own owner-targeted RPC.** `NetworkedHealthComponent.AnnounceDamage` is a broadcast gated on a *player* having dealt the hit, so a client mauled by a creature is told nothing by it; `TellOwnerWhereFrom` is a unicast to the victim about their own body, cheap enough to send for every source. `NetMessaging` cannot carry it — that layer has no unicast.

## Persistence

| Value | How |
| --- | --- |
| Suit oxygen | [SuitOxygenSaveable](Assets/Game/Scripts/Core/Persistence/Adapters/SuitOxygenSaveable.cs), attached automatically by `SaveablePolicy.Ensure` alongside `HealthSaveable`. |
| Visor detail level (**H**) | `GameSettings.VisorDetail`, PlayerPrefs. `SchemaVersion` is 2. |
| Motion reduction | `GameSettings.ReduceVisorMotion`, PlayerPrefs. |
| Everything else | **Nothing.** Gauge readings are views onto live components; markers and arcs are session-only, deliberately. |

## Gotchas

- **A source with `Max == 0` reports unavailable, and the gauge hides.** It must never draw a bar: full means "you are fine" and empty means "you are dying", and both are lies about a component that simply has not spawned yet. This is the visor's version of the "a HUD element stays blank" symptom in [UI.md](UI.md).
- **A threshold boundary belongs to the calmer state.** `StateOf` compares strictly below, never at. Comparing `<=` makes a value resting exactly on 35% flicker between blue and amber every frame.
- **`VisorStyle` sprites are cached per parameter**, for the reason `UITheme.Rounded(radius)` is: a generator called from a draw path allocates a texture per call, and a 9-sliced sprite whose border exceeds its rect draws its corners over each other.
- **The visor root must stay active at every detail level.** It owns the sublayers, so a controller that deactivated itself could not switch them back on. `HelmetOverlayVisibility` switches the two children and never the root — the same reason it lives on the canvas root rather than on what it toggles.
- **`VisorSway` must reset its offset in `OnDisable`.** Left where it was, a layer switched off mid-turn comes back shifted to one side; and a stale rotation makes the first frame after re-enabling read as an enormous turn.
- **Deleting `HealthUI` is not finished until the authored objects go too.** `PlayerHUD.prefab` carries `Health` / `HealthBar` / `HealthText` / `maxHealthText` as authored children. Leaving them draws a second, dead health bar in the old warm palette beside the new gauge.
- **The message surfaces are NOT children of the visor canvas, and must not become them.** `VisorMessageStack` and `VisorWarningBanner` are self-instantiating `DontDestroyOnLoad` overlays because the arrival announces things at exactly the moments the player's whole HUD is switched off — `SeatPromptUI`'s "Q — exit the ship" hint would never be seen otherwise. **H** therefore reaches them by calling `SetShown`, not by deactivating a parent.
- **The warning banner stays visible at the Vitals detail level.** Turning the markers off is not consent to stop being told the suit is failing.
- **LiberationSans has no warning triangle.** The banner's mark is ASCII `!` / `!!` on purpose; a glyph the font lacks renders as literally nothing, so the severity mark would silently vanish. Same rule as the "no glyph spinners" gotcha in [UI.md](UI.md).
- **A `BreathableVolume` being disabled or streamed out raises no `OnTriggerExit`.** `BreathableVolume.OnDisable` calls `SuitOxygen.ForgetVolume` for exactly that reason: without it a player standing in a chunk that unloads keeps the shelter for ever and never breathes their own supply again.
- **Front and back damage are deliberately indistinguishable.** There are two arcs, on the left and right edges; `HitFrom` splits a hit between them by its lateral component, so due-front and due-behind both split evenly. Saying "behind you" honestly would be a shader change, not a maths change.
- **`ReportDamageDirection` changes no health.** The value arrives separately through the health `NetworkVariable`; applying it there as well would subtract the same hit twice.
- **`Awake` does not run on an `AddComponent` in an EditMode test.** That is why every decision in `VisorGauge` is a static helper — `FractionOf`, `StateOf`, `ColourFor`, `ShowsHatch`, `SuffixFor` — rather than instance state.
- **The bracket and the info box can legitimately disagree.** `VisorReticle` shows the bracket for anything `Interactor` is hovering, but the info box only for what `InteractionPromptResolver.TryResolve` accepts — an authored `InteractionPrompt.ShowPrompt = false` hides the box while the bracket still frames the target. Not a bug: it is the same asymmetry `InteractionPromptUI` had before the two were folded together, kept on purpose rather than smoothed away.
- **The bracket snaps; the info box fades — different motions on purpose.** The bracket's oversize-in read as the suit acquiring a target; the info box cross-fades instead, because a hard cut while a live value (a winch, a helm) is changing under it reads as a flicker, not a new readout. Giving the box the bracket's snap curve, or the reverse, undoes the reason each was chosen.
- **A nested `PlayerHUD` prefab instance can silently lose its `HelmetHUD` child.** `PlayerCharacter.prefab` nests `PlayerHUD.prefab`, and a prefab-instance override (`m_RemovedGameObjects`) on that nested instance can delete the `HelmetHUD` child without touching `PlayerHUD.prefab` itself — the source prefab still looks correct in isolation. `HelmetOverlayVisibility.Awake` then finds no `HelmetHUDController` (`GetComponentInChildren` comes back null, logged as a warning) and every later `Apply()` returns before touching `Vitals`/`Annotations`, so H visibly does nothing. Diagnose by checking `~/Library/Logs/Unity/Editor.log` for `"No HelmetHUDController under this canvas"`; fix by clearing the stale entry from the nested instance's `m_RemovedGameObjects` (or re-adding the child and re-applying the override) in `PlayerCharacter.prefab`, not in `PlayerHUD.prefab`.

## Extending

**A new readout**
1. Implement `IVisorGaugeSource` over whatever holds the number. Hold the component, not a copy of its value, and report `Available` false until it resolves.
2. `VisorGauge.Create(controller.Vitals, "MyGauge", VisorGauge.Align.Left, source)` from `HelmetHUDController.EnsureSubsystems`. Pick `Vitals` if the player plays by it, `Annotations` if it describes the world.
3. Draw only with `VisorStyle` and `UIBuilder`. No PNGs, no new palette, no `CanvasScaler` — [UIScale](Assets/Game/Scripts/Presentation/UI/Widgets/UIScale.cs) is the only thing that may configure one.
4. If it can reach an alarm state, give it a shape and a word as well as a colour.

**A new system message** — `SystemMessages.Post(id, text, severity)` from anywhere. Pick an id namespaced to your system (`suit.oxygen`, `hint:seat`) so nothing else can clear it. Post a `Warning`/`Alarm` while a condition holds and `Clear` it when it stops; do not post one every frame with a timeout, which makes it flicker. If the message is about the local player specifically, raise it only on their machine.

**A new breathable place** — put a trigger collider on it and add `BreathableVolume`. Nothing else.
