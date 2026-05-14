using System.Collections;
using UnityEngine;

/// <summary>
/// DontDestroyOnLoad host that runs SceneTransition orchestrator coroutines.
///
/// The transition GameObject (a door, a portal, ...) often lives in a scene that the
/// transition itself unloads — e.g. an InteriorExit inside the interior. If the
/// coroutine ran on that GameObject, it would die mid-transition and effects would
/// never receive End(), leaving the screen stuck black.
///
/// Self-instantiates on first use, like LetterboxOverlay.
/// </summary>
public class TransitionRunner : MonoBehaviour
{
    private static TransitionRunner s_instance;

    public static TransitionRunner Instance
    {
        get
        {
            if (s_instance != null) return s_instance;
            var found = FindFirstObjectByType<TransitionRunner>();
            if (found != null) { s_instance = found; return s_instance; }
            var go = new GameObject("TransitionRunner");
            go.AddComponent<TransitionRunner>();
            return s_instance;
        }
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);
}
