using UnityEngine;

namespace SpaceGame.World
{
    public enum RoomKind
    {
        Normal,
        BigChamber,
        Junction,
        DeadEnd,
        /// <summary>
        /// The cave's single entrance chamber. Highest point of the cave, thin radius, always a
        /// dead end. Holds the entrance anchor + player spawn and is capped by the dark exit cover.
        /// </summary>
        Entrance,
    }

    [System.Serializable]
    public struct CaveRoom
    {
        public int Id;
        public Vector3 Center;
        public float Radius;
        public RoomKind Kind;

        /// <summary>
        /// Extra ceiling height added on top of the sphere — used by cathedral chambers to give large
        /// rooms dramatic vertical scale. 0 for normal rooms.
        /// </summary>
        public float CeilingLift;
    }
}
