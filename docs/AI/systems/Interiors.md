---
system: Interiors
layer: world
summary: Merged into SceneTransitions.md; this page is a pointer with the current interior asset paths.
paths:
  - Assets/Game/Scripts/Core/SceneManagement/
symptoms:
  - "looking for the interiors doc"
  - "where did Assets/Game/Scripts/SceneManagement/ go"
  - "how does entering a cave or building load its scene"
reads_with: [SceneTransitions, Portals]
redirect_to: SceneTransitions
updated: 2026-09-01
---

# Interiors — moved

Merged into **[SceneTransitions.md](SceneTransitions.md)**, which now covers the whole subsystem:
additive interior loading/unloading, the `SceneTransition` orchestrator, and teleporting.

Quick pointers:

- Scripts moved under [`Assets/Game/Scripts/Core/SceneManagement/`](Assets/Game/Scripts/Core/SceneManagement) — the old `Assets/Game/Scripts/SceneManagement/` paths are gone.
- Triggers live in the interaction system: [`VolumeTrigger.cs`](Assets/Game/Scripts/Gameplay/Interaction/Triggers/VolumeTrigger.cs), [`InteractableTrigger.cs`](Assets/Game/Scripts/Gameplay/Interaction/Triggers/InteractableTrigger.cs).
- Live interior assets: `Interior_AlgeaCave`, `Interior_SandstoneCave` in [`Assets/Game/Resources/Interiors/`](Assets/Game/Resources/Interiors).
- Every instant move goes through `SaveTeleport.Move` / `NetworkedTeleport.Move` — see [SceneTransitions.md](SceneTransitions.md).
- Portals are a separate mechanism — see [Portals.md](Portals.md).
