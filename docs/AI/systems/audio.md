---
system: Audio
layer: presentation
summary: FMOD behind an SfxId vocabulary; one Resources AudioCatalog maps meaning to event and tuning.
paths:
  - Assets/Game/Scripts/Audio/
  - Assets/Game/Scripts/Presentation/Audio/
  - Assets/Game/Scripts/agents/Audio/
  - Assets/Game/Resources/AudioCatalog.asset
  - Assets/Game/Audio/
symptoms:
  - "no sound plays for the client but the host hears it"
  - "a sound went silent and only a single warning appeared in the console"
  - "I fixed the mapping mid-session and it is still silent"
  - "an ambience or engine loop keeps playing after the object was destroyed"
  - "menu buttons do not click when MainMenu is entered directly"
  - "the volume sliders in the settings menu do nothing"
  - "I want to add a new sound and cannot find where to author the FMOD event"
  - "I have an mp3 or wav and need it played by a creature or a prop"
  - "an NPC's chatter mutes every other NPC of the same kind"
reads_with: [Multiplayer, AgentSystem, Combat, Cutscenes]
updated: 2026-09-03
---

# Audio

FMOD is the only playback backend; every gameplay sound is asked for by *meaning* (`SfxId`) and resolved to an FMOD event by one Resources-loaded `AudioCatalog` asset.

**Scope:** [`Assets/Game/Scripts/Audio/`](Assets/Game/Scripts/Audio) · [`Assets/Game/Scripts/Presentation/Audio/`](Assets/Game/Scripts/Presentation/Audio) · [`Assets/Game/Scripts/agents/Audio/`](Assets/Game/Scripts/agents/Audio) · [`Assets/Game/Resources/AudioCatalog.asset`](Assets/Game/Resources/AudioCatalog.asset) · [`Assets/Game/Audio/*.bank`](Assets/Game/Audio)
**Related:** [audio-prefab-inventory.md](audio-prefab-inventory.md) (generated per-prefab sweep) · [Multiplayer.md](Multiplayer.md) · [AgentSystem.md](AgentSystem.md)

## Model

- **FMOD is used, not replaced.** `SfxId`/`AudioCatalog` is an indirection layer *on top of* FMOD — every path bottoms out in `RuntimeManager.PlayOneShot` / `CreateInstance`.
- Call sites say `Sfx.Play(SfxId.PlayerJump, position)`. The catalog decides the event, cooldown, cull distance and volume trim.
- Components expose **both** an `SfxId` and an `EventReference`; a non-null inspector `EventReference` wins for the *asset*, but the catalog still supplies the *tuning* for that slot.
- `Sfx` is static, needs no manager and no scene object — deliberately, so menus, prefabs and items that exist before Bootstrap can make noise.
- **Nothing in the audio layer throws.** A missing catalog, missing bank or uninitialised FMOD Studio warns once per `SfxId` and then goes silent.
- One-shots are fire-and-forget; anything sustained must be owned by a [`LoopingEmitter`](Assets/Game/Scripts/Audio/LoopingEmitter.cs).
- [`SpaceGame.Audio.asmdef`](Assets/Game/Scripts/Audio/SpaceGame.Audio.asmdef) references only `FMODUnity`, so vehicle/creature asmdefs (which cannot reference `Assembly-CSharp`) can reach it.

## Key types

| Type | File | Role |
|---|---|---|
| `SfxId` | [SfxId.cs](Assets/Game/Scripts/Audio/SfxId.cs) | The vocabulary — **73** named sounds, explicit values grouped in hundreds. Serialized; never reuse a number. |
| `AudioCatalog` / `AudioCatalog.Entry` | [AudioCatalog.cs](Assets/Game/Scripts/Audio/AudioCatalog.cs) | ScriptableObject: `id → eventRef, cooldown, maxDistance, volume, note`. Static `Default` loads `Resources/AudioCatalog` once. |
| `Sfx` | [Sfx.cs](Assets/Game/Scripts/Audio/Sfx.cs) | Static façade: `Play`, `PlayAttached`, `Play2D`, `Reset`. Cooldown table + one-warning-per-id. |
| `LoopingEmitter` | [LoopingEmitter.cs](Assets/Game/Scripts/Audio/LoopingEmitter.cs) | Owns one sustained `EventInstance`. `Play/Stop/SetParameter/SetVolume/SetPosition`. Safe to double-Play and double-Stop. |
| `AudioLoop` | [AudioLoop.cs](Assets/Game/Scripts/Presentation/Audio/AudioLoop.cs) | Drop-on MonoBehaviour wrapper over `LoopingEmitter` (ambience, hums). Replaces FMOD's `StudioEventEmitter` so scene loops go through the catalog. |
| `AudioManager` | [AudioManager.cs](Assets/Game/Scripts/Presentation/Audio/AudioManager.cs) | **Bus volumes only** (`bus:/`, `/Music`, `/SFX`, `/UI`, `/Reverb`) from `GameSettings`. Singleton on Bootstrap. Not a playback route. |
| `PlayerAudioModule` | [PlayerAudioModule.cs](Assets/Game/Scripts/Presentation/Audio/PlayerAudioModule.cs) | Player voice: footsteps paced by **distance travelled** (`strideLength`), jump/land/dash, hurt/death/revive. |
| `EntityAudioModule` | [EntityAudioModule.cs](Assets/Game/Scripts/agents/Audio/EntityAudioModule.cs) | Creature/NPC voice: footsteps off `IMovementMotor.Velocity`, aggro on `ChaseModule` edge, randomised ambient mumbles. Also fires `NoiseEmitter`. |
| `UIButton` | [UIButton.cs](Assets/Game/Scripts/Presentation/UI/Buttons/UIButton.cs) | UI audio: `Sfx.Play2D(hoverId)` / `(pressId)` on pointer enter/down. |
| `NoiseEmitter` / `NoiseType` / `NoiseReceiverModule` | [agents/Audio/](Assets/Game/Scripts/agents/Audio) | **Not audio.** AI perception — `OverlapSphereNonAlloc` broadcast of "something was heard". Lives here because it is triggered alongside sounds. |

## Catalog

- Asset: [`Assets/Game/Resources/AudioCatalog.asset`](Assets/Game/Resources/AudioCatalog.asset) — path constant `AudioCatalog.ResourcePath`. Must stay under *some* `Resources` folder.
- **73 entries — one per non-`None` `SfxId`.** Groups: Player 100s, Weapons 200s, Impacts 300s, NPC/entity 400s, Interaction 500s, Wings 600s, Ship/vehicle 700s, Ambience 800s, UI 900s, Portals 1000s.
- Those 73 slots resolve to only **18 distinct FMOD events** (plus `event:/Music/TestSong` used directly by `AudioManager.PlayTestMusic` = 19 shipped events total). Heaviest reuse: `SFX/Wham` ×9, `SFX/ElectricHum` ×9, `UI/No` ×8, `SFX/Slurp` ×7, `SFX/Antigravity` ×7.
- **No FMOD Studio project.** [`FMODStudioSettings.asset`](Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset) has `ImportType: 0` (single bank folder), `TargetAssetPath: FMODBanks`, and **no `SourceProjectPath`** — the `.fspro` that built these banks is not in the repo. Banks are the compiled `.bank` files in [`Assets/Game/Audio/`](Assets/Game/Audio) and `Assets/StreamingAssets/` (Master, Master.strings, SFX, UI, Music). New events cannot be authored until a `.fspro` exists.
- **A new sound can only arrive as a Unity `AudioClip`.** Since events cannot be authored, the escape hatch is an inspector-pinned clip that beats the catalog id — the same shape as the pinned `EventReference` override, one layer further out. `FightOrFlightModule.roarClip` is the first: assigned, it plays through a lazily-built 3D `AudioSource` and the `SfxId` is ignored; empty, nothing changes. Build the `AudioSource` in code rather than authoring it on the prefab, so a creature with no clip never carries a dead one and the 3D settings cannot be half-set. This is the second documented exception after `SandstormAudio`, and unlike that one it **is** the pattern to copy until a `.fspro` exists.
- [`Assets/Game/Audio/GUIDs.txt`](Assets/Game/Audio/GUIDs.txt) is the authoritative manifest of everything the banks contain: 5 banks, 5 busses, **19 events**, and one parameter (`parameter:/Floor`). Read it before assuming an event exists.
- Every entry carries a `note` string; stand-in mappings are marked there, so `grep` the asset to find them. Per-slot event/cooldown/distance/volume values are in the asset — read it, do not mirror it here.
- Roughly **37 inspector `EventReference` assignments** still sit in prefabs/scenes and override the catalog for those components.

## Flows

**One-shot (3D)**
1. `Sfx.Play(id, position, overrideRef?, sourceKey)` → `PlayInternal`.
2. Resolve event: override if non-null, else `AudioCatalog.Default.TryGet(id)`. Null → warn once, return.
3. Cooldown: key is `(id << 32) ^ sourceKey`, so one chatty NPC rate-limits itself without muting its neighbour. Table pruned at 512 entries (>30 s stale).
4. Distance cull: if `entry.maxDistance > 0` and `StudioListener.ListenerCount > 0`, drop when `DistanceSquaredToNearestListener > maxDistance²`.
5. Play: `volume >= 0.999` → `PlayOneShot` (or `PlayOneShotAttached`). Otherwise `CreateInstance` → `setVolume` → attach or `set3DAttributes` → `start()` → **`release()` immediately** (FMOD reclaims on completion).

**2D one-shot** — `Sfx.Play2D(id, overrideRef?)`: same path with `ignoreDistance: true`, `position = zero`, `sourceKey = 0`. Used for UI, HUD, dialog open/close, scanner discovery.

**Loop / 3D source**
1. Own a `LoopingEmitter` field (or drop an `AudioLoop` on the object).
2. `emitter.Play(SfxId, attachTo, overrideRef)` — attach a `GameObject` to track movement, or pass `null` + `SetPosition` for a fixed point.
3. Drive `SetParameter(name, value)` / `SetVolume` per frame as needed (`AudioLoop.SetIntensity` exposes one configured parameter).

**Stop** — `emitter.Stop(allowFadeOut)`: detaches from the GameObject *first* (FMOD must not follow a Transform about to die), then `stop` → `release` → `clearHandle`. Must be called from **both** `OnDisable` and `OnDestroy`.

## Multiplayer

Sound is **never replicated**. There is no audio message in `NetMsg`/`NetRelay`; each machine plays locally off state or events it already has.

- **The rule:** put the `Sfx.Play` call on a code path that runs on *every* machine. Server-only code makes no sound for anyone else.
- **Replicated-event route (preferred):** `HealthComponent.OnRevive` / `OnDamage`, `RepairWorkstation.PlayFeedback` — health and feedback already fan out, so the sound fires everywhere and only on genuine success.
- **Present route:** `UsableItem.PlayUse` plays the use sound then calls `Present()`. `Use()` (server) must not play sound; `Present()` (all machines) must.
- **Local-immediate route:** [`PickupableItem`](Assets/Game/Scripts/Items/Core/PickupableItem.cs) plays the click *before* `Network.Execute`, accepting that a refused pickup still clicks, because a server round trip would lag the feedback.
- [`Projectile.OnImpact`](Assets/Game/Scripts/Weapons/Projectiles/Projectile.cs) is deliberately **not** gated on cosmetic/authority: every peer holds a copy of the shot, only one bills damage, but all should hear it land.

## Persistence

Volumes only, and not via the save system: [`GameSettings`](Assets/Game/Scripts/Core/Settings/GameSettings.cs) stores `MasterVolume` (1.0), `MusicVolume` (0.7), `SfxVolume` (1.0), `UiVolume` (0.85), `AmbienceVolume` (1.0) in **PlayerPrefs**, and raises `GameSettings.Changed`; `AudioManager.ApplySettings` pushes them onto the FMOD busses. No audio state is in world saves.

## Gotchas

- **`Sfx` warns once per `SfxId`, forever.** Fix a bank or a mapping mid-session and it stays silent — call `Sfx.Reset()` (auto-called on play-mode entry in editor) to clear `Complained` and the catalog cache.
- **`Play2D` uses `sourceKey = 0`.** All 2D sounds share one cooldown bucket per id — fine for UI, wrong if you want per-widget rate limiting.
- **Override does not override tuning.** An inspector `EventReference` picks the asset; cooldown, `maxDistance` and `volume` still come from the catalog entry for that `SfxId`. A slot with no entry gets `cooldown 0`, no cull, `volume 1`.
- **`EventLinkage: 0` (GUID).** Prefab `EventReference`s and the catalog bind by GUID. Recreating an event in a future FMOD project gives it a new GUID and silently breaks ~37 prefab assignments plus the catalog. Switch `EventLinkage` to Path before recreating anything.
- **`AudioManager.Instance` is null outside Bootstrap.** It lives on [`AudioManager.prefab`](Assets/Game/Prefabs/Systems/AudioManager.prefab) in `Bootstrap.unity` only. `UIButton` used to route through it and NREd when MainMenu was entered directly — which manifested as "buttons don't highlight". Always use `Sfx`.
- **`AudioManager` resolves busses lazily and tolerates failure.** `GetBus` throws while banks are still loading; it retries on the next `GameSettings.Changed`. Inspector volume sliders are an editor preview only — the settings menu re-asserts them.
- **Distance cull needs a listener.** With `StudioListener.ListenerCount == 0` the cull is *skipped*, not forced — everything plays. The listener rides [`Main Camera.prefab`](Assets/Game/Prefabs/Camera/Main%20Camera.prefab).
- **Loops leak on the untested teardown path.** `OnDisable` (scene unload) and `OnDestroy` (despawn) are different exits; `AudioLoop` handles both — copy that shape.
- **Duplicate ids in the catalog** are a warning, not an error: first entry wins. `OnValidate` clamps and invalidates the lookup.
- **[`SandstormAudio`](Assets/Game/Scripts/World/Environment/Sandstorm/Effects/SandstormAudio.cs) is a plain Unity `AudioSource` + `AudioLowPassFilter`, on purpose** — a 2D continuous loop driven by one number, which would need an FMOD project to author properly. It is the documented exception, not a pattern to copy.
- **[`AudioTestThingy`](Assets/Game/Scripts/Presentation/Audio/AudioTestThingy.cs) is dead** (empty `Start`/`Update`).

## Extending — add a new sound

1. Add a value to [`SfxId`](Assets/Game/Scripts/Audio/SfxId.cs) in the right hundreds group. **Take the next free number; never reuse a retired one.**
2. Add an entry in [`AudioCatalog.asset`](Assets/Game/Resources/AudioCatalog.asset): pick the closest existing FMOD event, set `cooldown` / `maxDistance` / `volume`, and put a `note` saying it is a stand-in if it is.
3. On the component, serialize both `[SerializeField] private SfxId xId = SfxId.New;` and `[SerializeField] private EventReference xSound;`.
4. Call `Sfx.Play(xId, transform.position, xSound, GetInstanceID())` — or `Play2D` for UI, `PlayAttached` for a long sound on a moving object, or a `LoopingEmitter` if it sustains.
5. Put the call on a path that runs on every machine (see **Multiplayer**), and stop any loop in both `OnDisable` and `OnDestroy`.
6. If the prefab is user-facing, add its row to [audio-prefab-inventory.md](audio-prefab-inventory.md).
