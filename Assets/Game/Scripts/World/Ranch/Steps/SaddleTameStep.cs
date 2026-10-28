using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;

namespace SpaceGame.World
{
    /// <summary>
    /// Fit a saddle to the ranch's tame animal. Pair it with <see cref="RanchCue.Saddle"/> so the ranch lays one out as
    /// the step begins. Points at the saddle while it lies loose in the yard, then at the animal.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Ranch/Saddle Tame Step")]
    public class SaddleTameStep : RanchStep
    {
        [Tooltip("How far from the yard a loose saddle is looked for, metres.")]
        [SerializeField, Min(1f)] private float searchRadius = 60f;

        [Tooltip("Under the title while nobody has the saddle in hand.")]
        [SerializeField] private string pickUp = "Pick up the saddle";

        [Tooltip("Under the title once somebody carries it.")]
        [SerializeField] private string fit = "Hold it, look at the esko's back and <b>LMB</b>";

        public override bool IsMet(ObjectiveWorld world)
        {
            SaddleSocket socket = Socket;
            return socket != null && socket.IsSaddled;
        }

        public override string Status(ObjectiveWorld world)
        {
            Ranch ranch = LoadedRanch;
            return ranch != null && CrewGear.AnyoneCarries(world, ranch.SaddleItem) ? fit : pickUp;
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            Ranch ranch = LoadedRanch;
            if (ranch == null)
            {
                position = site != null ? site.Yard : default;
                return site != null;
            }

            if (world.TryFindLoose(ranch.SaddleItem, ranch.YardPosition, searchRadius, out position)) return true;

            position = ranch.Tame != null ? ranch.Tame.transform.position : ranch.YardPosition;
            return true;
        }

        private SaddleSocket Socket
        {
            get
            {
                Ranch ranch = LoadedRanch;
                return ranch != null && ranch.Tame != null ? ranch.Tame.GetComponent<SaddleSocket>() : null;
            }
        }
    }
}
