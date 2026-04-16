using UnityEngine;

/// <summary>
/// ScriptableObject holding references to all settlement tile prefabs.
/// Assign in the Inspector. Create via Assets > Create > Settlement > Prefab Config.
/// </summary>
[CreateAssetMenu(fileName = "SettlementPrefabConfig", menuName = "Settlement/Prefab Config")]
public class SettlementPrefabConfig : ScriptableObject
{
    [Header("Structural")]
    public GameObject wallPrefab;
    public GameObject pillarPrefab;
    public GameObject floorPrefab;

    [Header("Scale")]
    [Tooltip("World units per tile. All meshes must be modelled at this size.")]
    public float tileSize = 1f;
    [Tooltip("Y scale of the floor/roof slab mesh (0.1 if your cube has Y scale 0.1).")]
    public float slabThickness = 0.1f;
}
