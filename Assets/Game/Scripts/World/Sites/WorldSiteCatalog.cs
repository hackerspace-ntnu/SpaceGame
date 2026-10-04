// Every place in one world worth going to, and every settlement in it that sends bands out, baked at edit time
// from all of the world's chunk scenes (Tools/SpaceGame/World/Bake Site Catalog). One per WorldStreamingConfig.
//
// It exists because the registry used to learn a site only when the site's chunk loaded, and a destination 2 km
// away is in an unloaded chunk nearly all the time: an NPC could not pick a place it had never been near, and a
// settlement's bands could not be run while nobody stood near the settlement. The world merges the sites into
// WorldSiteRegistry when it starts; a marker that streams in afterwards updates its own entry in place (same id).
using System;
using UnityEngine;
using SpaceGame.Agents.Expeditions;

namespace SpaceGame.World
{
    [CreateAssetMenu(menuName = "World Streaming/Site Catalog", fileName = "SiteCatalog")]
    public sealed class WorldSiteCatalog : ScriptableObject
    {
        /// <summary>One <see cref="WorldSiteMarker"/> as it stood in its chunk scene when the catalog was baked.</summary>
        [Serializable]
        public struct SiteEntry
        {
            [Tooltip("The marker's id: what the registry keys the site by, and what saves name it by.")]
            public string id;
            public SiteKind kind;
            public Vector3 position;
            public float radius;
            public string name;
            public bool airborne;

            public WorldSite ToSite() => new WorldSite(id, kind, position, radius, name, airborne);
        }

        /// <summary>One settlement whose culture has an expedition profile.</summary>
        [Serializable]
        public struct SettlementEntry
        {
            [Tooltip("The settlement's id (Settlement.SettlementId): what its expedition state is kept under.")]
            public string settlementId;
            public string name;
            public Vector3 position;

            [Tooltip("False when the settlement has no spot of its profile's muster use: the muster pose below is " +
                     "then meaningless, and the settlement cannot send a band.")]
            public bool hasMuster;
            public Vector3 musterPosition;
            [Tooltip("Level direction out through the muster point, the way a departing band walks.")]
            public Vector3 musterForward;

            [Tooltip("The sum of its dwellings' beds.")]
            public int beds;
            [Tooltip("Index of its culture's profile in the ExpeditionCatalog; -1 when the catalog does not list it.")]
            public int cultureProfileIndex;
            public RosterEntry[] roster;
        }

        public SiteEntry[] sites = Array.Empty<SiteEntry>();
        public SettlementEntry[] settlements = Array.Empty<SettlementEntry>();
    }
}
