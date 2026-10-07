// Somewhere the sand cannot reach you.
//
// Drop this on the ShipRV, a cave mouth, a building interior — anything with an inside — and
// whoever is in it stops being sanded. The volume is expressed in the component's own local
// space, so it rotates and moves with whatever it is attached to: the ship shelters you while it
// is driving, which is the whole reason this is a component and not a baked-out region.
//
// It is a box and not a trigger collider on purpose. A shelter is a point query, never a physics
// object, and putting a live trigger on a drivable vehicle would quietly enter every SceneTransition
// and VolumeTrigger the ship drove through. Colliders are still accepted for shapes a box cannot
// express — a cave mouth — but they have to be named explicitly; nothing is collected automatically,
// because "every collider under this object" on a vehicle means its whole outer hull.
//
// The doors list is what makes closing the hatch mean something: with one open you get a reduced
// value rather than none, because an open hatch is still better than open desert, and because
// watching the fog thin as the door swings shut is the feedback that teaches the rule.
using System.Collections.Generic;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [DisallowMultipleComponent]
    public class SandstormShelter : MonoBehaviour
    {
        private static readonly List<SandstormShelter> Active = new List<SandstormShelter>();

        [Tooltip("The sheltered space, in this object's local space. Draw it with the gizmo: it is " +
                 "shown whenever this object is selected.")]
        [SerializeField] private Bounds localVolume = new Bounds(Vector3.zero, new Vector3(4f, 3f, 10f));

        [Tooltip("Extra volumes for shapes a box cannot express. Optional, and never filled in " +
                 "automatically — listing a vehicle's hull colliders here would shelter its roof.")]
        [SerializeField] private Collider[] extraVolumes;

        [Tooltip("How much of the storm is kept out when everything is shut. 1 is total safety.")]
        [SerializeField, Range(0f, 1f)] private float shelter = 1f;

        [Tooltip("How much is kept out while any door below is open.")]
        [SerializeField, Range(0f, 1f)] private float shelterWithDoorOpen = 0.35f;

        [Tooltip("Doors that must be shut for full shelter. Leave empty for a sealed space such " +
                 "as a cave, where there is nothing to close.")]
        [SerializeField] private DoorInteraction[] doors;

        [SerializeField] private bool drawGizmos = true;

        /// <summary>How sheltered a point is, 0 to 1, taking the best cover that contains it.</summary>
        public static float ShelterAt(Vector3 worldPos)
        {
            float best = 0f;
            for (int i = 0; i < Active.Count; i++)
            {
                SandstormShelter candidate = Active[i];
                if (candidate.CurrentShelter <= best || !candidate.Contains(worldPos))
                    continue;

                best = candidate.CurrentShelter;
                if (best >= 1f)
                    break;
            }

            return best;
        }

        /// <summary>What this shelter is worth right now, given the state of its doors.</summary>
        public float CurrentShelter => AnyDoorOpen ? shelterWithDoorOpen : shelter;

        public bool AnyDoorOpen
        {
            get
            {
                if (doors == null)
                    return false;

                for (int i = 0; i < doors.Length; i++)
                {
                    if (doors[i] != null && doors[i].IsOpen)
                        return true;
                }

                return false;
            }
        }

        public bool Contains(Vector3 worldPos)
        {
            // Local space, so a rotated or moving shelter needs no special case and no per-frame
            // bookkeeping — the transform already holds the answer.
            if (localVolume.size.sqrMagnitude > 0f &&
                localVolume.Contains(transform.InverseTransformPoint(worldPos)))
            {
                return true;
            }

            if (extraVolumes == null)
                return false;

            for (int i = 0; i < extraVolumes.Length; i++)
            {
                Collider volume = extraVolumes[i];

                // ClosestPoint returns the point itself only when it is inside, and is exact for
                // the convex shapes a shelter is built from.
                if (volume != null && volume.enabled && volume.ClosestPoint(worldPos) == worldPos)
                    return true;
            }

            return false;
        }

        /// <summary>Sets the sheltered box in local space. Used by editor tooling that measures a model.</summary>
        public void SetLocalVolume(Bounds value) => localVolume = value;

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Gizmos.color = AnyDoorOpen ? new Color(1f, 0.6f, 0.1f, 0.6f) : new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(localVolume.center, localVolume.size);
            Gizmos.matrix = Matrix4x4.identity;

            if (extraVolumes == null)
                return;

            for (int i = 0; i < extraVolumes.Length; i++)
            {
                if (extraVolumes[i] == null)
                    continue;

                Bounds bounds = extraVolumes[i].bounds;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
