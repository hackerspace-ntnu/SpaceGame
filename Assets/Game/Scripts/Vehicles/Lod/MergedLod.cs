// The far level a settlement prefab carries (SettlementLodBaker, SettlementLods.md): its LODGroup, and
// the one merged mesh -- one submesh per material, in the prefab's rest pose -- that LOD1 draws. Read by
// DistantGroupSilhouette to draw the folded Strider city.
//
// The merged level is the rest pose. A body that died lies where it fell (a crab on its back, a barge
// on its side), so while the root's HealthComponent reads dead the group is held at LOD0 at every
// distance. Health replicates (RestoreHealth raises OnDeath/OnRevive on clients), so every machine
// holds the same level and nothing is sent or saved here.
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public sealed class MergedLod : MonoBehaviour
    {
        /// <summary>The child the baker puts the merged level on, at the root's identity pose.</summary>
        public const string ChildName = "LOD1_Merged";

        [SerializeField] private LODGroup group;
        [SerializeField] private MeshFilter mergedFilter;
        [SerializeField] private MeshRenderer mergedRenderer;

        private HealthComponent health;

        public LODGroup Group => group;
        public Mesh Mesh => mergedFilter.sharedMesh;
        public Material[] Materials => mergedRenderer.sharedMaterials;
        public MeshRenderer MergedRenderer => mergedRenderer;

        /// <summary>Baker only: the group and the merged level it switches to.</summary>
        public void Configure(LODGroup lodGroup, MeshFilter filter, MeshRenderer renderer)
        {
            group = lodGroup;
            mergedFilter = filter;
            mergedRenderer = renderer;
        }

        /// <summary>The level to force: LOD0 for a dead body, none (-1, the group chooses) for a living one.</summary>
        public static int ForcedLevel(bool alive) => alive ? -1 : 0;

        private void OnEnable()
        {
            health = GetComponent<HealthComponent>();
            if (health == null) return;

            health.OnDeath += Sync;
            health.OnRevive += Sync;
            health.OnRestored += Sync;
            Sync();
        }

        private void OnDisable()
        {
            if (health == null) return;

            health.OnDeath -= Sync;
            health.OnRevive -= Sync;
            health.OnRestored -= Sync;
        }

        private void Sync() => group.ForceLOD(ForcedLevel(health.Alive));
    }
}
