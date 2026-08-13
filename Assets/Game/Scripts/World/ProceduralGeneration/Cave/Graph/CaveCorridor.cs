using UnityEngine;

namespace SpaceGame.World
{
    public enum CorridorKind
    {
        Normal,      // average corridor
        Tight,       // narrow squeeze
        Wide,        // big tunnel
        Bridge,      // added by the connectivity pass to merge disjoint components
        Slot,        // tall narrow slit (slot canyon)
        Shaft,       // near-vertical shaft / chimney (drops slope rule, vertical capsule)
        Entrance,    // thin dead-end passage leading up to the cave's single entrance chamber
    }

    [System.Serializable]
    public struct CaveCorridor
    {
        public int FromRoomId;
        public int ToRoomId;
        public float Radius;
        public CorridorKind Kind;

        /// <summary>
        /// Vertical-stretch factor for slot canyons. 1 = round capsule, >1 = taller than wide.
        /// Ignored for non-Slot kinds.
        /// </summary>
        public float HeightStretch;
    }
}
