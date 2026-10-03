// A place on a building or decoration prefab where one resident can be: the anvil, a seat at the fire,
// the counter of a stall, a patch of shade. Put it on a child transform standing on the prefab's floor,
// +Z the way the resident faces. It travels with the prefab, so a generated settlement's places are
// simply every spot its buildings and decorations brought along — nothing is baked per settlement.
//
// One spot holds one resident. Spots that share a non-empty group under the same parent are one circle
// (the seats around a fire, a stall's counter and its customers): residents holding there talk to each
// other, and friends plan their free time into the same circle.
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementSpot : MonoBehaviour
    {
        // Gizmo proportions, in metres: a person-sized marker, so a spot reads at the scale it is used.
        private const float GizmoRadius = 0.3f;
        private const float GizmoHeight = 1.7f;
        private const float GizmoFacing = 0.8f;

        [Tooltip("What this spot is for. Decides who comes here, when, and what they do.")]
        [SerializeField] private SpotUse use;

        [Tooltip("What a resident here looks at — the anvil, the fire, the counter. Empty = the spot's own +Z.")]
        [SerializeField] private Transform face;

        [Tooltip("Spots with the same group under the same parent are one circle: they talk, and friends meet there. Empty = alone.")]
        [SerializeField] private string group;

        [Tooltip("The loop held here when this prop calls for another than its use's: the grill's spot grills, the pot's spot " +
                 "stirs, though both are the kitchen. Empty = the use's hold cue.")]
        [SerializeField] private CharacterCue stationCue;

        public SpotUse Use => use;
        public string Group => group;

        /// <summary>The loop a resident holds here: this prop's own station cue, else what the spot's use is for.</summary>
        public CharacterCue HoldCue => stationCue != null ? stationCue : use != null ? use.holdCue : null;

        /// <summary>The spot names the thing worked at (a face transform), as opposed to looking along its own +Z.</summary>
        public bool HasTarget => face != null;
        public Vector3 Position => transform.position;

        /// <summary>Where a resident here looks: the face target, else a point ahead along the spot's +Z.</summary>
        public Vector3 FacePoint => face ? face.position : transform.position + transform.forward;

        private void OnDrawGizmos()
        {
            Gizmos.color = ColourOf(use);
            Vector3 feet = transform.position;
            Gizmos.DrawWireSphere(feet + Vector3.up * GizmoRadius, GizmoRadius);
            Gizmos.DrawLine(feet, feet + Vector3.up * GizmoHeight);
            Vector3 eye = feet + Vector3.up * (GizmoHeight - GizmoRadius);
            Gizmos.DrawLine(eye, Vector3.MoveTowards(eye, FacePoint, GizmoFacing));
        }

        private static Color ColourOf(SpotUse spotUse)
        {
            if (spotUse == null) return Color.red;
            return spotUse.role switch
            {
                SpotRole.Work => new Color(1f, 0.6f, 0.1f),
                SpotRole.Gathering => new Color(1f, 0.3f, 0.6f),
                SpotRole.Errand => new Color(0.4f, 0.9f, 0.4f),
                _ => new Color(0.3f, 0.8f, 1f),
            };
        }
    }
}
