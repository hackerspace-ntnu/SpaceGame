using System.Collections;
using UnityEngine;

// Base class for any scripted cutscene. Subclass and implement Play().
// CutsceneDirector handles input lock + restore around the coroutine — your Play()
// just describes what should happen on screen.
public abstract class Cutscene : MonoBehaviour
{
    public abstract IEnumerator Play(CutsceneContext ctx);
}

public readonly struct CutsceneContext
{
    public readonly PlayerController Player;
    public readonly Camera PlayerCamera;

    public CutsceneContext(PlayerController player, Camera playerCamera)
    {
        Player = player;
        PlayerCamera = playerCamera;
    }
}
