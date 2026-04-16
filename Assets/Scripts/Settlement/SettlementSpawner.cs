using UnityEngine;

/// <summary>
/// Entry point. Drop this on a GameObject in your scene.
/// Assign the config, set seed and size, then hit Generate in the Inspector
/// (or call Generate() from code).
///
/// In Play mode or in Editor via the context menu button.
/// </summary>
[RequireComponent(typeof(SettlementBuilder))]
public class SettlementSpawner : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private SettlementPrefabConfig config;

    [Header("Layout")]
    [Tooltip("Random seed. Same seed = identical settlement.")]
    [SerializeField] private int seed = 42;

    [Tooltip("Half-width of the settlement in tiles. 6 = roughly 12x12 tile footprint.")]
    [SerializeField] private int footprintRadius = 6;

    [Tooltip("Tallest tower height in floors.")]
    [SerializeField] private int maxHeight = 8;

    [Tooltip("Shortest building height in floors.")]
    [SerializeField] private int minHeight = 2;

    private SettlementBuilder builder;

    void Awake()
    {
        builder = GetComponent<SettlementBuilder>();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (config == null)
        {
            Debug.LogError("[SettlementSpawner] Assign a SettlementPrefabConfig first.");
            return;
        }

        builder = GetComponent<SettlementBuilder>();
        var placements = SettlementGenerator.Generate(seed, footprintRadius, maxHeight, minHeight);
        builder.Build(placements, transform.position);
        Debug.Log($"[SettlementSpawner] Generated {placements.Count} tiles. Seed={seed}");
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        builder = GetComponent<SettlementBuilder>();
        builder.Clear();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Draw a wire cube showing the approximate settlement footprint
        float s = config != null ? config.tileSize : 1f;
        float size = footprintRadius * 2 * s;
        float height = maxHeight * s;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireCube(
            transform.position + new Vector3(0, height / 2f, 0),
            new Vector3(size, height, size)
        );
    }
#endif
}
