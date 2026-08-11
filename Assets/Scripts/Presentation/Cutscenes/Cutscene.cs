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

    /// <summary>
    /// The GameObject the cutscene is "about" — usually the player, but a cutscene
    /// driven from an AI scripted sequence could pass an agent here. Defaults to
    /// the resolved Player's GameObject when no explicit subject is supplied.
    /// </summary>
    public readonly GameObject Subject;

    public CutsceneContext(PlayerController player, Camera playerCamera, GameObject subject)
    {
        Player = player;
        PlayerCamera = playerCamera;
        Subject = subject;
    }
}
