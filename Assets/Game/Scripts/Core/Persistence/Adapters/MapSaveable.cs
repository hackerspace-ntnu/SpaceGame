using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the explored map: which chunks have been uncovered, and which points of interest
    /// have been found.
    ///
    /// <para>
    /// Exploration is the one kind of progress in this game that has no object to hang off. It is
    /// not a thing in the world, it is a fact about the player's relationship to the world — so this
    /// is a global saver, like the clock and the game timer, written even when no chunk is loaded.
    /// </para>
    /// <para>
    /// <b>POIs are stored, not just their discovery flags.</b> A marker only exists while something
    /// registered it, and a <c>MapPOI</c> registers when its chunk streams in. Storing only "the
    /// wreck was found" would put the map back with nothing to be found ON it until the player
    /// walked into that chunk again — which is precisely the corner of the map they no longer need
    /// to visit. Re-registering under the same id is free: <c>MapService.RegisterPOI</c> is
    /// idempotent by id, so the live component and the record cannot produce two markers.
    /// </para>
    /// <para>
    /// Markers that FOLLOW a transform are deliberately not stored. They describe something that is
    /// currently there — a creature, a vehicle — and whatever registered them registers them again;
    /// a saved copy could only ever be a second, stale marker for the same thing.
    /// </para>
    /// <para>
    /// One caveat worth stating: the revealed set is what THIS machine's player has uncovered, and
    /// only the server writes save files, so in a hosted session it is the host's map that is kept.
    /// Per-player exploration would need the record moved into <c>PlayerSaveService</c>, which is a
    /// bigger change than the one this bug asked for.
    /// </para>
    /// </summary>
    public class MapSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "map";       // written into save files — NEVER rename

        [Tooltip("Optional. Left empty, the service on this GameObject is used, then the live one.")]
        [SerializeField] private MapService service;

        public string SaveKey => Key;

        public struct PoiState
        {
            public string id;
            public Vector3 position;
            public MapMarkerType type;
            public string label;
            public bool requiresRevealedChunk;
            public float discoveryRadius;
            public bool discovered;
        }

        public struct State
        {
            /// <summary>Grid coordinates of every chunk uncovered so far.</summary>
            public List<Vector2Int> revealed;

            public List<PoiState> pois;
        }

        public object CaptureState()
        {
            MapService map = Resolve();
            if (map == null) return null;

            var revealed = new List<Vector2Int>(map.RevealedChunks);
            var pois = new List<PoiState>(map.POIs.Count);

            foreach (KeyValuePair<string, MapService.Marker> entry in map.POIs)
            {
                MapService.Marker marker = entry.Value;
                if (marker == null) continue;

                pois.Add(new PoiState
                {
                    id = entry.Key,
                    position = marker.GetWorldPosition(),
                    type = marker.type,
                    label = marker.label,
                    requiresRevealedChunk = marker.requiresRevealedChunk,
                    discoveryRadius = marker.discoveryRadius,
                    discovered = marker.discovered,
                });
            }

            // An untouched map stores nothing, which keeps the key out of a save written from the
            // main menu or from a world with no map service in it.
            if (revealed.Count == 0 && pois.Count == 0) return null;

            return new State { revealed = revealed, pois = pois };
        }

        public void RestoreState(JObject state)
        {
            MapService map = Resolve();
            if (map == null) return;

            if (state == null)
            {
                // An unexplored world, said out loud. The map service lives in the persistent scene
                // and outlives a world change, so leaving the set alone would carry the previous
                // world's explored chunks onto this one's map.
                map.RestoreRevealedChunks(null);
                map.RestoreNothingDiscovered();
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            map.RestoreRevealedChunks(restored.revealed);

            if (restored.pois == null) { map.RestoreNothingDiscovered(); return; }

            foreach (PoiState poi in restored.pois)
            {
                map.RestorePOI(poi.id, poi.position, poi.type, poi.label,
                               poi.requiresRevealedChunk, poi.discoveryRadius, poi.discovered);
            }
        }

        /// <summary>
        /// The service this adapter speaks for.
        ///
        /// Resolved on demand rather than in Awake, for the reason <see cref="DayNightSaveable"/>
        /// gives: <see cref="SaveManager"/> applies state the moment a saver registers, inside
        /// OnEnable, and a reference filled in later would make that restore silently do nothing.
        /// <c>MapService.Instance</c> is a static singleton cleared in its own OnDestroy, so it is
        /// asked rather than cached.
        /// </summary>
        private MapService Resolve()
        {
            if (service != null) return service;

            service = GetComponent<MapService>();
            if (service == null) service = MapService.Instance;
            if (service == null) service = FindFirstObjectByType<MapService>();

            return service;
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
