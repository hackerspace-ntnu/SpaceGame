using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.World
{
    /// <summary>
    /// Climb onto the ranch's tame animal and try its controls — the same struck-through checklist as the crash site's
    /// first lesson (<see cref="LearnControlsStep"/>, <c>GDC-L1-UX-0001</c>), counted only from the saddle. There is
    /// one animal, so it is met as soon as ANY player has ridden through the list; the lessons themselves (keys and
    /// words) are set on the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Ranch/Ride Tame Step")]
    public class RideTameStep : LearnControlsStep
    {
        [Tooltip("The ranch whose tame animal is ridden.")]
        [SerializeField] private RanchSite site;

        [Tooltip("Shown above the list while this machine's player is not in its saddle.")]
        [SerializeField] private string climbOn = "<b>RMB</b>  climb into the saddle";

        public override bool IsMet(ObjectiveWorld world) => world.AnyoneFinished;

        public override string Status(ObjectiveWorld world) =>
            RidingTame() ? base.Status(world) : climbOn + "\n\n" + base.Status(world);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            Ranch ranch = Ranch.Of(site);
            position = ranch != null && ranch.Tame != null ? ranch.Tame.transform.position : site != null ? site.Yard : default;
            return ranch != null || site != null;
        }

        protected override bool CanPractise(PlayerController player) => OutOfCutscene && RidingTame();

        /// <summary>EVERY machine: this machine's player sits on the ranch's tame animal.</summary>
        private bool RidingTame()
        {
            Ranch ranch = Ranch.Of(site);
            MountModule mount = MountModule.LocalRiderMount;
            return ranch != null && ranch.Tame != null && mount != null && mount.gameObject == ranch.Tame.gameObject;
        }
    }
}
