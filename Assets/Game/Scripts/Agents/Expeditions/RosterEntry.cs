// One resident as the expedition director knows it while the settlement's chunk may be unloaded: who it is, what
// it can be on a band, who it is close to and which prefab its stand-in is spawned from. Baked into the site
// catalog and refreshed from the scene whenever the chunk loads; saved with the settlement's expedition state.
// Plain fields only (no Vector3, no asset references), so the save serializer writes it as it is. A row is built in
// one place (Of), so the baked roster and the one a loaded settlement reports read a resident the same way.
using System;
using SpaceGame.Agents.Residents;

namespace SpaceGame.Agents.Expeditions
{
    [Serializable]
    public sealed class RosterEntry
    {
        /// <summary>The resident's key (<see cref="ResidentKey"/>): "r:" + its roster index for an authored resident.</summary>
        public string residentKey;

        /// <summary>Index of the resident's archetype in its culture's <c>archetypes</c> list; -1 when it is not listed.</summary>
        public int archetypeIndex = -1;

        /// <summary>What it can be on a band: its archetype's <c>expeditionRoles</c>.</summary>
        public ExpeditionRole roles;

        /// <summary>False for a child, who never goes. Every authored resident is an adult: only births make children.</summary>
        public bool adult = true;

        /// <summary>The roster indices (<c>Resident.index</c>) of its family and friends (<c>Resident.CloseTo</c>), whom a band prefers to take along.</summary>
        public int[] bonds = Array.Empty<int>();

        /// <summary>Asset GUID of the prefab its body was placed from; the stand-in is spawned from it. Empty when unknown.</summary>
        public string sourcePrefabGuid = string.Empty;

        /// <summary>Its name (<c>Resident.displayName</c>), which its stand-in carries. Empty = the stand-in is called by its role.</summary>
        public string displayName = string.Empty;

        /// <summary>
        /// The row of <paramref name="resident"/>, of a settlement of <paramref name="culture"/>: the catalog baker's and the
        /// settlement's live report alike. <paramref name="sourcePrefabGuid"/> is the asset GUID of its source prefab, which
        /// each caller finds its own way (the AssetDatabase when baking, the ExpeditionCatalog at runtime).
        /// </summary>
        public static RosterEntry Of(Resident resident, SettlementCulture culture, string sourcePrefabGuid)
        {
            ResidentArchetype archetype = resident.archetype;
            return new RosterEntry
            {
                residentKey = ResidentKey.ForAuthored(resident.index),
                archetypeIndex = archetype ? Array.IndexOf(culture.archetypes, archetype) : -1,
                roles = archetype ? archetype.expeditionRoles : ExpeditionRole.None,
                bonds = resident.CloseTo(),
                sourcePrefabGuid = sourcePrefabGuid ?? string.Empty,
                displayName = resident.displayName ?? string.Empty,
            };
        }
    }
}
