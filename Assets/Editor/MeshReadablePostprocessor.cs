using UnityEditor;

/// <summary>
/// Automatically enables Read/Write on all imported meshes so the runtime
/// NavMesh builder can use their actual geometry instead of box approximations.
/// </summary>
public class MeshReadablePostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        if (!importer.isReadable)
        {
            importer.isReadable = true;
        }
    }
}
