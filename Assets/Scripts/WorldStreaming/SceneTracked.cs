using UnityEngine;

/// <summary>
/// Opt-in marker for any GameObject whose position should influence world streaming
/// and/or whose scene membership should follow the chunk it currently sits in.
///
/// Attach to vehicles, NPCs, dropped items, anything that moves between chunks at runtime.
/// Self-registers with WorldStreamer in OnEnable / unregisters in OnDisable.
/// </summary>
public class SceneTracked : MonoBehaviour
{
    public enum UnloadPolicy
    {
        // Always live in the persistent scene. Never moves between chunk scenes.
        // Use for player-attached entities (mounts) and anything that must outlive chunk unloads.
        Pin,

        // Live in the chunk scene the entity is currently over. Forces the chunk to stay loaded
        // while present (when keepChunksLoaded is true). Use for NPCs, dropped items, world props
        // that should belong to whichever chunk they're in.
        Migrate,

        // Allow the chunk to unload normally. The entity will be destroyed with it.
        // Use for ephemeral things you don't want to persist a chunk for.
        Despawn,
    }

    [Tooltip("If true, this entity's position is added to WorldStreamer's required-chunks set, " +
             "keeping nearby chunks loaded around it.")]
    [SerializeField] private bool keepChunksLoaded = true;

    [Tooltip("What to do with this entity when chunk membership changes or its current chunk wants to unload.")]
    [SerializeField] private UnloadPolicy policy = UnloadPolicy.Migrate;

    public bool KeepChunksLoaded => keepChunksLoaded;
    public UnloadPolicy Policy => policy;
    public Transform TrackedTransform => transform;

    private void OnEnable()
    {
        WorldStreamer.RegisterTracked(this);
    }

    private void OnDisable()
    {
        WorldStreamer.UnregisterTracked(this);
    }
}
