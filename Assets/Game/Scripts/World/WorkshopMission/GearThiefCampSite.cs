using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Where the gear thieves' camp stands, for a mission that is a long way from it. The camp itself (a
    /// <see cref="GearThiefCamp"/>) is a scene object in a chunk that is not loaded while the player is at the workshop, and
    /// the trail to it must be laid before then; this asset is in memory on every machine from the start. Written by
    /// <c>GearThiefCampPlacer</c> when it places the camp; read by <see cref="WorkshopMission"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Workshop/Gear Thief Camp Site")]
    public sealed class GearThiefCampSite : ScriptableObject
    {
        [Tooltip("The camp's position in the world. Written by Place Gear Thief Camp; do not edit by hand.")]
        [SerializeField] private Vector3 position;

        [Tooltip("The chunk scene the camp stands in, so placing it somewhere else can take it away from there.")]
        [SerializeField] private string chunkScenePath;

        public Vector3 Position => position;
        public string ChunkScenePath => chunkScenePath;
        public bool IsPlaced => !string.IsNullOrEmpty(chunkScenePath);

        /// <summary>Editor: the camp now stands at <paramref name="at"/> in <paramref name="scenePath"/>.</summary>
        public void Record(Vector3 at, string scenePath)
        {
            position = at;
            chunkScenePath = scenePath;
        }
    }
}
