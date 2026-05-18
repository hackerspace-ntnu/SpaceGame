using UnityEngine;

public enum RoomKind
{
    Normal,
    BigChamber,
    Junction,
    DeadEnd,
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
