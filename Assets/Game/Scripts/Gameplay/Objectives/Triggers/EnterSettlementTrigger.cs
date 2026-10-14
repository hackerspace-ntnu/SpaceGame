using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Begins the quest the first time any member of the crew walks into a settlement. The settlement is named as
    /// the world's baked town list has it (<see cref="WorldSiteCatalog.towns"/>: its GameObject's name), so it is
    /// known while its chunk is still unloaded and nothing has to be added to the settlement's scene.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Triggers/Enter Settlement")]
    public class EnterSettlementTrigger : QuestTrigger
    {
        [Tooltip("The settlement's name in the baked town list: its GameObject's name, e.g. ClankerSettlement.")]
        [SerializeField] private string settlement;

        [Tooltip("How far outside the settlement's buildings still counts as in it, metres.")]
        [SerializeField, Min(0f)] private float margin = 10f;

        [Tooltip("The settlement's reach when the town list knows none (radius 0), metres.")]
        [SerializeField, Min(1f)] private float fallbackRadius = 60f;

        public override bool ShouldStart(ObjectiveDirector director)
        {
            ObjectiveWorld world = director.Lookups;
            return world.TryFindTown(settlement, out WorldSiteCatalog.TownEntry town) &&
                   world.NearestCrewDistance(town.position) <= ObjectiveWorld.TownReach(town, margin, fallbackRadius);
        }
    }
}
