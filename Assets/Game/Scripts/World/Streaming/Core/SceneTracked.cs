using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.World
{
    /// <summary>
    /// Opt-in marker for any GameObject whose position should influence world streaming
    /// and/or whose scene membership should follow the chunk it currently sits in.
    ///
    /// Attach to vehicles, NPCs, dropped items, anything that moves between chunks at runtime.
    /// Self-registers with WorldStreamer in OnEnable / unregisters in OnDisable.
    ///
    /// Also <see cref="IPersistentEntity"/>: "this thing moves between chunks at runtime" and "this
    /// thing must survive a save" are the same claim, so declaring one declares the other. Scene
    /// membership is a persistence concern as well as a streaming one — <c>WorldSaveStore.Dehydrate</c>
    /// is driven per scene, and an entity in a chunk that unloads without this marker is destroyed
    /// with it.
    /// </summary>
    public class SceneTracked : MonoBehaviour, IPersistentEntity
    {
        public enum UnloadPolicy
        {
            // Always live in the persistent scene. Never moves between chunk scenes.
            // Use for player-attached entities (mounts) and anything that must outlive chunk unloads.
            Pin,

            // Live in the chunk scene the entity is currently over, and keep that chunk loaded for
            // as long as this is standing in it. Use for NPCs, vehicles and world props whose
            // disappearing where somebody can see it would read as a bug.
            Migrate,

            // Live in the chunk scene the entity is currently over, and let that chunk go. The
            // entity is captured into the save record as the chunk unloads and rebuilt from it when
            // the chunk comes back, so nothing is lost — it simply is not resident while nobody is
            // there. Use for the things a player leaves lying around: dropped items, which would
            // otherwise keep a corner of the world loaded for each place anyone has ever put
            // something down.
            Release,

            // Allow the chunk to unload normally. The entity will be destroyed with it, and stays
            // in whatever scene it was created in until then.
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

        /// <summary>
        /// Set whether this entity pins chunks, at runtime.
        ///
        /// <para>
        /// Exists for entities that are spawned rather than placed, where the prefab cannot know the
        /// answer. <c>NpcWorldSim</c> is the case: an NPC it spawns is always within a few hundred
        /// metres of a player, so its chunk is loaded regardless — and pinning would mean every
        /// caravan in the world dragging its own nine loaded chunks around behind it, which for a
        /// dozen groups is most of the world resident at once.
        /// </para>
        /// <para>
        /// Applied by re-registering, because <c>WorldStreamer</c> reads the flag when a tracker
        /// joins rather than every frame.
        /// </para>
        /// </summary>
        /// <summary>
        /// Set the unload policy at runtime, for an entity whose prefab does not carry this
        /// component at all — a dropped item, which <c>SaveablePolicy.EnsureSpawned</c> gives one to
        /// at the moment it is spawned. Nothing re-registers here because
        /// <c>WorldStreamer.UpdateSceneMembership</c> reads the policy every tick.
        /// </summary>
        public void SetPolicy(UnloadPolicy value) => policy = value;

        public void SetKeepChunksLoaded(bool pin)
        {
            if (keepChunksLoaded == pin) return;

            keepChunksLoaded = pin;

            if (!isActiveAndEnabled) return;

            WorldStreamer.UnregisterTracked(this);
            WorldStreamer.RegisterTracked(this);
        }

        private void OnEnable()
        {
            WorldStreamer.RegisterTracked(this);
        }

        private void OnDisable()
        {
            WorldStreamer.UnregisterTracked(this);
        }
    }
}
