using UnityEngine;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.World
{
    /// <summary>
    /// Go with the wrangler to where this step's beat on the <see cref="Ranch"/> sends him — the yard, out onto the
    /// range. Met once he stands there and somebody of the crew is beside him. The guidance is the man himself
    /// (<c>GDC-L1-LEVEL-0001</c>): the waypoint is pinned on him, never on a point he has not reached yet, and while he
    /// leads he waits for stragglers.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Ranch/Follow Wrangler Step")]
    public class FollowWranglerStep : RanchStep
    {
        [Tooltip("How close to him somebody must be once he is there, metres.")]
        [SerializeField, Min(1f)] private float joinRadius = 12f;

        [Tooltip("Under the title while he walks; {0} is his name.")]
        [SerializeField] private string following = "Keep up with <b>{0}</b>";

        public override bool IsMet(ObjectiveWorld world)
        {
            Ranch ranch = LoadedRanch;
            return ranch != null && ranch.GuideStandsFor(this) &&
                   TryGetWrangler(out Vector3 at) && world.NearestCrewDistance(at) <= joinRadius;
        }

        public override string Status(ObjectiveWorld world) =>
            string.Format(following, site != null ? site.WranglerName : string.Empty);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) => TryGetWrangler(out position);
    }
}
