# Interiors

Server-authoritative additive scene loading. Click a door, the interior scene loads alongside the exterior, the player teleports in. The exterior keeps streaming around the player's old position so re-exit is instant.

## Concepts

| Piece | What it is |
|---|---|
| `InteriorScene` (ScriptableObject) | Names a scene + a spawn anchor id. Lives in `Assets/Resources/Interiors/`. |
| `InteriorAnchor` (component) | A spawn / exit marker inside a scene. Self-registers by `(scene, id)`. Also usable for same-scene teleports via `InteriorPortal`. |
| `InteriorManager` (singleton) | Loads/unloads interior scenes additively. Refcounts so multiple players don't fight. NetCode-aware. Lives in `persistentScene`. |

## Triggers

| Component | Use case |
|---|---|
| `InteriorPortal` (in `Assets/Scripts/Cutscenes/`) | **Recommended.** Cutscene + destination + fade in one component. Destination is either an `InteriorScene` or a same-scene anchor id. See [CUTSCENES.md](CUTSCENES.md). |
| `InteriorEntrance` | Minimal entrance. No fade, no cutscene. Drop on a door, drag an `InteriorScene`, done. |
| `InteriorExit` | Exit door inside an interior. Fades to black, calls `ExitInterior`, fades back. |

## Adding a new interior

1. **New Scene** → save as `Assets/Scenes/Interiors/MyInterior.unity`. Build the room.
2. Drop an empty GameObject named `EntranceAnchor`, position + rotate it where the player should spawn. Add `InteriorAnchor`, set `anchorId = "entrance"`.
3. Add an `InteriorExit` to a door so the player can come back.
4. **File → Build Profiles → Add Open Scenes** (otherwise `LoadSceneAsync` returns null).
5. **Create → Scene Management → Interior Scene** in `Assets/Resources/Interiors/`. Drag the scene asset in; `OnValidate` syncs the name. Set `spawnAnchorId = "entrance"`.
6. In your exterior, place an `InteriorPortal`, drag the new `InteriorScene` into `targetInterior`.

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

Door A in the persistentScene showcase plaza targets `Interior_InsideRuin`.

## Files

```
Assets/Scripts/SceneManagement/Interiors/
├── InteriorManager.cs           singleton loader
├── InteriorScene.cs             ScriptableObject (sceneName + anchorId)
├── InteriorAnchor.cs            spawn/exit marker; FindAnywhere/TeleportPlayer helpers
├── InteriorEntrance.cs          minimal IInteractable
├── InteriorExit.cs              IInteractable; fades around exit
└── InteriorTestBootstrap.cs     test-only auto-spawner (off by default)

Assets/Resources/Interiors/      InteriorScene assets
Assets/Scenes/Interiors/         the .unity scenes
```

## Limitations

- Scene + anchor ids are strings. Typos give "dropped at origin" at runtime. `InteriorScene.OnValidate` catches missing Build Settings entries and (when the target scene is loaded) missing anchors.
- `SceneTracked` entities (vehicles, mounts) don't follow the player inside.
- Items dropped inside an interior are lost when the last player leaves and the scene unloads.
