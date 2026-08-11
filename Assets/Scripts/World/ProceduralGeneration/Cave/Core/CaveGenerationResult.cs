using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bundle returned by <see cref="CaveGenerator.Generate"/>. Holds the high-level graph,
/// the underlying density field, the extracted mesh, and any detected liquid pools.
/// Scene-level instantiation (decoration scatter, light placement, NavMesh bake) is the
/// <see cref="CaveSpawner"/>'s job — this object is pure data.
/// </summary>
public class CaveGenerationResult
{
    public CaveGraph Graph;
    public ICaveDensityField DensityField;
    public Mesh Mesh;
    public Bounds Bounds;
    public int Seed;

    /// <summary>Detected liquid pools (may be empty if liquid was disabled or none were large enough).</summary>
    public List<LiquidPool> LiquidPools = new List<LiquidPool>();
}
