using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Full description of a cave type. One asset = one biome.
    ///
    /// A CaveProfile bundles:
    ///   • <see cref="shape"/>     — graph + density + meshing parameters (the "physical cave")
    ///   • <see cref="decoration"/>— scatter rules for stalagmites / vegetation / crystals / alien flora
    ///   • <see cref="liquid"/>    — per-pool liquid detection + spawning
    ///   • <see cref="material"/>  — primary material (shader), cluster lights, fog, layer
    ///
    /// A <see cref="CaveSpawner"/> references a profile; generating with the same profile + seed
    /// produces a deterministic cave. Different profiles with the same seed produce caves that
    /// share footprint but differ everywhere visually/decoratively.
    ///
    /// Create one via the menu: Create → Cave → Cave Profile.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCaveProfile", menuName = "Cave/Cave Profile", order = 100)]
    public class CaveProfile : ScriptableObject
    {
        [Tooltip("Human-readable description. Not used by code — purely a notebook for designers.")]
        [TextArea(2, 5)] public string description = "";

        [Header("Shape (graph + density + meshing)")]
        public CaveGenerationSettings shape = new CaveGenerationSettings();

        [Header("Decoration (stalagmites / vegetation / crystals / alien flora)")]
        public CaveDecorationSettings decoration = new CaveDecorationSettings();

        [Header("Liquid (per-pool water / lava)")]
        public CaveLiquidSettings liquid = new CaveLiquidSettings();

        [Header("Material / Lighting / Fog")]
        public CaveMaterialSettings material = new CaveMaterialSettings();

        /// <summary>
        /// Deep clone — useful when a spawner wants to override one or two fields per-instance
        /// without mutating the shared asset. Returns a fresh CaveProfile in-memory.
        /// </summary>
        public CaveProfile Clone()
        {
            var copy = Instantiate(this);
            copy.name = name + " (Clone)";
            return copy;
        }
    }
}
