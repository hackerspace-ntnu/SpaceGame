// A ladder a player can climb: where it stands, where the climb ends, and the volume in front of it
// that counts as being at it.
//
// The volume is a box of numbers, not a trigger collider. A trigger would be found by every ray in
// the game that does not pass QueryTriggerInteraction.Ignore -- the crosshair, placement aims, the
// camera -- and a ladder in front of a door would eat the click on the door. The only thing that
// needs the volume is LadderClimber, which asks Ladder.At directly.
//
// Where the climber stands is derived from the exit, not from the transform's axes: the exit is the
// floor at the top, behind the rungs, so the climber is on the other side. The markers come out of
// Blender's FBX axis conversion rotated and scaled x100, and a direction read off the geometry that
// was route-checked is one the importer cannot turn round.
//
// Static scene geometry: no network state, nothing to save. Each machine's own player asks its own
// copy of the same ladders.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    [DisallowMultipleComponent]
    public class Ladder : MonoBehaviour
    {
        [Tooltip("Where a climber's feet step off, at the top. Its height is the end of the climb.")]
        [SerializeField] private Transform top;

        [Tooltip("The floor a climber steps onto at the top, behind the rungs.")]
        [SerializeField] private Transform exit;

        [Header("Volume, in world metres")]
        [Tooltip("Half the volume's width across the rungs. A little wider than the ladder, so a " +
                 "player who walks at it slightly off-centre still takes hold.")]
        [SerializeField, Min(0.1f)] private float halfWidth = 0.6f;

        [Tooltip("How far in front of the rungs a player's feet can be and still take hold.")]
        [SerializeField, Min(0.1f)] private float reach = 1.2f;

        [Tooltip("How far behind the rung line the volume extends, so a climber pressed against the " +
                 "rungs is still inside it.")]
        [SerializeField, Min(0f)] private float behind = 0.2f;

        [Tooltip("How far below the foot the volume starts, for a deck that is not quite level.")]
        [SerializeField, Min(0f)] private float footMargin = 0.3f;

        [Tooltip("Half the height of the band at the top where a player on the exit floor, or dropping " +
                 "into the gap the rails leave, takes hold to climb down.")]
        [SerializeField, Min(0f)] private float topBand = 0.4f;

        [Tooltip("How far back from the rungs, onto the exit floor, the top band reaches.")]
        [SerializeField, Min(0f)] private float topReach = 1.3f;

        private static readonly List<Ladder> ActiveLadders = new List<Ladder>();

        private static readonly Color GizmoColour = new Color(0.9f, 0.7f, 0.2f, 0.8f);
        private const float ExitGizmoRadius = 0.15f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveLadders.Clear();

        /// <summary>The ladder whose volume holds <paramref name="feet"/>, or null.</summary>
        public static Ladder At(Vector3 feet)
        {
            foreach (Ladder ladder in ActiveLadders)
                if (ladder.Contains(feet))
                    return ladder;
            return null;
        }

        /// <summary>The ladder whose top band holds <paramref name="feet"/>, or null.</summary>
        public static Ladder AtTop(Vector3 feet)
        {
            foreach (Ladder ladder in ActiveLadders)
                if (ladder.TopContains(feet))
                    return ladder;
            return null;
        }

        /// <summary>The foot of the ladder, on the deck, on the rung line.</summary>
        public Vector3 Foot => transform.position;

        /// <summary>World height at which a climber's feet reach the top.</summary>
        public float TopHeight => top.position.y;

        /// <summary>Where a climber's feet are put when they step off at the top.</summary>
        public Vector3 ExitPoint => exit.position;

        /// <summary>Unit horizontal direction from the rungs toward the side a climber stands on.</summary>
        public Vector3 TowardClimber
        {
            get
            {
                Vector3 away = Foot - exit.position;
                away.y = 0f;
                return away.normalized;
            }
        }

        public bool IsValid => top != null && exit != null;

        /// <summary>Whether a player's feet at <paramref name="feet"/> are at this ladder.</summary>
        public bool Contains(Vector3 feet)
        {
            if (!IsValid) return false;

            Vector3 toward = TowardClimber;
            Vector3 across = Vector3.Cross(Vector3.up, toward);
            Vector3 relative = feet - Foot;

            float along = Vector3.Dot(relative, toward);
            return along >= -behind && along <= reach
                && Mathf.Abs(Vector3.Dot(relative, across)) <= halfWidth
                && feet.y >= Foot.y - footMargin && feet.y <= TopHeight;
        }

        /// <summary>
        /// Whether feet at <paramref name="feet"/> are at the top of this ladder: level with the step-off
        /// height, from the exit floor behind the rungs out to the climber's side of the gap.
        /// </summary>
        public bool TopContains(Vector3 feet)
        {
            if (!IsValid) return false;

            Vector3 toward = TowardClimber;
            Vector3 across = Vector3.Cross(Vector3.up, toward);
            Vector3 relative = feet - Foot;

            float along = Vector3.Dot(relative, toward);
            return along >= -topReach && along <= reach
                && Mathf.Abs(Vector3.Dot(relative, across)) <= halfWidth
                && Mathf.Abs(feet.y - TopHeight) <= topBand;
        }

        /// <summary>Wires the ladder. For builders; a ladder authored in a scene sets these in the Inspector.</summary>
        public void Configure(Transform topMarker, Transform exitMarker)
        {
            top = topMarker;
            exit = exitMarker;
        }

        private void OnEnable()
        {
            if (!IsValid)
            {
                Debug.LogWarning($"[Ladder] {name} has no top or exit; nobody can climb it.", this);
                return;
            }

            if (Mathf.Approximately(new Vector2(Foot.x - exit.position.x, Foot.z - exit.position.z).sqrMagnitude, 0f))
                Debug.LogWarning($"[Ladder] {name}'s exit is directly above its foot, so it has no side " +
                                 "to climb from.", this);

            ActiveLadders.Add(this);
        }

        private void OnDisable() => ActiveLadders.Remove(this);

        private void OnDrawGizmosSelected()
        {
            if (!IsValid) return;

            Vector3 toward = TowardClimber;
            float height = TopHeight - Foot.y + footMargin;
            Vector3 centre = Foot + toward * ((reach - behind) * 0.5f) + Vector3.up * (height * 0.5f - footMargin);

            Gizmos.color = GizmoColour;
            Gizmos.matrix = Matrix4x4.TRS(centre, Quaternion.LookRotation(toward, Vector3.up), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfWidth * 2f, height, reach + behind));
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawLine(new Vector3(Foot.x, TopHeight, Foot.z), ExitPoint);
            Gizmos.DrawWireSphere(ExitPoint, ExitGizmoRadius);
        }
    }
}
