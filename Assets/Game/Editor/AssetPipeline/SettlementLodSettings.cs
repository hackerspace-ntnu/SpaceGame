// The tunables of the generated settlement LODs (SettlementLodBaker, SettlementLods.md), one profile per
// settlement, as distances: the baker turns them into screen heights for each prefab's own size, so a
// 2 m monowheel and a 21 m house switch at the same range. Created with these defaults the first time
// anything loads it; edit the asset, then re-run Tools/SpaceGame/Art/Bake Settlement LODs.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    [CreateAssetMenu(fileName = "SettlementLodSettings", menuName = "SpaceGame/Art/Settlement LOD Settings")]
    public sealed class SettlementLodSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Game/Settings/SettlementLodSettings.asset";

        [System.Serializable]
        public struct Profile
        {
            [Tooltip("Camera distance (m) beyond which the merged level replaces the prefab's own renderers.")]
            [Min(1f)] public float mergedBeyondMetres;

            [Tooltip("Camera distance (m) beyond which the prefab is not drawn at all.")]
            [Min(1f)] public float cullBeyondMetres;

            [Tooltip("Most Mesh LOD levels generated inside the merged mesh (MeshLodUtility.GenerateMeshLods). " +
                     "Negative: keep simplifying until a level has about 64 indices.")]
            public int meshLodLimit;

            [Tooltip("Vertical field of view (degrees) the distances are converted with: the player camera's " +
                     "(GameSettings default 60).")]
            [Range(1f, 179f)] public float referenceFovDegrees;
        }

        [Tooltip("The Strider city's vehicles. Merged inside spawnRadius (250 m), so the live city arrives already " +
                 "drawing the level its silhouette drew; culled past the loaded ground (~1250 m).")]
        public Profile strider = new Profile
        {
            mergedBeyondMetres = 160f,
            cullBeyondMetres = 1500f,
            meshLodLimit = 4,
            referenceFovDegrees = 60f,
        };

        [Tooltip("The Sky fleet: the flagship city and its escorts. Culled beyond the 4 x 3 km map's diagonal, so " +
                 "the fleet stays in the sky from anywhere, as the old 2 % cull group did.")]
        public Profile sky = new Profile
        {
            mergedBeyondMetres = 400f,
            cullBeyondMetres = 8000f,
            meshLodLimit = 4,
            referenceFovDegrees = 60f,
        };

        /// <summary>The settings asset, created with the defaults above if it does not exist yet.</summary>
        public static SettlementLodSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SettlementLodSettings>(AssetPath);
            if (settings != null) return settings;

            settings = CreateInstance<SettlementLodSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
