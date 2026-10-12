// Where the raiders who robbed the workshop hole up: an outpost placed in a chunk scene (a scavenger camp), marked with
// this. The workshop's mission finds the nearest loaded one, puts the thieves on the outpost's own spots and the stolen
// gear on one of its prop rests. Placed by Tools/SpaceGame/Workshop/Place Gear Thief Camp; holds no state.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class GearThiefCamp : MonoBehaviour
    {
        private static readonly List<GearThiefCamp> Live = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>The loaded camp nearest <paramref name="position"/> within <paramref name="maxDistance"/>, or null.</summary>
        public static GearThiefCamp Nearest(Vector3 position, float maxDistance)
        {
            GearThiefCamp nearest = null;
            float best = maxDistance * maxDistance;
            foreach (GearThiefCamp camp in Live)
            {
                float distance = (camp.transform.position - position).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                nearest = camp;
            }
            return nearest;
        }

        /// <summary>Where the gear lies: the camp's prop rest nearest its middle, or its middle when it has none.</summary>
        public Pose GearRest
        {
            get
            {
                Transform best = null;
                float nearest = float.MaxValue;
                foreach (PropRest rest in GetComponentsInChildren<PropRest>())
                {
                    float distance = (rest.transform.position - transform.position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    best = rest.transform;
                }
                return best != null ? new Pose(best.position, best.rotation) : new Pose(transform.position, transform.rotation);
            }
        }

        /// <summary>Where the thieves stand: the outpost's spots, nearest the gear first, so the first is the carrier's.</summary>
        public List<Pose> Posts()
        {
            Vector3 gear = GearRest.position;
            var posts = new List<Pose>();
            foreach (SettlementSpot spot in GetComponentsInChildren<SettlementSpot>())
                posts.Add(new Pose(spot.transform.position, spot.transform.rotation));
            posts.Sort((a, b) => (a.position - gear).sqrMagnitude.CompareTo((b.position - gear).sqrMagnitude));
            return posts;
        }
    }
}
