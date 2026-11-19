using UnityEngine;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// One team's side of the arena: where its players stand at the start of every round.
    ///
    /// A team tag on an ordinary <see cref="SpawnPoint"/>, so the scatter, the ground probe and the
    /// clearance test are the spawn point's own — and so <c>SpawnManager</c> still sees a spawn
    /// point in the scene, which <c>NetworkGameManager</c> waits for before spawning anyone.
    /// </summary>
    [RequireComponent(typeof(SpawnPoint))]
    public class ArenaTeamSpawn : MonoBehaviour
    {
        [Tooltip("Which team starts here: 0 or 1.")]
        [SerializeField, Range(0, ArenaRules.TeamCount - 1)] private int team;

        private SpawnPoint point;

        public int Team => team;

        public SpawnPoint Point => point != null ? point : point = GetComponent<SpawnPoint>();
    }
}
