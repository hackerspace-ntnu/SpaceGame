// Marks a doorway on a building prefab: on the ground in front of the door, +Z pointing away from the
// building. A building lists its entrances as child objects, main door first; a planned settlement
// paves a path from the main door to the street the building faces. A building with none (an open
// yard, a market) is entered at the middle of its front wall (its local +Z side) instead.
// Scenery-only: nothing reads it at runtime.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public class SettlementEntrance : MonoBehaviour
    {
        public readonly struct Doorway
        {
            /// <summary>World XZ on the ground in front of the door.</summary>
            public readonly Vector2 position;
            /// <summary>Unit direction in XZ pointing away from the building.</summary>
            public readonly Vector2 outward;

            public Doorway(Vector2 position, Vector2 outward)
            {
                this.position = position;
                this.outward = outward;
            }
        }

        /// <summary>
        /// The first <see cref="SettlementEntrance"/> under <paramref name="building"/>, or the middle
        /// of the side of <paramref name="worldFootprint"/> its forward points through.
        /// </summary>
        public static Doorway MainDoorway(Transform building, in SettlementFootprint worldFootprint)
        {
            var entrance = building.GetComponentInChildren<SettlementEntrance>();
            Transform facing = entrance != null ? entrance.transform : building;
            Vector3 forward = Vector3.ProjectOnPlane(facing.forward, Vector3.up).normalized;
            var outward = new Vector2(forward.x, forward.z);
            if (entrance != null) return new Doorway(new Vector2(facing.position.x, facing.position.z), outward);

            Vector2 wall = worldFootprint.center + outward * worldFootprint.ExtentAlong(outward);
            return new Doorway(wall, outward);
        }

        /// <summary>
        /// Across-the-front position (prefab-local X) of <paramref name="prefab"/>'s main door: its first
        /// <see cref="SettlementEntrance"/>, else the middle of <paramref name="localFootprint"/>. Same frame as
        /// <see cref="SettlementPlacementUtil.MeasureFootprint"/>: the root's position and rotation, not its scale.
        /// </summary>
        public static float LocalDoorX(GameObject prefab, Rect localFootprint)
        {
            var entrance = prefab.GetComponentInChildren<SettlementEntrance>(true);
            if (entrance == null) return localFootprint.center.x;
            Transform root = prefab.transform;
            return Matrix4x4.TRS(root.position, root.rotation, Vector3.one).inverse.MultiplyPoint3x4(entrance.transform.position).x;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            Vector3 p = transform.position + Vector3.up * 0.1f;
            Gizmos.DrawWireSphere(p, 0.3f);
            Gizmos.DrawLine(p, p + transform.forward * 1.5f);
        }
    }
}
