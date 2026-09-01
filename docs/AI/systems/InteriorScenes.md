---
system: InteriorScenes
layer: world
summary: Merged into SceneTransitions.md; this page is a pointer to InteriorManager and transit routing.
paths:
  - Assets/Game/Scripts/Core/SceneManagement/Interiors/
symptoms:
  - "looking for the interior scenes doc"
  - "where are InteriorManager, InteriorScene and InteriorAnchor"
  - "how is an interior visit saved and restored"
reads_with: [SceneTransitions, Portals]
redirect_to: SceneTransitions
updated: 2026-09-01
---

# Interior Scenes — moved

Merged into **[SceneTransitions.md](SceneTransitions.md)**, which now covers the whole subsystem:
additive interior loading/unloading, the `SceneTransition` orchestrator, and teleporting.

Quick pointers:

- `InteriorManager` / `InteriorScene` / `InteriorAnchor` — [`Core/SceneManagement/Interiors/`](Assets/Game/Scripts/Core/SceneManagement/Interiors)
- Server routing lives on the player: [`PlayerInteriorTransit.cs`](Assets/Game/Scripts/Core/SceneManagement/Interiors/PlayerInteriorTransit.cs) — `InteriorManager` has no `NetworkObject` and cannot host RPCs.
- Save/load of an interior visit: [`InteriorVisitSaveable.cs`](Assets/Game/Scripts/Core/Persistence/Adapters/InteriorVisitSaveable.cs), key `"interior"`.
- Portals are a separate mechanism — see [Portals.md](Portals.md).
