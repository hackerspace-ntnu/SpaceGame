# Interiors

Server-authoritative additive scene loader for "interior" spaces (taverns, ruins, ship interiors, ...). The exterior never unloads — interiors are layered on top of the persistent scene so re-exit is instant and `SceneTracked` entities outside stay alive.

This is the layer that the [Scene Transition system](../Transitions/README.md) sits on top of. New code should go through `SceneTransition` + `InteriorSceneDestination` rather than calling `InteriorManager` directly.

## Files

| File | Role |
|---|---|
| `InteriorManager.cs` | Singleton in the persistent scene. Loads interiors additively, tracks return positions, refcounts shared interiors. RPC entry points + offline fallback via `Network.Execute`. |
| `InteriorScene.cs` | ScriptableObject pairing a Unity scene asset with a spawn-anchor id. Edit-time validation: anchor exists, scene is in Build Settings. |
| `InteriorAnchor.cs` | Marks a spawn / exit point inside an interior scene. Self-registers in a `(scene, id)` dictionary. |
| `InteriorEntrance.cs` | **Legacy.** `IInteractable` door that calls `InteriorManager.EnterInterior`. Replaced by `SceneTransition` + `InteractableTransitionTrigger` + `InteriorSceneDestination`. |
| `InteriorExit.cs` | **Legacy.** `IInteractable` door inside an interior that calls `InteriorManager.ExitInterior`. |
| `InteriorTestBootstrap.cs` | Self-installing test harness. Disabled by default now that real entrances exist. |

## How loading works

1. A caller invokes `InteriorManager.Instance.EnterInterior(player, interiorScene)`.
2. `Network.Execute` routes to the server (RPC if networked, direct call offline).
3. Server records the player's exterior position + scene in `returnInfoByPlayer`.
4. If the interior scene is already loaded, refcount is incremented and the player is moved straight to the spawn anchor.
5. Otherwise the scene loads additively — via `NetworkManager.SceneManager.LoadScene` when networked, or `SceneManager.LoadSceneAsync` offline.
6. On load complete: `SceneManager.MoveGameObjectToScene` moves the player into the interior scene, then `TeleportPlayer` snaps them to the anchor (CharacterController-aware: disables/re-enables; falls back to Rigidbody zeroing).
7. Exit is the mirror: move player back to the recorded exterior scene + position, decrement refcount, unload the interior if no one's left.

## Anchors

`InteriorAnchor` self-registers in a static dictionary keyed by `(scene.name, anchorId)`. `InteriorAnchor.Find` and `InteriorAnchor.FindAnywhere` are the lookup APIs. There's a hierarchy-scan fallback in case `OnEnable` ordering misses a registration.

> **Caveat for future instancing:** the dictionary key is the scene *name*. Two loaded scenes with the same name (template-based instancing) would collide. When instanced interiors land, the key needs to become a `Scene` handle.

## Adding a new interior

1. Create the Unity scene. Add it to Build Settings (and enable it).
2. Place one or more `InteriorAnchor` GameObjects inside it. Give each an id (e.g., `"entrance"`, `"upstairs"`).
3. Bake the NavMesh into the scene if AI is involved.
4. `Create → Scene Management → Interior Scene` to make the SO. Drag the scene asset in; set `spawnAnchorId` to match an anchor you placed.
5. Reference the SO from a destination — typically `InteriorSceneDestination` driven by a `SceneTransition`.

## Caveats / known limitations

- **Single shared instance per interior.** Refcount assumes everyone entering "Interior_Tavern" lands in the *same* loaded scene. Fine for hub spaces; insufficient for instanced dungeons or per-player housing.
- **Loaded at world origin.** Interior geometry overlaps with whatever streamed exterior chunk happens to be at `(0,0,0)`. Currently fine because nothing important sits at origin; a real fix is offsetting interior loads to a far-away "interior bay."
- **Return info is in-memory only.** Lost on host migration / server restart. For persistent worlds this needs to live on the player's save data.
- **`async void` in `Bootstrapper.AfterSceneLoad`.** Swallows exceptions. Low risk but a sharp edge.

## Dependencies

- `Network.Execute` / Unity Netcode for GameObjects — server-authoritative routing.
- `LetterboxOverlay` — used indirectly via the legacy `InteriorExit` for fade chains. New code uses `FadeToBlackEffect` instead.
- `SceneTracked` (`WorldStreaming/`) — entities marked with this survive in the unmodified exterior while a player is inside an interior.
