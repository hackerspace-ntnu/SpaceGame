using UnityEngine;

/// <summary>
/// Entry point MonoBehaviour. Drop onto a GameObject, assign config, hit Generate.
/// </summary>
[RequireComponent(typeof(SettlementBuilder))]
public class SettlementSpawner : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private SettlementPrefabConfig config;

    [Header("Generation")]
    [Tooltip("Seed. Same seed = identical ruins.")]
    [SerializeField] private int seed = 42;

    [SerializeField] private SettlementGenerationSettings settings = new();

    private SettlementBuilder builder;

    void Awake() => builder = GetComponent<SettlementBuilder>();

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (config == null)
        {
            Debug.LogError("[SettlementSpawner] Assign a SettlementPrefabConfig first.");
            return;
        }
        builder = GetComponent<SettlementBuilder>();
        builder.SetConfig(config);

        var placements = SettlementGenerator.GenerateFull(seed, settings);
        builder.Build(placements, transform.position);
        Debug.Log($"[SettlementSpawner] {placements.Count} tiles. Seed={seed}");
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
        float s    = config != null ? config.tileSize : 1f;
        float size = settings != null ? settings.footprintRadius * 2 * s : 14f;
        float h    = settings != null ? settings.maxHeight * s : 20f;
        Gizmos.color = new Color(0.8f, 0.6f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(
            transform.position + new Vector3(0, h / 2f, 0),
            new Vector3(size, h, size));
    }
#endif
}
