using UnityEngine;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// A bolt out of a clear sky over a far-off place, the moment the quest reaches a step — over
    /// the hamlet gate as the witch dies, which is Merob waking up. Seen from wherever the crew are
    /// standing, so the next goal is shown before it is told (<c>GDC-L1-LEVEL-0006</c>).
    ///
    /// <para>
    /// Pure presentation on every machine, started by the step change itself (only a live one — a
    /// reload or a late join does not replay it). Where it strikes comes from the site table, so the
    /// hamlet's chunk need not be loaded for the bolt to land on it.
    /// </para>
    /// </summary>
    public class QuestLightningOmen : MonoBehaviour
    {
        [Tooltip("The chain to watch.")]
        [SerializeField] private ObjectiveChain chain;

        [Tooltip("Strike when the chain reaches this step.")]
        [SerializeField] private string onStep;

        [Tooltip("The QuestSite id of where it strikes.")]
        [SerializeField] private string siteId;

        [Tooltip("The bolt, as LightningStrike.Present draws it.")]
        [SerializeField] private GameObject boltPrefab;

        [Tooltip("How high above the site the bolt starts, metres.")]
        [SerializeField, Min(1f)] private float drawHeight = 120f;

        [Tooltip("Seconds before the bolt is cleaned up.")]
        [SerializeField, Min(0.1f)] private float boltLifetime = 3f;

        private ObjectiveDirector director;
        private StepArrival arrival;

        private void OnEnable()
        {
            director = ObjectiveDirector.Instance;
            if (director != null)
            {
                director.Started += OnChanged;
                director.Changed += OnChanged;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.Started -= OnChanged;
                director.Changed -= OnChanged;
            }
        }

        private void OnChanged(Quest quest, bool live)
        {
            if (!arrival.Notice(quest, live, chain, onStep)) return;
            if (!QuestSite.TryGet(siteId, out Vector3 site, out _)) return;

            LightningStrike.Present(boltPrefab, site + Vector3.up * drawHeight, site, boltLifetime);
        }
    }
}
