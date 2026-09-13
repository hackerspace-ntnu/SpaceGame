# Helmet Visor UI — design

**Date:** 2026-09-04
**Status:** approved, ready for implementation planning

## Goal

Replace the split in-game HUD with one coherent projected visor: a blue digital layer drawn on
the inside of the helmet, holding the readouts the player lives by. Health moves into the helmet.
Oxygen becomes a real resource. One generic system-message channel serves the whole game. Chat
joins the visor's design language. The layer is alive without being busy.

## Decisions taken

| Question | Decision |
| --- | --- |
| Oxygen source | **Build the consumer.** A per-player suit-oxygen value that drains outside breathable air, is refilled by spending a charged bottle, and suffocates at zero. |
| AI / system messages | **One generic system-message channel for the whole game.** System voice only, never characters. |
| Chat | Part of the visor, in the visor's language. Still a list, still toggled with **T**. |
| Palette | **Blue is the language, warm is the alarm.** Everything projected is blue; `#FFB347` / `#D9541F` are spent only on danger. |
| Motion | **Alive, restrained.** Motion is a signal, not a texture. Idle movement stays under the threshold of noticing. |
| Build approach | **One visor layer, everything code-built** under `HelmetHUDController`. |
| "Inventory marker" | The hotbar, redrawn in the visor language. |
| "Info boxes" | A look-at detail panel, plus notices that expire on their own — **never dismissable by the player**. |

## Design principles applied

From `docs/game-development-constitution/`:

- **`GDC-L1-UX-0003`** (objective, c4) — rank by salience; *"never encode critical information in
  colour alone"*. Every alarm state therefore carries a shape and word change as well as amber.
- **`GDC-L1-FEEL-0004`** (objective, c4) — amplify meaningful events. Its recorded *Disagreement*
  is the binding constraint here: reflexive juice "can produce sensory overload, obscure game
  state". Hence restrained motion, and no idle telemetry animation.
- **`GDC-L1-UX-0006`** (contextual, c4) — no colour-only encoding; toggles for motion effects.
  Drives the three-state **H** toggle and a motion-reduction setting.
- **`GDC-L1-ANIM-0002`** (contextual, c4) — animation never blocks input. The boot sweep is
  purely visual and never gates control.

---

## 1 · The visor layer

`HelmetHUDController` becomes the root of the whole visor. Its only job is to spawn the modules
and own their lifecycle. It **stops** binding health and **stops** relaying damage — each module
resolves its own source via `GameplayMenuScope.FindLocalPlayer(this)` and binds in `OnEnable`.
That division is what keeps it from becoming a god class.

### Two sublayers

| Sublayer | Holds | Rationale |
| --- | --- | --- |
| **Vitals** | oxygen + integrity gauges, hotbar, reticle/bracket, damage arcs, warning banner | things you play by |
| **Annotations** | nav markers, message stack, chat list, look-at info box | things that tell you *about* the world |

**H becomes three-state: Full → Vitals only → Off.** This exists because moving health into the
helmet would otherwise make **H** hide the player's own health bar —
`HelmetOverlayVisibility`'s comment is explicit that health and hotbar stay out of the toggled
layer because they are *"readouts you play by"*. Three states preserve that intent, keep a
screenshot mode, and satisfy `GDC-L1-UX-0006`.

The chosen state persists in `GameSettings` (the only UI state that survives a quit).

### `VisorStyle`

A new static beside `UITheme` and `HotbarStyle`, and the reason coherence is structural rather
than a matter of discipline. It owns:

- **Palette** — `Ink #7AD4FF`, `InkDim`, `InkFaint`, `Alarm #FFB347`, `Critical #D9541F`.
  The two warm values are lifted from `Assets/Game/Art/Models/_Source~/PALETTE.md`, the same way
  `HotbarStyle`'s are, so the visor's alarm is the amber that glows on the rig. Hex written beside
  each, because that table is the source and this is a copy.
- **Type ramp** — `Label` (small, wide tracking, uppercase), `Body`, `Readout` (large, light),
  `Micro`.
- **Geometry** — stroke weight, glow radius, corner radius, gauge track height.
- **Motion constants** — sway amplitude and lag, bloom duration, type-in rate, snap overshoot.
- **Runtime-generated sprites** — bracket corner, arc, gauge track, chevron, hatch. Drawn into
  textures at runtime and cached per parameter, exactly as `UITheme.Rounded(radius)` is. **No
  imported art.**

### Colour is never the only signal

Per `GDC-L1-UX-0003`. Every alarm state changes shape and wording as well as hue:

| State | Colour | Shape | Word |
| --- | --- | --- | --- |
| Normal | `Ink` | plain track | value only |
| Warning | `Alarm` | hatched danger zone appears on the track | banner text |
| Critical | `Critical` | hatch + thickened stroke + pulse | banner text + `⚠` |

### Modules

Each is a small `MonoBehaviour` with one job, one source, its own rect. Spawned by
`HelmetHUDController`, self-binding.

| Module | Draws | Source |
| --- | --- | --- |
| `VisorGauge` ×2 | label + track + number | `IVisorGaugeSource`; instances bound to `SuitOxygen` and `HealthComponent` |
| `VisorWarningBanner` | top-centre banner | `SystemMessages` at `Warning`/`Alarm` |
| `VisorMessageStack` | upper-left message column | `SystemMessages` at `Info`/`Notice` |
| `VisorChatList` | chat log below the stack | `ChatLog.Added` |
| `VisorReticle` | crosshair, interaction bracket, look-at info box | `Interactor` + `InteractionPromptResolver` |
| `HelmetDangerVignette` *(extended)* | directional damage arcs | new directional damage channel |
| `HelmetNavMarkers` *(restyled)* | AR markers | `EntityTargetRegistry` + `MapService` |
| `VisorSway` | applies the restrained parallax lag to the layer root | camera rotation delta |
| `VisorBoot` | the spawn sweep | lifecycle only |

### Existing code

- **`HealthUI` — deleted**, together with the authored `Health` / `HealthBar` / `HealthText` /
  `maxHealthText` objects in `PlayerHUD.prefab`. Its serialized-reference wiring is the documented
  cause of *"a HUD element stays blank until something happens to it"*.
- **`CrosshairUI` — absorbed** into `VisorReticle`. Its dead hover path goes with it. `UI.md`
  says not to fix that path *"without owning the look change"*; this change owns the look change,
  so the caveat is discharged rather than ignored. `UI.md`'s gotcha and the `Pages & widgets` row
  must both be updated.
- **`InventoryUI` / `InventorySlotUI` — logic kept intact.** `IPlayerInventory`, the
  `PackHandController` hand-off and the refusal shake do not move. Only their drawing swaps
  `HotbarStyle` → `VisorStyle`. **Slot count is read from the live inventory, never a constant**
  (it is `4` today, in `PlayerInventoryComponent.inventorySize` and
  `PlayerInventoryNetwork.inventorySize`).
- **`InteractionPromptUI`** — its resolver logic stays; its drawing becomes the info box on
  `VisorReticle`.
- **`HotbarStyle`** — retired once nothing references it. Its palette comment records a decision
  this spec reverses; that comment must be deleted, not left to contradict the code.

---

## 2 · The system-message channel

One static `SystemMessages` bus for the whole game. System voice only — never characters, which
keeps it clear of `NpcDialogPopupUI`.

```
SystemMessages.Post(id, text, severity, seconds = 0)
SystemMessages.Clear(id)
```

`id` is how a late `Clear` cannot erase a newer message from another source — the same rule
`PlayerHints` already uses for its one slot.

| Severity | Surface | Behaviour |
| --- | --- | --- |
| `Info` | message stack, `InkFaint` | fades on its own |
| `Notice` | message stack, `Ink` | holds longer; expires on time or on `Clear`. **Not player-dismissable.** |
| `Warning` | banner, `Alarm` | holds while the condition is true |
| `Alarm` | banner, `Critical`, pulsing | holds while the condition is true |

`PlayerHints` keeps its exact public API (`Show(id, text[, seconds])` / `Hide(id)`) and becomes a
thin adapter posting `Notice`. Every existing caller — `SeatPromptUI` among them — compiles
unchanged, and there is still exactly one surface where text appears.

`ChatUI` keeps ownership of input, **T**, `ChatText.Sanitize` and `<noparse>`. None of that moves.
Only its log rendering moves, into `VisorChatList`, in `VisorStyle`, in one column under the
message stack, pitched quieter. Chat stays a separate *channel* — it is other players, not the
suit — but stops being a separate *design language*.

---

## 3 · Suit oxygen

The plant already produces charged bottles that nothing spends. This closes that loop, along the
path [Oxygen.md](../../AI/systems/Oxygen.md) `Extending` already prescribes: a number on the
player's own record, with the bottle left as the *unit it is spent in*.

### `SuitOxygen`

A component on the player, modelled structurally on `SandstormVictim`: server applies, everyone
keeps the value current, fractional accumulation carried into integer damage.

- `float Current` over `maxOxygen`, exposed read-only for the HUD.
- **Drain** at `drainPerSecond` whenever the player is not inside a `BreathableVolume`.
- **Refill** by *using* a charged bottle: the `OxygenTank` item gains a use verb that swaps it for
  `OxygenTankEmpty` and adds `bottleRestores`. Because a bottle's charge is its identity, the swap
  replicates, saves and re-icons for free — no new state on the wire.
- **At zero:** `suffocationDamage` via `NetDamage.Apply` every `suffocationInterval`, server-side.
  Not instant death.
- **Warnings:** crossing `warnFraction` posts `Warning`; crossing `alarmFraction` posts `Alarm`;
  recovering `Clear`s them.

Every one of those is a serialized field. No magic numbers.

### `BreathableVolume`

A trigger collider marking air you can breathe. `SuitOxygen` counts the volumes it is inside, so
overlapping volumes and streamed-out volumes behave. The ship interior gets one.

**Flagged assumption, needs playtest:** draining everywhere outside the ship turns the open world
into a timer, which is a real change to how the game plays. `drainPerSecond` defaults generously —
a full tank lasts on the order of ten minutes — and is the single knob that tunes it. If playtest
says the timer is wrong, the fix is that number or the placement of `BreathableVolume`s, not the
architecture.

---

## 4 · Damage indication

`HealthComponent.Damage(int, Transform source)` already receives the source and drops it before
the event. Two changes:

1. `HealthComponent` gains an event carrying the source alongside the amount. `OnDamage` stays as
   it is, so existing subscribers are untouched.
2. **Clients need the direction over the wire, and today they get nothing.**
   `NetworkedHealthComponent.AnnounceDamage` returns early unless a `PlayerIdentity` dealt the
   hit — deliberately, as a cost optimisation for damage between things nobody is watching. That
   gate stays. Alongside it, an **owner-targeted RPC** fires whenever the victim is a player,
   carrying amount and the hit's world position. `NetworkedHealthComponent` is a
   `NetworkBehaviour`, so `[Rpc(SendTo.Owner)]` is valid here; `NetMessaging` cannot carry this
   because it has no unicast.

`HelmetDangerVignette` gains `HitFrom(Vector3 worldSource, float strength)`, which places the arc
by **bearing** rather than by the current `Left`/`Right` enum. `HitBoth` is kept for sourceless
damage — falls, suffocation, sand.

---

## 5 · Reticle, markers, info boxes

`VisorReticle` draws the crosshair, and a bracket that snaps onto the collider bounds of the
`Interactor`'s current target with a short overshoot. Beside the bracket, the look-at info box
unfolds: name, state, distance, and the verbs, from `InteractionPromptResolver`. It dies when you
look away.

`HelmetNavMarkers` keeps its projection and edge-clamping and is restyled to `VisorStyle`:
hostile markers `Critical`, everything else `Ink`, with the shape carrying hostility as well as
the colour.

---

## 6 · Multiplayer

Every feature is designed for host **and** client from the start.

| Thing | Where it lives |
| --- | --- |
| Suit oxygen value | **Server-authoritative** `NetworkVariable` on the player, matching how health works. Clients read, never write. |
| Oxygen drain and suffocation | Server only. A client cannot suffocate itself. |
| Bottle consumption | Item identity swap through the existing inventory path, which already replicates. |
| Damage direction | Owner-targeted RPC from the server; each player is told only about their own hits. |
| The visor itself | Pure local presentation. Draws only the local player's state, resolved through `GameplayMenuScope.FindLocalPlayer(this)` — **never a `"Player"` tag search**. |
| Chat | Unchanged. |

**Verification is on an actual client, not just the host.** A second peer must show: its own
oxygen draining and refilling, its own suffocation damage, a damage arc pointing at the thing
that actually hit it, and its own health in the gauge.

## 7 · Persistence

| Value | How |
| --- | --- |
| Suit oxygen | `SuitOxygenSaveable`, following `HealthSaveable` exactly. |
| **H** toggle state | `GameSettings` (PlayerPrefs), with a `SchemaVersion` bump. |
| Motion-reduction setting | `GameSettings`. |
| Everything else on the visor | **Holds no state worth persisting** — messages, markers, brackets and the chat log are all session-only, and deliberately so. |

Verified by reloading and by confirming the oxygen value actually appears in the save JSON.

## 8 · Tests

EditMode NUnit, matching the existing suite:

- Oxygen drains only outside a `BreathableVolume`; a charged bottle restores and becomes an empty
  one; zero causes damage on a tick, not instantly.
- `SystemMessages` severity routes to the right surface; a stale `Clear(id)` cannot erase a newer
  message posted under a different id.
- `VisorStyle` sprite generation is cached per parameter (the `UITheme.Rounded` failure mode).
- The visor uses no `"Player"` tag search and configures no `CanvasScaler` itself — the existing
  `UIScalingTests` rule.
- The hotbar reads its slot count from the inventory rather than a literal.

## 9 · Documentation

Per `CLAUDE.md`, documenting the change is part of the change.

- **New** `docs/AI/systems/Visor.md` — the visor layer, `VisorStyle`, `SystemMessages`, suit
  oxygen. Standard shape: Model → Key types → Flows → Multiplayer → Persistence → Gotchas →
  Extending. Needs `symptoms:` entries for anything that costs real time.
- **`docs/AI/systems/UI.md`** — rewrite the HUD rows; delete the `CrosshairUI` hover gotcha; delete
  the `HealthUI` row; update the two-design-languages statement to three.
- **`docs/AI/systems/Oxygen.md`** — **delete the line "Nothing consumes oxygen"**, which this
  change makes false, and fold the `Extending` recipe into the real design.
- **`docs/AI/systems/Combat.md`** — the new directional damage channel.
- **`docs/Human/the-systems.md`** — a short plain-language entry for the visor; the validator
  fails without it.
- Bump `updated:` on every touched doc, then `python3 tools/docs_check.py --index`.

## Out of scope

- Any AI *character* — this is a system-message channel, not a companion.
- A world-space projected canvas on real visor geometry (rejected: text legibility through a
  render texture, hotbar raycasting, and it breaks `UIScale`'s single-canvas rule).
- Changing the hotbar from 4 slots to 3. Flagged, not assumed.
- Reworking `PostProcessVisor` / `VisorOverlayController`, which handle glass distortion and sun
  flare and are unrelated to the readout layer.
