# Cutscenes

Take control of the player camera + input for a scripted moment, then give it back. Coroutine-based; no Timeline, no Cinemachine.

## Concepts

- **`Cutscene`** — abstract `MonoBehaviour`. Subclass and write `IEnumerator Play(CutsceneContext)`. `CutsceneContext` carries `Player`, `PlayerCamera`, and `Subject` (the GameObject the cutscene is "about" — usually the player, can be an AI agent).
- **`CutsceneDirector`** (singleton in `persistentScene`) — runs one `Cutscene` at a time. Locks player input + HUD on entry, restores on exit (even on exception). Shows letterbox bars while playing. `Play(cutscene, subject)` takes the subject; the no-subject overload falls back to the local player.
- **`CutsceneRunner`** — `IEnumerator PlayAndAwait(cutscene, initiator?)` for chaining post-cutscene work. Used by `CutsceneAction`, `WalkThroughCutsceneEffect`, and ad-hoc story code.
- **`LetterboxOverlay`** (auto-spawned, `DontDestroyOnLoad`) — bars + black fade. Use for any fade, not just cutscenes.

## Built-in cutscenes

| Class | Effect |
|---|---|
| `LookAtCutscene` | Rotate FP camera toward a target, hold, rotate back. |
| `WalkThroughDoorCutscene` | FP camera glides through a `throughPoint`. |
| `ThirdPersonWalkThroughCutscene` | Spawn a temp camera behind/above/side, dolly out while moving the player. |
| `CameraShakeCutscene` | Perlin jitter on the FP camera. No movement, no scene change. |

## Triggering a cutscene

Cutscenes are wired through the generic [trigger seam](Assets/Game/Scripts/InteractionSystem/README.md): a `CutsceneAction` component on a GameObject implements `ITriggerable`, and a separate trigger component decides how it fires.

| Trigger | Component | Use case |
|---|---|---|
| Walk into a volume | `VolumeTrigger` + `CutsceneAction` | Discovery moments, area transitions. |
| Click → cutscene → arbitrary actions | `InteractableTrigger` + `CutsceneAction` | "Click → cutscene → UnityEvent." Wire any post-action on `CutsceneAction.onCutsceneEnded`. |
| Click → cutscene → go somewhere | `InteractableTrigger` + `SceneTransition` + `WalkThroughCutsceneEffect` + a destination | Doors. See [INTERIORS.md](INTERIORS.md). |
| From code | `CutsceneDirector.Instance.Play(myCutscene, subject)` | Story beats, death, etc. |

For doors, prefer the [`SceneTransition`](Assets/Game/Scripts/SceneManagement/Transitions/SceneTransition.cs) stack — it composes a cutscene effect with a fade effect and a destination so you can mix and match.

## Writing a new cutscene

```csharp
public class MyCutscene : Cutscene
{
    [SerializeField] Transform target;
    [SerializeField] float duration = 2f;

    public override IEnumerator Play(CutsceneContext ctx)
    {
        var cam = ctx.PlayerCamera.transform;
        var start = cam.rotation;
        var end = Quaternion.LookRotation(target.position - cam.position);
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            cam.rotation = Quaternion.Slerp(start, end, t / duration);
            yield return null;
        }
    }
}
```

For a third-person camera: disable `ctx.PlayerCamera`, spawn your own with `AudioListener`, restore in `finally`, destroy on exit. See `ThirdPersonWalkThroughCutscene`.

To act on whoever triggered the cutscene (e.g. an AI agent walking through a door), read `ctx.Subject` — it's the initiator passed by the caller and falls back to `Player.gameObject` when no explicit subject was supplied.

## Showcase in `persistentScene`

Four stations in front of the player spawn:

| Station | Demo |
|---|---|
| **A** (red) | `SceneTransition` + `WalkThroughCutsceneEffect` + `ThirdPersonWalkThroughCutscene` + `InteriorSceneDestination` → loads `RuinInterior` |
| **B** (green) | `CutsceneAction` + `LookAtCutscene`. No scene change. |
| **C** (grey) | `CutsceneAction` + `CameraShakeCutscene`. "Locked door." |
| **D** (gold pad) | `VolumeTrigger` + `CutsceneAction` + `LookAtCutscene`. Walk on it. |

## Files

```
Assets/Game/Scripts/Cutscenes/
├── Cutscene.cs                          base + CutsceneContext (Player, Camera, Subject)
├── CutsceneDirector.cs                  singleton; Play(cutscene, subject)
├── CutsceneRunner.cs                    PlayAndAwait helper
├── CutsceneAction.cs                    ITriggerable action: play cutscene + fire UnityEvent
├── LookAtCutscene.cs
├── WalkThroughDoorCutscene.cs
├── ThirdPersonWalkThroughCutscene.cs
├── CameraShakeCutscene.cs
└── UI/LetterboxOverlay.cs
```

`PlayerController.EnterCutsceneMode()` / `ExitCutsceneMode()` is what the Director uses. Captures + restores prior state, so it's safe mid-mount.

## Current state

The target is full multiplayer: every cutscene correct on every machine, including late joiners.
The items below are work still to do, not decisions to stop here.

- **One cutscene at a time.** Concurrent `Play` rejects with a warning.
- **Not replicated yet.** A cutscene runs only on the client that triggered it. Until it is routed
  through `NetMessaging` (see `spacegame-multiplayer`), don't mutate networked state inside one —
  the mutation would land on one machine only.
- **`Time.deltaTime`.** If `timeScale = 0` (pause), camera moves freeze. `LetterboxOverlay` uses unscaled time and is fine.
- **No audio duck.** Music plays at full volume through cutscenes.
- **`ThirdPersonWalkThroughCutscene` writes the player Rigidbody directly** — correct offline, and it
  fights `NetworkTransform` in a session. It has to move to `NetworkedTeleport.Move`.
