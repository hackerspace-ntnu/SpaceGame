using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Go to a settlement named in the world's baked town list (<see cref="WorldSiteCatalog.towns"/>). Met the
    /// moment any member of the crew is inside its reach. The generic form of <see cref="AnswerSignalStep"/>, whose
    /// settlement is chosen by the ship instead.
    ///
    /// <para>
    /// Explicit guidance, waypoint and beacon both: a settlement is hundreds of metres to kilometres across open
    /// desert, the open-world exception <c>GDC-L1-LEVEL-0001</c> carves out.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Reach Settlement Step")]
    public class ReachSettlementStep : ObjectiveStep
    {
        [Tooltip("The settlement's name in the baked town list: its GameObject's name, e.g. ClankerSettlement.")]
        [SerializeField] private string settlement;

        [Tooltip("How far outside the settlement's buildings still counts as arrived, metres.")]
        [SerializeField, Min(0f)] private float arrivalMargin = 15f;

        [Tooltip("The settlement's reach when the town list knows none (radius 0), metres.")]
        [SerializeField, Min(1f)] private float fallbackRadius = 60f;

        [Tooltip("The line under the title. Empty for none.")]
        [SerializeField] private string status;

        public override bool IsMet(ObjectiveWorld world) =>
            world.TryFindTown(settlement, out WorldSiteCatalog.TownEntry town) &&
            world.NearestCrewDistance(town.position) <= ObjectiveWorld.TownReach(town, arrivalMargin, fallbackRadius);

        public override string Status(ObjectiveWorld world) => status;

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            bool known = world.TryFindTown(settlement, out WorldSiteCatalog.TownEntry town);
            position = town.position;
            return known;
        }

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position) =>
            TryGetWaypoint(world, out position);
    }
}
