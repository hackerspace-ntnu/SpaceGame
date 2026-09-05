using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Tying the catch off: handing a roped creature from the lasso to a <see cref="Leash"/>.
    ///
    /// <para>
    /// <b>The verb the item was missing.</b> Catching something used to end in dragging it, and
    /// nothing else — nothing outside <c>Items/Artifacts/</c> so much as referenced
    /// <see cref="LassoTether"/>, so the whole of the lasso's consequence was that the player was
    /// now holding an animal and could let go. A choice whose outcome the player cannot perceive is
    /// not one they made (<c>GDC-L1-DESIGN-0006</c>). The catch needed somewhere to land.
    /// </para>
    /// <para>
    /// <b>A leash rather than anything new.</b> [LeashSystem](LeashSystem.md) already models
    /// exactly this — a rope between any two things, resolved per end by ownership, saved by
    /// <c>LeashSaveable</c> off <see cref="Leash.All"/>, broken by its own strain rules. So the
    /// lasso becomes the verb that <i>catches</i> and the leash the one that <i>keeps</i>, the
    /// creature stays tied across a save and a quit for free, and this file is thirty lines instead
    /// of a second rope subsystem that would have had to learn all the same lessons again.
    /// </para>
    /// <para>
    /// The rope's tuning and — the part nothing else can supply — its material come from the leash
    /// item's own prefab via <see cref="LeashArtifact.TryResolveSettings"/>, which is the seam that
    /// already existed for the save system to rebuild ropes with. The lasso owns no leash assets
    /// and needs none.
    /// </para>
    ///
    /// <para>
    /// <b>Every machine builds its own.</b> Like every other rope in this project, a leash is a
    /// local <c>GameObject</c> rather than a networked one, and the two machines that own its two
    /// ends each resolve the end they own. So this is called from the lasso's <c>Present</c> path,
    /// on all of them, from one announced hitch.
    /// </para>
    /// </summary>
    public static class LassoHitch
    {
        /// <summary>
        /// Slack left in the hitched rope, in metres. Tied off exactly taut, the creature is
        /// standing at the end of its rope from the first frame and reads as pinned rather than
        /// tethered.
        /// </summary>
        private const float Slack = 1.5f;

        /// <summary>Shortest hitch worth making. Under this the creature is stood on the anchor.</summary>
        private const float MinLength = 2f;

        /// <summary>
        /// Tie <paramref name="creature"/> to the anchor and hand it over. Null when there is
        /// nothing to tie or nothing to tie it to.
        /// </summary>
        /// <param name="anchorRoot">
        /// What the far end is tied to, or null for bare geometry — a rock, the sand — which has no
        /// local space worth naming and whose world point is the same on every machine anyway.
        /// </param>
        /// <param name="anchorPoint">
        /// The knot: an offset in <paramref name="anchorRoot"/>'s own space when there is one, a
        /// world point when there is not. The same encoding <c>LeashArtifact.Present</c> uses, and
        /// for the same reason — a world point re-projected per machine names a different part of
        /// anything that moves.
        /// </param>
        public static Leash TieOff(GameObject creature, GameObject anchorRoot, Vector3 anchorPoint,
                                   float creatureAttachHeight)
        {
            if (creature == null) return null;

            Vector3 anchorWorld = anchorRoot != null
                ? anchorRoot.transform.TransformPoint(anchorPoint)
                : anchorPoint;

            Vector3 knotWorld = creature.transform.position + Vector3.up * creatureAttachHeight;

            LeashArtifact.TryResolveSettings(out Leash.Settings settings);
            settings.length = Mathf.Max(Vector3.Distance(knotWorld, anchorWorld) + Slack, MinLength);

            Leash leash = Leash.Create(settings);

            // The creature end in the creature's own space, at the height the lasso had it. The rope
            // that replaces the lasso should start where the lasso finished, or the knot visibly
            // jumps down the animal at the moment of the hand-off.
            leash.TieEndTo(true, creature, Vector3.up * creatureAttachHeight);

            if (anchorRoot != null) leash.TieEndTo(false, anchorRoot, anchorPoint);
            else leash.PinEndTo(false, anchorPoint);

            return leash;
        }

        /// <summary>
        /// Is this a thing a rope can be tied to?
        ///
        /// <para>
        /// Owner-side only, on the machine holding the camera. A hitch needs something that will
        /// still be there in a minute, so a rope is refused on anything the player is themselves
        /// carrying or riding, and — the case that matters — on the animal already on the other end
        /// of the rope, which would be a loop that constrains nothing.
        /// </para>
        /// </summary>
        public static bool IsHitchable(Collider surface, Transform holder, Transform caught)
        {
            if (surface == null || holder == null) return false;
            if (surface.transform.IsChildOf(holder)) return false;
            if (caught != null && surface.transform.IsChildOf(caught)) return false;

            // A creature is not a hitching post. Tying one animal to another is a rope between two
            // things that both walk off, which the leash can do and the lasso should not be the way
            // to ask for — the gesture here is "tie it up", and it needs an end that stays put.
            return surface.GetComponentInParent<SpaceGame.Agents.AgentController>() == null;
        }

        /// <summary>
        /// The knot, encoded for the wire: a local offset when the anchor has a networked identity,
        /// a world point when it does not.
        /// </summary>
        public static Vector3 EncodeKnot(GameObject anchorRoot, Vector3 worldPoint) =>
            anchorRoot != null ? anchorRoot.transform.InverseTransformPoint(worldPoint) : worldPoint;
    }
}
