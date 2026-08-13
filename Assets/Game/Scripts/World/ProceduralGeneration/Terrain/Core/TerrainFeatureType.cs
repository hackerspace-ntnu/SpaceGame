namespace SpaceGame.World
{
    /// <summary>
    /// Open registry of procedural terrain feature kinds. The <c>TerrainFeatureSpawner</c> inspector
    /// shows this as a dropdown; <see cref="TerrainFeatureRegistry"/> maps each entry to the concrete
    /// <see cref="TerrainFeature"/> subclass that builds it.
    ///
    /// This enum is deliberately OPEN: the foundation ships only <see cref="FlatPad"/> as a reference
    /// implementation. Each of the nine planned features has a reserved entry here so a feature-agent
    /// can implement the class and register it without touching the spawner or editor. Do not reorder
    /// existing entries (serialized scenes store the integer value) — only append.
    /// </summary>
    public enum TerrainFeatureType
    {
        // --- Reference stub (shipped with the foundation) -----------------------
        /// <summary>Trivial reference feature: a flat raised pad. Template for feature agents.</summary>
        FlatPad = 0,

        // --- Area features (use the box footprint) ------------------------------
        /// <summary>Rolling wind-blown sand dunes filling the box. Heightfield density.</summary>
        SandDunes = 1,
        /// <summary>Flat-topped, steep-sided mesa. Heightfield density.</summary>
        Mesa = 2,
        /// <summary>Tall narrow isolated butte / rock spire. Heightfield density.</summary>
        Butte = 3,
        /// <summary>A cliff escarpment — a height step across the box. Heightfield density.</summary>
        Cliff = 4,

        // --- Linear features (use the spline path) ------------------------------
        /// <summary>Narrow carved canyon following the spline. Heightfield density.</summary>
        Canyon = 5,
        /// <summary>Walkable path winding along a canyon floor. Heightfield density.</summary>
        CanyonPath = 6,
        /// <summary>Raised rock ridge following the spline. Heightfield density.</summary>
        Ridge = 7,
        /// <summary>Natural rock bridge spanning a gap — needs a real overhang. Voxel SDF density.</summary>
        NaturalBridge = 8,
        /// <summary>Free-standing stone arch — needs a real overhang. Voxel SDF density.</summary>
        StoneArch = 9,
        /// <summary>Surface cave entrance / overhang mouth — needs a real overhang. Voxel SDF density.</summary>
        CaveEntrance = 10,

        // --- Large composite area features --------------------------------------
        /// <summary>Monumental desert rock complex — a forest of pillars and natural arches with light
        /// through the gaps. AREA feature, voxel SDF, internally chunked into many sub-meshes.</summary>
        ArchingCave = 11,

        /// <summary>A badlands maze — a solid rock massif eroded by a wide branching channel network,
        /// leaving a labyrinth of mesas with overhangs and cliffs, scattered with boulders. Walked from
        /// the channel floors looking up. AREA feature, voxel SDF, internally chunked into sub-meshes.</summary>
        BadlandsMaze = 12,

        /// <summary>A scattered field of natural, eroded rock boulders strewn across the footprint
        /// polygon. Noise-driven non-uniform placement (clusters and sparse areas), each boulder a
        /// domain-warp eroded 3D rock partially buried in the ground. AREA feature, voxel SDF.</summary>
        Boulders = 13,
    }
}
