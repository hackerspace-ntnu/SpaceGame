// Every expedition profile in the game, in one list (Resources/Expeditions/ExpeditionCatalog). A stand-in
// replicates its profile as an index into this list, so the order is part of the wire format: append
// only. It also maps the source-prefab GUIDs of baked rosters to prefabs, so the director can spawn a
// resident's stand-in while its settlement's chunk is not loaded.
using System;
using UnityEngine;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>A resident prefab by its asset GUID, as the site catalog's roster rows name it.</summary>
    [Serializable]
    public struct PrefabEntry
    {
        public string guid;
        public GameObject prefab;
    }

    [CreateAssetMenu(menuName = "SpaceGame/Expeditions/Catalog", fileName = "ExpeditionCatalog")]
    public sealed class ExpeditionCatalog : ScriptableObject
    {
        public const string ResourcePath = "Expeditions/ExpeditionCatalog";

        [Tooltip("Every profile a settlement may run. Append only: a stand-in replicates its profile as an index into this list.")]
        public ExpeditionProfile[] profiles = Array.Empty<ExpeditionProfile>();

        [Tooltip("Written by the site catalog baker: every resident prefab a baked roster names, by asset GUID.")]
        public PrefabEntry[] prefabs = Array.Empty<PrefabEntry>();

        private static ExpeditionCatalog instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>The project's catalog; an empty one when the asset is missing. Never null.</summary>
        public static ExpeditionCatalog Instance
        {
            get
            {
                if (instance) return instance;

                instance = Resources.Load<ExpeditionCatalog>(ResourcePath);
                if (instance) return instance;

                Debug.LogWarning($"ExpeditionCatalog: no asset at Resources/{ResourcePath} — no settlement runs bands.");
                instance = CreateInstance<ExpeditionCatalog>();
                return instance;
            }
        }

        /// <summary>The replicated index of <paramref name="profile"/>; -1 when it is not listed.</summary>
        public int IndexOf(ExpeditionProfile profile) => profile ? Array.IndexOf(profiles, profile) : -1;

        /// <summary>The prefab baked under <paramref name="guid"/>; null when the baker did not list it.</summary>
        public GameObject PrefabFor(string guid)
        {
            foreach (PrefabEntry entry in prefabs)
                if (entry.guid == guid) return entry.prefab;
            return null;
        }

        /// <summary>The GUID <paramref name="prefab"/> was baked under; empty when the baker did not list it (or for none).</summary>
        public string GuidOf(GameObject prefab)
        {
            if (prefab == null) return string.Empty;

            foreach (PrefabEntry entry in prefabs)
                if (entry.prefab == prefab) return entry.guid;
            return string.Empty;
        }
    }
}
