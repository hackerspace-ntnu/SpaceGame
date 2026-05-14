using UnityEngine;

/// <summary>
/// Bundle returned by <see cref="CaveGenerator.Generate"/>. Holds everything a caller needs
/// to instantiate the cave: the high-level graph (for spawning lights / loot / encounters),
/// the underlying density field (for further carving / debug gizmos), and the extracted mesh.
/// </summary>
public class CaveGenerationResult
{
    public CaveGraph Graph;
    public ICaveDensityField DensityField;
    public Mesh Mesh;
    public Bounds Bounds;
    public int Seed;
}
