// A hatch in a hull, used the way it reads: interact with it, the lid swings open, the player
// crawls through on their own, and the lid shuts behind them.
//
// The lid is an ordinary ArticulatedPart behind an ArticulatedPartInteraction, so opening and closing
// it replicate and save exactly like every door in the game; this component only asks that switch to
// move and then hands the interactor's body to their HatchCrawler with a three-point route — the
// fender outside, the sill, the floor inside (or the reverse). The crawl itself is owner-side, so
// nothing new goes on the wire.
//
// The lid is shut in the model's rest pose; the hatch is closed until someone uses it.
using System.Collections;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public class HatchPassage : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [Tooltip("The switch that drives the lid. Opening and closing go through it so they replicate.")]
        [SerializeField] private ArticulatedPartInteraction lidSwitch;
        [SerializeField] private ArticulatedPart lid;

        [Header("Crawl route (floor points)")]
        [SerializeField] private Transform outer;
        [SerializeField] private Transform sill;
        [SerializeField] private Transform inner;

        [Tooltip("The hull's colliders. The crawl passes through the sill and coaming, so the crawler " +
                 "ignores these for its duration.")]
        [SerializeField] private Collider[] hull;

        [SerializeField] private string label = "Hatch";

        [Tooltip("How far open the lid must be before the crawl starts, 0..1.")]
        [SerializeField, Range(0f, 1f)] private float openEnough = 0.8f;

        [Tooltip("Start the crawl after this long even if the lid has not opened (it is blocked).")]
        [SerializeField, Min(0f)] private float lidWaitSeconds = 2.5f;

        public string Label => label;
        public string Prompt => Camera.main != null && IsOutside(Camera.main.transform.position, outer.position, inner.position)
            ? "Climb in" : "Climb out";
        public float? Value01 => null;
        public string ValueText => string.Empty;

        public bool CanInteract() => lidSwitch != null && lid != null && outer != null && sill != null && inner != null;

        public void Interact(Interactor interactor)
        {
            if (!CanInteract() || interactor == null) return;
            HatchCrawler crawler = interactor.GetComponentInParent<HatchCrawler>();
            if (crawler == null || crawler.IsCrawling) return;

            bool fromOutside = IsOutside(crawler.transform.position, outer.position, inner.position);
            Vector3[] route = Route(fromOutside, outer.position, sill.position, inner.position);
            if (!lid.IsOpen) lidSwitch.Interact(interactor);
            StartCoroutine(CrawlWhenOpen(crawler, route, interactor));
        }

        private IEnumerator CrawlWhenOpen(HatchCrawler crawler, Vector3[] route, Interactor interactor)
        {
            float until = Time.time + lidWaitSeconds;
            while (lid.Openness < openEnough && Time.time < until) yield return null;
            crawler.TryCrawl(route, hull, () =>
            {
                if (lid.IsOpen) lidSwitch.Interact(interactor);
            });
        }

        /// <summary>Which side of the hatch a point is on: the nearer of the two floor marks.</summary>
        public static bool IsOutside(Vector3 point, Vector3 outerMark, Vector3 innerMark) =>
            (point - outerMark).sqrMagnitude < (point - innerMark).sqrMagnitude;

        /// <summary>The crawl, from the side the player is on to the other: start mark, sill, far mark.</summary>
        public static Vector3[] Route(bool fromOutside, Vector3 outerMark, Vector3 sillMark, Vector3 innerMark) =>
            fromOutside ? new[] { outerMark, sillMark, innerMark } : new[] { innerMark, sillMark, outerMark };

        /// <summary>Wiring for the builder, which owns this prefab.</summary>
        public void Configure(ArticulatedPartInteraction lidSwitchIn, ArticulatedPart lidIn,
                              Transform outerIn, Transform sillIn, Transform innerIn, Collider[] hullIn)
        {
            lidSwitch = lidSwitchIn;
            lid = lidIn;
            outer = outerIn;
            sill = sillIn;
            inner = innerIn;
            hull = hullIn;
        }
    }
}
