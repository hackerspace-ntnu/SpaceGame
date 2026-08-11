using System;
using System.Collections;
using UnityEngine;

// Local-per-client cutscene playback. Locks the subject (player or AI agent), runs a
// Cutscene's coroutine, restores on end (even if Play throws). One cutscene at a time;
// concurrent Play() rejects.
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

    /// <summary>Play a cutscene with the local player as the subject (legacy convenience).</summary>
    public bool Play(Cutscene cutscene) => Play(cutscene, subject: null);

    /// <summary>
    /// Play a cutscene with an explicit subject. The subject is whichever entity the
    /// cutscene is "about" — usually the player walking through a door, but it could be
    /// an AI agent in scripted sequences. If null, falls back to the local PlayerController.
    /// </summary>
    public bool Play(Cutscene cutscene, GameObject subject)
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

        StartCoroutine(RunCutscene(cutscene, subject));
        return true;
    }

    private IEnumerator RunCutscene(Cutscene cutscene, GameObject subject)
    {
        IsPlaying = true;

        PlayerController player = ResolvePlayer(subject);
        if (player == null)
        {
            Debug.LogError("[CutsceneDirector] No PlayerController for cutscene subject; aborting cutscene.");
            IsPlaying = false;
            yield break;
        }

        Camera cam = player.PlayerCamera;
        var ctx = new CutsceneContext(player, cam, subject != null ? subject : player.gameObject);

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

    private static PlayerController ResolvePlayer(GameObject subject)
    {
        if (subject != null)
        {
            var p = subject.GetComponentInParent<PlayerController>();
            if (p != null) return p;
        }
        return FindFirstObjectByType<PlayerController>();
    }
}
