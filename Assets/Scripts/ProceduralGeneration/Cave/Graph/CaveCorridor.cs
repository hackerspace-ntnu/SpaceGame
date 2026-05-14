using UnityEngine;

public enum CorridorKind
{
    Normal,      // average corridor
    Tight,       // narrow squeeze
    Wide,        // big tunnel
    Bridge,      // added by the connectivity pass to merge disjoint components
}

[System.Serializable]
public struct CaveCorridor
{
    public int FromRoomId;
    public int ToRoomId;
    public float Radius;
    public CorridorKind Kind;
}
