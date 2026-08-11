# Interiors

Server-authoritative additive scene loading. Click a door, the interior scene loads alongside the exterior, the player teleports in. The exterior keeps streaming around the player's old position so re-exit is instant.

## Concepts

| Piece | What it is |
|---|---|
| `InteriorScene` (ScriptableObject) | Names a scene + a spawn anchor id. Lives in `Assets/Resources/Interiors/`. |
| `InteriorAnchor` (component) | A spawn / exit marker inside a scene. Self-registers by `(scene, id)`. Also usable for same-scene teleports. |
| `InteriorManager` (singleton) | Loads/unloads interior scenes additively. Refcounts so multiple players don't fight. NetCode-aware. Lives in `persistentScene`. |

## The transition stack (recommended)

Interior doors are wired through the generic [`SceneTransition`](Assets/Scripts/SceneManagement/Transitions/SceneTransition.cs) orchestrator. It composes three plug-points:

```
GameObject (the door)
├── SceneTransition                 — orchestrator (ITriggerable)
├── InteractableTrigger             — fires on E-press
│       OR VolumeTrigger            — fires on walk-in
├── Cutscene (optional)             — e.g. WalkThroughDoorCutscene
└── (assigns)
    ├── effects[]
    │   ├── FadeToBlackEffect            (ScriptableObject)
    │   └── WalkThroughCutsceneEffect    (ScriptableObject) — plays the Cutscene on this GO
    └── destination
        └── InteriorSceneDestination | ExitInteriorDestination | SameSceneAnchorDestination
```

| Destination (ScriptableObject) | Use case |
|---|---|
| `InteriorSceneDestination` | Additive scene load + place at spawn anchor. The common "enter the ruin" case. |
| `ExitInteriorDestination` | Drop on a door inside an interior; sends the player back to where they came from. |
| `SameSceneAnchorDestination` | Teleport to an `InteriorAnchor` in any loaded scene. No scene load. "Portal between rooms" / "spawn on ship bridge." |

Effects use distinct `TransitionChannel`s (Screen / Audio / Camera / Time). `SceneTransition` warns at edit-time if two effects fight for the same channel.

`InteriorEntrance` still exists as the minimal "no fade, no cutscene" entry door (used by `InteriorTestBootstrap`). It's not deprecated — just simpler than the full transition stack when you don't need effects.

## Adding a new interior

1. **New Scene** → save as `Assets/Scenes/Interiors/MyInterior.unity`. Build the room.
2. Drop an empty GameObject named `EntranceAnchor`, position + rotate it where the player should spawn. Add `InteriorAnchor`, set `anchorId = "entrance"`.
3. Add an exit door (any GO with `SceneTransition` + `InteractableTrigger` + `ExitInteriorDestination` + `FadeToBlackEffect`).
4. **File → Build Profiles → Add Open Scenes** (otherwise `LoadSceneAsync` returns null).
5. **Create → Scene Management → Interior Scene** in `Assets/Resources/Interiors/`. Drag the scene asset in; `OnValidate` syncs the name. Set `spawnAnchorId = "entrance"`.
6. In your exterior, place the entrance door (GO with `SceneTransition` + `InteractableTrigger` + an `InteriorSceneDestination` pointing at your `InteriorScene`, plus any effects you want).

## Multiplayer

- All `EnterInterior` / `ExitInterior` calls route through `ServerRpc`; the server is authoritative for the load + teleport.
- Interiors load via `NetworkManager.SceneManager.LoadScene` when networked, `SceneManager.LoadSceneAsync` offline.
- Refcounted by `NetworkObjectId`; the interior unloads when the last player leaves.
- Late joiners don't auto-load active interiors (known limit).

## Currently wired

| Asset | Scene |
|---|---|
| `Interior_Test.asset` | `TestInterior.unity` (4-wall placeholder) |
| `Interior_InsideRuin.asset` | `InsideRuin.unity` (16×16 room with altar, crates, pillars) |

Door A in the persistentScene showcase plaza targets the ruin interior via `Destination_EnterRuin` (which references `Interior_RuinDemo` → `RuinInterior.unity`).

## Files

```
Assets/Scripts/SceneManagement/Interiors/
├── InteriorManager.cs              singleton loader (NetCode-aware)
├── InteriorScene.cs                ScriptableObject (sceneName + anchorId)
├── InteriorAnchor.cs               spawn/exit marker; FindAnywhere/TeleportPlayer helpers
├── InteriorEntrance.cs             minimal IInteractable (no fade, no cutscene)
└── InteriorTestBootstrap.cs        test-only auto-spawner (off by default)

Assets/Scripts/SceneManagement/Transitions/
├── SceneTransition.cs              orchestrator (ITriggerable)
├── TransitionRunner.cs             DontDestroyOnLoad coroutine host
├── Destinations/
│   ├── SceneDestination.cs             abstract base
│   ├── InteriorSceneDestination.cs     additive load + place at anchor
│   ├── ExitInteriorDestination.cs      return to recorded exterior position
│   └── SameSceneAnchorDestination.cs   teleport to anchor in any loaded scene
└── Effects/
    ├── SceneTransitionEffect.cs        abstract base + EffectHandle + TransitionChannel
    ├── FadeToBlackEffect.cs            screen channel, spacebar-skippable in-phase
    └── WalkThroughCutsceneEffect.cs    camera channel, blocks load until cutscene done

Triggers live in the InteractionSystem (InteractableTrigger, VolumeTrigger).

Assets/Resources/Interiors/         InteriorScene + Destination + Effect assets
Assets/Scenes/Interiors/            the .unity scenes
```

## Limitations

- Scene + anchor ids are strings. Typos give "dropped at origin" at runtime. `InteriorScene.OnValidate` catches missing Build Settings entries and (when the target scene is loaded) missing anchors.
- `SceneTracked` entities (vehicles, mounts) don't follow the player inside.
- Items dropped inside an interior are lost when the last player leaves and the scene unloads.
