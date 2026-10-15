using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Somebody in the crew reaches a named place — "find the witch's hut", "return to the hamlet".
    ///
    /// <para>
    /// <b>The waypoint can wait.</b> A place the crew are meant to find for themselves is found
    /// through the world — a trail, a plume of smoke — not a marker (<c>GDC-L1-LEVEL-0001</c>). But a
    /// crew who are truly lost should not stay lost (<c>GDC-L1-LEVEL-0005</c>), so the marker appears
    /// after <see cref="revealWaypointAfterSeconds"/>. The clock is this machine's and restarts on a
    /// reload.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Reach Site Step")]
    public class ReachSiteStep : ObjectiveStep
    {
        [Tooltip("The QuestSite id of the place to reach.")]
        [SerializeField] private string siteId;

        [Tooltip("Seconds into the step before the waypoint appears. 0 shows it at once.")]
        [SerializeField, Min(0f)] private float revealWaypointAfterSeconds;

        public override bool IsMet(ObjectiveWorld world)
        {
            if (!QuestSite.TryGet(siteId, out Vector3 site, out float radius)) return false;

            return world.NearestCrewDistance(site) <= radius;
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return world.StepSeconds() >= revealWaypointAfterSeconds && QuestSite.TryGet(siteId, out position, out _);
        }
    }
}
