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
}
