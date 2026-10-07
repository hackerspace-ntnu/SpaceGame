using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Visual / lighting sub-config. Owns the cave's primary material (and optional per-RoomKind
    /// overrides routed through submeshes), plus the cluster-light scatter that used to live on
    /// CaveSpawner. Lifted here so a CaveProfile fully defines "how this cave looks."
    /// </summary>
    [System.Serializable]
    public class CaveMaterialSettings
    {
        [Header("Primary surface")]
        [Tooltip("Material applied to the cave mesh. Falls back to URP/Lit if null. Assign your CaveTriplanar.shader-based material here for the alien look.")]
        public Material primaryMaterial;

        [Tooltip("Optional second material applied via submesh to BigChamber/Cathedral rooms. Null = primary material is used for everything.")]
        public Material chamberMaterial;

        [Header("Cluster lights")]
        [Tooltip("Number of ambient point lights placed along the walls. 0 = none.")]
        public int clusterLightCount = 20;

        public Color clusterLightColor = new Color(0.35f, 0.75f, 1.0f, 1f);

        [Range(0f, 8f)] public float clusterLightIntensity = 1.2f;

        public float clusterLightRange = 5f;

        [Tooltip("Surfaces whose inward-normal.y exceeds this are treated as floor/ceiling and skipped (lights only on walls).")]
        [Range(0f, 1f)] public float clusterLightMaxNormalY = 0.7f;

        [Header("Ambient & fog")]
        [Tooltip("If true, RenderSettings.fog is forced on while the cave is loaded (and restored on destroy).")]
        public bool overrideFog = false;
        public Color fogColor = new Color(0.05f, 0.08f, 0.10f);
        [Range(0f, 0.2f)] public float fogDensity = 0.025f;

        [Tooltip("Layer for spawned cave geometry + decor. Defaults to 'Interior' if it exists, else 'Default'.")]
        public string caveLayer = "Interior";
    }
}
