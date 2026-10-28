using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How <see cref="CharacterLodWiring"/> splits a dressed character into three levels of detail:
    /// where each level starts, how far its meshes are reduced, which renderers it keeps and which of
    /// them cast shadows. Editor-only, like <see cref="EyeStyle"/>: the wiring writes these numbers
    /// onto each prefab's <c>LODGroup</c>, so a build carries the group and never this asset.
    ///
    /// <para>
    /// Tune here and run <i>Tools ▸ SpaceGame ▸ Agents ▸ Wire Character LODs</i> to push a change to
    /// every prefab in <see cref="CharacterFolders"/>. A value changed on one prefab's
    /// <c>LODGroup</c> by hand lasts until the next run.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Art/Character LOD Profile", fileName = "CharacterLodProfile")]
    public class CharacterLodProfile : ScriptableObject
    {
        [Header("Where each level ends (screen-relative height, before QualitySettings.lodBias)")]
        [Tooltip("The full character (LOD0) is drawn while it fills more than this share of the screen's height.")]
        [SerializeField, Range(0f, 1f)] private float fullDetailAbove = 0.25f;

        [Tooltip("The reduced character (LOD1) is drawn down to this share; below it, the silhouette (LOD2).")]
        [SerializeField, Range(0f, 1f)] private float reducedAbove = 0.08f;

        [Tooltip("Below this share the character is not drawn at all.")]
        [SerializeField, Range(0f, 1f)] private float culledBelow = 0.02f;

        [Header("Mesh reduction (MeshLodUtility levels: each halves the triangles)")]
        [Tooltip("Generated Mesh LOD level the reduced character (LOD1) wears. 1 is about half the triangles.")]
        [SerializeField, Min(1)] private int reducedMeshLevel = 1;

        [Tooltip("Generated Mesh LOD level the silhouette (LOD2) wears. 2 is about a quarter of the triangles.")]
        [SerializeField, Min(1)] private int silhouetteMeshLevel = 2;

        [Tooltip("A mesh with fewer triangles than this is worn as it is at every level; reducing it saves nothing worth a seam.")]
        [SerializeField, Min(0)] private int minTrianglesToReduce = 1000;

        [Header("Which garments each level keeps (outline against the body's: largest face of the rest bounds)")]
        [Tooltip("A garment with less than this share of the body's outline (a belt, the bracelets) is drawn at full detail only.")]
        [SerializeField, Range(0f, 1f)] private float accessoryBelow = 0.06f;

        [Tooltip("A garment with at least this share of the body's outline (a coat, trousers, a brimmed hat, a face mask) shapes the silhouette and is kept down to LOD2.")]
        [SerializeField, Range(0f, 1f)] private float silhouetteFrom = 0.1f;

        [Tooltip("Keep the eyes in the reduced character. They are what reads as a face at mid range, and are not copied: the same renderers, so the blink and a dead character's shut lids still apply.")]
        [SerializeField] private bool eyesInReduced = true;

        [Header("Shadows and skinning at range")]
        [Tooltip("In the reduced character (LOD1) only the body and garments with at least this share of the body's outline cast shadows; a mask or a strap's shadow is lost in the body's at that range.")]
        [SerializeField, Range(0f, 1f)] private float reducedShadowFrom = 0.2f;

        [Tooltip("The silhouette's body still casts a shadow, so a distant character keeps its contact with the ground.")]
        [SerializeField] private bool silhouetteBodyCastsShadow = true;

        [SerializeField] private SkinQuality reducedSkinQuality = SkinQuality.Auto;
        [SerializeField] private SkinQuality silhouetteSkinQuality = SkinQuality.Bone2;

        [Header("Where")]
        [Tooltip("Folder, beside each source model, that the reduced meshes are written to.")]
        [SerializeField] private string reducedMeshFolder = "LOD";

        [Tooltip("Prefab folders (not their subfolders) whose characters get LODs: by the menu item, and by the drifter builder for its own prefabs in them.")]
        [SerializeField] private string[] characterFolders = { "Assets/Game/Prefabs/Agents/Characters/Raxy" };

        public float FullDetailAbove => fullDetailAbove;
        public float ReducedAbove => reducedAbove;
        public float CulledBelow => culledBelow;
        public int ReducedMeshLevel => reducedMeshLevel;
        public int SilhouetteMeshLevel => silhouetteMeshLevel;
        public int MinTrianglesToReduce => minTrianglesToReduce;
        public float AccessoryBelow => accessoryBelow;
        public float SilhouetteFrom => silhouetteFrom;
        public bool EyesInReduced => eyesInReduced;
        public float ReducedShadowFrom => reducedShadowFrom;
        public bool SilhouetteBodyCastsShadow => silhouetteBodyCastsShadow;
        public SkinQuality ReducedSkinQuality => reducedSkinQuality;
        public SkinQuality SilhouetteSkinQuality => silhouetteSkinQuality;
        public string ReducedMeshFolder => reducedMeshFolder;
        public string[] CharacterFolders => characterFolders;
    }
}
