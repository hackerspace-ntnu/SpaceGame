using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Makes a piece of the world follow a quest: the stash lid that only opens once the oath is
    /// reforged, the keepers' escort behaviour that only runs while they are being led.
    ///
    /// <para>
    /// <b>Derived, never saved.</b> It reads the chain's progress — which is saved and replicated —
    /// and sets its targets from that, on every machine, whenever the progress changes and when it
    /// wakes. So a reloaded world, a late joiner and a chunk streaming back in all come out right with
    /// nothing of their own on the wire or in the save.
    /// </para>
    /// <para>
    /// For plain objects and behaviours only. Never point it at a GameObject holding a spawned
    /// <c>NetworkObject</c>: deactivating one on a client desynchronises it from the server.
    /// </para>
    /// </summary>
    public class QuestStateGate : MonoBehaviour
    {
        [SerializeField] private QuestStepWindow window;

        [Tooltip("Active only while the chain is inside the window.")]
        [SerializeField] private GameObject[] activeInside = System.Array.Empty<GameObject>();

        [Tooltip("Active only while the chain is outside the window.")]
        [SerializeField] private GameObject[] activeOutside = System.Array.Empty<GameObject>();

        [Tooltip("Enabled only while the chain is inside the window.")]
        [SerializeField] private Behaviour[] enabledInside = System.Array.Empty<Behaviour>();

        private ObjectiveDirector director;
        private bool? applied;

        private void OnEnable()
        {
            director = ObjectiveDirector.Instance;
            if (director != null) director.Changed += OnChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (director != null) director.Changed -= OnChanged;
            applied = null;
        }

        private void OnChanged(ObjectiveRun run, bool live)
        {
            if (run.Chain == window.chain) Apply();
        }

        private void Apply()
        {
            // Outside a story world there is no quest to follow: leave the targets as authored.
            ObjectiveRun run = window.FindRun();
            if (run == null) return;

            bool inside = window.Contains(run);
            if (applied == inside) return;
            applied = inside;

            foreach (GameObject target in activeInside)
                if (target != null) target.SetActive(inside);
            foreach (GameObject target in activeOutside)
                if (target != null) target.SetActive(!inside);
            foreach (Behaviour target in enabledInside)
                if (target != null) target.enabled = inside;
        }
    }
}
