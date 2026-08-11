using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-level structural description of a cave: a set of rooms (spheres) joined by corridors (capsules).
/// Consumed by <see cref="CaveSdfField"/> to build the density function, but also exposed on the
/// generation result so gameplay systems (loot spawning, light placement, encounter design) can
/// reason about cave topology without re-deriving it from the mesh.
/// </summary>
public class CaveGraph
{
    public readonly List<CaveRoom> Rooms = new();
    public readonly List<CaveCorridor> Corridors = new();

    /// <summary>
    /// Index into <see cref="Rooms"/> of the cave's single entrance chamber, or -1 if none was
    /// generated. The entrance chamber is the highest point of the cave, thin, and a dead end.
    /// <see cref="CaveSpawner"/> places the InteriorAnchor + player spawn + dark exit cover here.
    /// </summary>
    public int EntranceRoomId = -1;

    /// <summary>The entrance chamber room, or null if <see cref="EntranceRoomId"/> is unset.</summary>
    public CaveRoom? EntranceRoom =>
        EntranceRoomId >= 0 && EntranceRoomId < Rooms.Count ? Rooms[EntranceRoomId] : (CaveRoom?)null;

    public Bounds ComputeBounds(float padding = 0f)
    {
        if (Rooms.Count == 0) return new Bounds(Vector3.zero, Vector3.one);

        Bounds b = new Bounds(Rooms[0].Center, Vector3.zero);
        foreach (var r in Rooms)
        {
            float p = r.Radius + padding;
            b.Encapsulate(r.Center + Vector3.one * p);
            b.Encapsulate(r.Center - Vector3.one * p);
        }
        return b;
    }
}
