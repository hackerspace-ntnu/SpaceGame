using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="NoiseHeightmapGenerator"/>: Generate writes the heightmap and slope
    /// splat into the terrain's <see cref="TerrainData"/> asset (undoable, and saved straight to disk,
    /// because saving the scene does not write a <c>TerrainData</c>); New Seed rolls a fresh world.
    /// </summary>
    [CustomEditor(typeof(NoiseHeightmapGenerator))]
    public class NoiseHeightmapGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (NoiseHeightmapGenerator)target;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28)))
                    Generate(generator);
                if (GUILayout.Button("New Seed + Generate", GUILayout.Height(28)))
                {
                    Undo.RecordObject(generator, "New Heightmap Seed");
                    generator.Seed = Random.Range(int.MinValue, int.MaxValue);
                    EditorUtility.SetDirty(generator);
                    Generate(generator);
                }
            }
        }

        public static void Generate(NoiseHeightmapGenerator generator)
        {
            TerrainData data = generator.GetComponent<Terrain>().terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Generate Noise Heightmap");
            float highest = generator.Generate();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"[NoiseHeightmap] '{generator.name}' generated (seed {generator.Seed}), highest point {highest:F0} m.", generator);
        }
    }
}
