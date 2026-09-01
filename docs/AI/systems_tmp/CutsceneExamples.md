CutsceneObjects — example prefabs
=================================
Drag any of these into a scene to see a cutscene type wired up. They are
templates, not finished props — clone the prefab (Right-click > Unpack) and
swap the visuals for whatever fits your scene.

Each prefab pairs a Cutscene component with the trigger that fires it
(CutsceneInteractable for clickable, CutsceneTriggerVolume for walk-in,
InteriorPortal for go-somewhere).

Examples
--------
- Example_CameraShake_Rubble       Click → small camera shake. CameraShakeCutscene.
- Example_LookAt_Beacon            Click pedestal → camera pans to a beacon. LookAtCutscene.
- Example_WalkThroughDoor_FP       Click door → first-person walk-through. WalkThroughDoorCutscene.
- Example_WalkThroughDoor_TP       Click door → third-person walk-through. ThirdPersonWalkThroughCutscene.
- Example_InteriorPortalDoor       Click door → cutscene + fade + teleport. Set destination on InteriorPortal.
- Example_TriggerVolume_Shake      Walk in → camera shake. CutsceneTriggerVolume + CameraShakeCutscene.

Wiring tips
-----------
- The Cutscene component lives on the same GameObject as the trigger and is
  referenced by the trigger's 'cutscene' field — keeps it portable.
- Many cutscenes use a child Transform target (LookAt's 'target',
  WalkThroughDoor's 'throughPoint'). Move that child to retarget the cutscene
  without touching the script.
- InteriorPortal needs an InteriorScene asset OR a SameSceneAnchor id —
  these examples leave the destination blank so designers can fill it in.
- CutsceneInteractable also exposes an OnCutsceneEnded UnityEvent — wire
  post-cutscene actions (sounds, dialog, scene change) there.
