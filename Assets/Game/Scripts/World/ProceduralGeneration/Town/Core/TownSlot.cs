// One decided placement: what goes there, where, which way round, how big.
//
// Deliberately holds no GameObject and no Transform. TownLayout produces a list of these from a
// recipe and a seed and nothing else, which is what lets the whole layout be tested in EditMode
// without a scene, a terrain or a prefab — the same trade WorldSite makes, for the same reason.
using System;
using UnityEngine;

namespace SpaceGame.World.Towns
{
    [Serializable]
    public struct TownSlot
    {
        /// <summary>Which of the recipe's four lists this came from.</summary>
        public TownSection Section;

        /// <summary>Index into that list. -1 for the centrepiece, which has no group.</summary>
        public int GroupIndex;

        /// <summary>Index into the group's <c>prefabs</c> array.</summary>
        public int PrefabIndex;

        /// <summary>Offset from the town's origin, on the ground plane. Y comes from the terrain later.</summary>
        public Vector2 LocalXZ;

        /// <summary>Degrees about Y.</summary>
        public float Yaw;

        /// <summary>Uniform scale multiplier. 1 for almost everything.</summary>
        public float Scale;

        public override string ToString() =>
            $"{Section}[{GroupIndex}] prefab {PrefabIndex} @ ({LocalXZ.x:F1}, {LocalXZ.y:F1}) yaw {Yaw:F0}°";
    }
}
