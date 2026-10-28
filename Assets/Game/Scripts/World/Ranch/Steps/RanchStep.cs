using UnityEngine;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.World
{
    /// <summary>
    /// A step of a ranch's quest: it is about one <see cref="RanchSite"/>, and as it begins it may have the ranch hand
    /// the crew something (<see cref="RanchCue"/>). Waits to begin until the ranch is loaded on the server — the steps
    /// are walked through there with the wrangler, so the ranch's chunk is where the crew are.
    /// </summary>
    public abstract class RanchStep : ObjectiveStep
    {
        [Tooltip("The ranch this step is about.")]
        [SerializeField] protected RanchSite site;

        [Tooltip("What the ranch hands the crew as this step begins. Laid once: the step's saved Begun flag keeps a " +
                 "reload from laying it again.")]
        [SerializeField] private RanchCue onBegin;

        /// <summary>The ranch, while it is loaded on this machine; else null.</summary>
        protected Ranch LoadedRanch => Ranch.Of(site);

        public override bool TryBegin(ObjectiveWorld world)
        {
            Ranch ranch = LoadedRanch;
            if (ranch == null) return false;

            ranch.Lay(onBegin, world);
            return true;
        }

        /// <summary>EVERY machine: the wrangler's position, else where the ranch's yard was recorded.</summary>
        protected bool TryGetWrangler(out Vector3 position)
        {
            RanchWrangler wrangler = RanchWrangler.Of(site);
            position = wrangler != null ? wrangler.transform.position : site != null ? site.Yard : default;
            return wrangler != null || site != null;
        }
    }
}
