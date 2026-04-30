using System;
using System.Collections;
using UnityEngine;

// Local-per-client cutscene playback. Locks the player, runs a Cutscene's coroutine,
// restores on end (even if Play throws). One cutscene at a time; concurrent Play() rejects.
public class CutsceneDirector : MonoBehaviour
{
    public static CutsceneDirector Instance { get; private set; }

    public bool IsPlaying { get; private set; }

    public event Action<Cutscene> OnCutsceneStarted;
    public event Action<Cutscene> OnCutsceneEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool Play(Cutscene cutscene)
    {
        if (cutscene == null)
        {
            Debug.LogWarning("[CutsceneDirector] Play called with null cutscene.");
            return false;
        }
        if (IsPlaying)
        {
            Debug.LogWarning($"[CutsceneDirector] Rejecting '{cutscene.name}' — another cutscene is already playing.");
            return false;
        }

        StartCoroutine(RunCutscene(cutscene));
        return true;
    }

    private IEnumerator RunCutscene(Cutscene cutscene)
    {
        IsPlaying = true;

        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            Debug.LogError("[CutsceneDirector] No PlayerController in scene; aborting cutscene.");
            IsPlaying = false;
            yield break;
        }

        Camera cam = player.PlayerCamera;
        var ctx = new CutsceneContext(player, cam);

        player.EnterCutsceneMode();
        LetterboxOverlay.Instance.ShowBarsAsync(0.4f);
        OnCutsceneStarted?.Invoke(cutscene);

        // try/finally around a coroutine: we can't yield inside a try-with-finally that
        // catches, but we can wrap the iteration manually so restore always runs.
        IEnumerator inner = cutscene.Play(ctx);
        while (true)
        {
            object current;
            try
            {
                if (!inner.MoveNext()) break;
                current = inner.Current;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                break;
            }
            yield return current;
        }

        player.ExitCutsceneMode();
        LetterboxOverlay.Instance.HideBarsAsync(0.4f);
        IsPlaying = false;
        OnCutsceneEnded?.Invoke(cutscene);
    }
}
