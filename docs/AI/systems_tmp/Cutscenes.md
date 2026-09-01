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
| `ArrivalCutscene` | The crash landing that opens a new world. Seated free look plus shake; the hull is flown by `ArrivalDirector` on the server. |

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

## The crash landing arrival

A new world opens with the crew strapped into `PlayerShip` on its way down. Three pieces, on three
different footings:

| Piece | Runs on | Job |
| --- | --- | --- |
| `ArrivalDirector` | server | Resolves the impact site, spawns the ship at altitude, seats each arriving player, walks the hull down `ArrivalTrajectory`, releases everyone at the end. |
| `SeatedRider` | every machine | Holds bodies at `ShipSeat` markers. Event channel plus a re-asserting state channel, so a client joining mid-descent is seated correctly. |
| `ArrivalCutscene` | locally, per player | Letterbox, fade, shake beats. Mutates nothing. |
| `CabinAlert` | every machine | Red alert lamps throbbing in the cabin while anyone is riding the crash down. |

It fires once per world and is recorded by `ArrivalSaveable` under the `arrival` key, so a loaded
save never replays it. The wreck persists on its own — `PlayerShip` already carries a
`SaveableEntity` with a `TransformSaveable`.

`ArrivalTrajectory` is a **descending spiral**: horizontal radius shrinks linearly from a lateral
budget to zero while the bearing sweeps, and altitude falls as `1 - t^2`. Three things that shape
buys, which a Bezier would not:

- The lateral budget is respected *by construction*. That budget is a **world-streaming** limit —
  chunks are 500 m and pin under tracked entities, so a cross-map descent would drag the streamer
  through a dozen of them at speed.
- It terminates exactly on the impact pose. The wreck is persisted where the trajectory leaves it,
  so "close" would mean a hull permanently buried or hovering.
- `1 - t^2` falls slowly then fastest at the end, which is the ground rush. The obvious
  `(1 - t)^2` does exactly the opposite.

### The cabin alarm

`CabinAlert` drives four red point lamps inside the hull. It is switched by `SeatedRider`, not by
`ArrivalCutscene`, and that is deliberate: the cutscene runs only on a machine whose own player is in
a chair, whereas the alarm has to be lit on every machine that can see the cabin — including one
watching a crewmate through the canopy. `SeatedRider` reads it off the replicated occupancy, so every
machine reaches the same answer with nothing extra on the wire.

Only a `SeatingReason.Arrival` lights it. Sitting down in a parked ship is not an emergency.

The lamps are switched **off** between flashes rather than dimmed to zero: a URP point light at zero
intensity is still culled, sorted and considered for everything in range, and these live inside a
hull that is often on screen. The pulse is a folded sine raised to a sharpness exponent, which keeps
the peak at exactly `peakIntensity` however sharp it is set — about a 1.1 s cycle that goes fully
dark for a third of it.

### Seating does not reparent anything

The obvious implementation — parent the body to the seat marker — throws
`InvalidParentException`: netcode refuses to put a spawned `NetworkObject` under a plain transform,
and a `ShipSeat` marker is exactly that. `MountModule.ParentRiderToMount` works around it by
parenting to the mount's own `NetworkObject` and folding the marker's offset into that root's local
space.

**The arrival does not need that workaround**, because of two facts about `PlayerCharacterNetworked`:
its `NetworkTransform` is **owner-authoritative** (`AuthorityMode = Owner`) and replicates in
**world space** (`InLocalSpace = false`). So parenting is not what would make a rider ride — the
owner's world position is what travels. The server cannot place a client's body at all, and a parent
the server set would not move a remote body one metre.

What actually carries a player is `SeatedRider.HoldSeats`: every frame, each machine writes *its own*
players to their seat's current world pose, and that position replicates outward like any other
movement. Bodies a machine does not own are left alone — their owner is doing the same job, and
writing them locally would be a guess fighting the wire.

The same fact decides where the cutscene starts. The descent is flown by the server, so a
presentation started from that coroutine plays for the host and nobody else. `SeatedRider` raises a
static `LocalPlayerSeated` when it seats *this machine's* player, and `ArrivalDirector` subscribes to
it on every machine — that event is the only moment that is true on exactly the machines that need it.

`ArrivalCameraRig` reads the `Look` action straight from `InputSystem.actions` rather than through
`PlayerInputManager`, because the cutscene runs with the player's input disabled and that component
zeroes its look axis in `OnDisable`. Leaving input enabled instead would let jump and dash through —
they are delivered as events whose handlers fire regardless of `PlayerMovement.enabled`.
`MountModule.Camera.cs` does the same thing for the same reason. It also bypasses `PlayerLook`,
which spends its yaw turning the player's *Rigidbody*, and would fight the seat it is parented into.

Shake is capped and multiplied by `GameSettings.CameraShakeIntensity`, which reaches a true zero
(`ShakeMath` early-returns rather than scaling, because Perlin noise is 0.5 at its origin and would
otherwise leave a constant offset). The descent runs for many seconds and **cannot be skipped**, so
that setting is an accessibility requirement rather than a polish option — see `GDC-L1-FEEL-0006`.

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

  The crash landing arrival is the worked way around this: `ArrivalCutscene` is pure presentation
  and mutates nothing, while everything that has to agree across machines — the hull's motion, who
  is in which seat — lives in `ArrivalDirector` and `SeatedRider` on the server. A cutscene that
  needs to be correct everywhere should split the same way rather than wait for the director itself
  to be networked.
- **`Time.deltaTime`.** If `timeScale = 0` (pause), camera moves freeze. `LetterboxOverlay` uses unscaled time and is fine.
- **No audio duck.** Music plays at full volume through cutscenes.
- **`ThirdPersonWalkThroughCutscene` writes the player Rigidbody directly** — correct offline, and it
  fights `NetworkTransform` in a session. It has to move to `NetworkedTeleport.Move`.
