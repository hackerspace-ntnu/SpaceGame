# Cutscenes

Take control of the player camera + input for a scripted moment, then give it back. Coroutine-based; no Timeline, no Cinemachine.

## Concepts

- **`Cutscene`** — abstract `MonoBehaviour`. Subclass and write `IEnumerator Play(CutsceneContext)`.
- **`CutsceneDirector`** (singleton in `persistentScene`) — runs one `Cutscene` at a time. Locks player input + HUD on entry, restores on exit (even on exception). Shows letterbox bars while playing.
- **`LetterboxOverlay`** (auto-spawned, `DontDestroyOnLoad`) — bars + black fade. Use for any fade, not just cutscenes.

## Built-in cutscenes

| Class | Effect |
|---|---|
| `LookAtCutscene` | Rotate FP camera toward a target, hold, rotate back. |
| `WalkThroughDoorCutscene` | FP camera glides through a `throughPoint`. |
| `ThirdPersonWalkThroughCutscene` | Spawn a temp camera behind/above/side, dolly out while moving the player. |
| `CameraShakeCutscene` | Perlin jitter on the FP camera. No movement, no scene change. |

## Triggering a cutscene

| Trigger | Component | Use case |
|---|---|---|
| Walk into a volume | `CutsceneTriggerVolume` | Discovery moments, area transitions. |
| Click → cutscene → arbitrary actions | `CutsceneInteractable` | "Click → cutscene → UnityEvent." Wire any post-actions. |
| Click → cutscene → go somewhere | `InteriorPortal` | Doors. Cutscene + destination (interior scene or same-scene anchor) in one component. |
| From code | `CutsceneDirector.Instance.Play(myCutscene)` | Story beats, death, etc. |

`InteriorPortal` is the recommended path for any "this door takes me somewhere." See [INTERIORS.md](INTERIORS.md).

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

## Showcase in `persistentScene`

Four stations in front of the player spawn:

| Station | Demo |
|---|---|
| **A** (red) | `InteriorPortal` + `ThirdPersonWalkThroughCutscene` → loads `InsideRuin` |
| **B** (green) | `CutsceneInteractable` + `LookAtCutscene`. No scene change. |
| **C** (grey) | `CutsceneInteractable` + `CameraShakeCutscene`. "Locked door." |
| **D** (gold pad) | `CutsceneTriggerVolume` + `LookAtCutscene`. Walk on it. |

## Files

```
Assets/Scripts/Cutscenes/
├── Cutscene.cs                          base + CutsceneContext
├── CutsceneDirector.cs                  singleton, lifecycle
├── CutsceneTriggerVolume.cs             walk-into trigger
├── CutsceneInteractable.cs              click → cutscene → UnityEvent
├── InteriorPortal.cs                    click → cutscene → teleport
├── EnterInteriorOnEvent.cs              UnityEvent helper for interior loads
├── LookAtCutscene.cs
├── WalkThroughDoorCutscene.cs
├── ThirdPersonWalkThroughCutscene.cs
├── CameraShakeCutscene.cs
└── UI/LetterboxOverlay.cs
```

`PlayerController.EnterCutsceneMode()` / `ExitCutsceneMode()` is what the Director uses. Captures + restores prior state, so it's safe mid-mount.

## Limitations

- **One cutscene at a time.** Concurrent `Play` rejects with a warning.
- **Local-only (no NetCode).** Cutscenes run on the client that triggered them. Don't mutate networked state inside one.
- **`Time.deltaTime`.** If `timeScale = 0` (pause), camera moves freeze. `LetterboxOverlay` uses unscaled time and is fine.
- **No audio duck.** Music plays at full volume through cutscenes.
- **`ThirdPersonWalkThroughCutscene` writes the player Rigidbody directly** — fine offline, fights `NetworkTransform` in MP.
