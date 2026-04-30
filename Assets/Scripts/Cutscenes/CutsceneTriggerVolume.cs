using UnityEngine;

// Drop on a GameObject with a trigger Collider. Fires the assigned cutscene once when
// the player walks in. Cutscene is a child component so it travels with the prefab.
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
        if (other.GetComponentInParent<PlayerController>() == null) return;

        if (CutsceneDirector.Instance.Play(cutscene))
            fired = true;
    }
}
