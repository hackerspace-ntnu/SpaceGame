using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The crash threw the oxygen plant out of the back door: carry it home, solder its cracked seams and get it running.
    /// Met when the plant is back in its mount, whole and powered — the cabin has air.
    ///
    /// <para>
    /// Stateless, like every step: the plant's place and its cracks are <see cref="OxygenPlantMount"/>'s and its
    /// <see cref="TorchRepairable"/>'s, the loose plant is a <see cref="Liftable"/>, and all of them replicate on their own. In a
    /// world whose plant was never thrown out — every world saved before this step existed, a disposable session that skipped
    /// the crash — the plant is already in its mount and whole, and the step asks only for power.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Recover Oxygen Plant Step")]
    public class RecoverOxygenPlantStep : ObjectiveStep
    {
        [Tooltip("Under the title while the plant lies outside. The visor shows the range itself.")]
        [SerializeField] private string outsideStatus = "Thrown out of the back door. Lift it by the handle";
        [SerializeField] private string carryingStatus = "Carrying:  <b>{0:0} m</b> to its mount";
        [Tooltip("{0} = seams soldered, {1} = seams in all.")]
        [SerializeField] private string damagedStatus = "Damaged: solder the seams  <b>{0}/{1}</b>";
        [SerializeField] private string powerStatus = "Fit a power cell";

        [Tooltip("How high above a target the beacon stands, in metres.")]
        [SerializeField, Min(0f)] private float beaconLift = 1.5f;

        public override bool IsMet(ObjectiveWorld world)
        {
            OxygenPlantMount mount = MountOf(world);
            return mount != null && mount.Running;
        }

        public override string Status(ObjectiveWorld world)
        {
            OxygenPlantMount mount = MountOf(world);
            if (mount == null) return string.Empty;

            if (mount.Damaged)
            {
                TorchRepairable cracks = mount.Damage;
                return string.Format(damagedStatus, Mathf.Max(0, cracks.WorkingSeam), cracks.SeamCount);
            }

            if (!mount.Detached) return powerStatus;

            Liftable plant = LoosePlant(mount);
            if (plant == null) return string.Empty;

            return plant.IsCarried
                ? string.Format(carryingStatus, Flat(plant.transform.position - mount.Point))
                : outsideStatus;
        }

        /// <summary>The loose plant until someone carries it; then where it goes; then the seam being soldered.</summary>
        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            OxygenPlantMount mount = MountOf(world);
            if (mount == null) return false;

            if (mount.Damaged && mount.Damage.TryGetWorkingSeam(out position)) return true;

            Liftable plant = mount.Detached ? LoosePlant(mount) : null;
            position = plant != null && !plant.IsCarried ? plant.transform.position : mount.Point;
            return true;
        }

        /// <summary>The briefing looks at the door that burst open, where the plant went out.</summary>
        public override bool TryGetFocus(ObjectiveWorld world, out Vector3 position)
        {
            OxygenPlantMount mount = MountOf(world);
            position = mount != null ? mount.DoorPoint : default;
            return mount != null;
        }

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            bool found = TryGetWaypoint(world, out position);
            position += Vector3.up * beaconLift;
            return found;
        }

        private static OxygenPlantMount MountOf(ObjectiveWorld world)
        {
            if (world.Ship == null) return null;
            return world.Ship.GetComponentInChildren<OxygenPlantMount>(true);
        }

        private static Liftable LoosePlant(OxygenPlantMount mount)
        {
            foreach (Liftable load in Liftable.Active)
                if (load != null && mount.Accepts(load)) return load;

            return null;
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
