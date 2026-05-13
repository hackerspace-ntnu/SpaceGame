using UnityEngine;

/// <summary>
/// Compatibility shim. Volume trigger that fires a single Cutscene on the player.
/// For new content, prefer <see cref="CutsceneAction"/> + <see cref="VolumeTrigger"/>.
/// </summary>
[System.Obsolete("Use CutsceneAction + VolumeTrigger on the same GameObject.")]
[RequireComponent(typeof(Collider))]
public class CutsceneTriggerVolume : MonoBehaviour
{
    [SerializeField] private Cutscene cutscene;
    [SerializeField] private bool playOnce = true;

    private bool fired;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (playOnce && fired) return;
        if (cutscene == null) return;
        if (CutsceneDirector.Instance == null) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        if (CutsceneDirector.Instance.Play(cutscene, player.gameObject))
            fired = true;
    }
}
