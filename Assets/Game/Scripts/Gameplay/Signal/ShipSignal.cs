using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The voice the lander's long-range transmitter picks up the moment a working one is fitted: a
    /// looped call on the open band from the nearest settlement that will have the crew, which the
    /// terminal's COMMS page plays, the map table charts and the "Answer the signal" objective walks
    /// them to.
    ///
    /// <para>
    /// <b>Server decides, every machine presents.</b> The server chooses the destination once, from the
    /// world's baked town list (<see cref="WorldSiteCatalog.towns"/>) by <see cref="SignalDestinationRule"/>,
    /// measured from where the hull actually came to rest — never a hard-coded spawn — and writes it to a
    /// <see cref="NetworkVariable{T}"/> a late joiner reads on spawn. Every machine then charts it on the
    /// map from that value. The choice is saved (<c>ShipSignalSaveable</c>), so a reload never chooses
    /// again and two machines never disagree.
    /// </para>
    /// <para>
    /// <b>"Working" is the rack's word, not this component's.</b> The transmitter socket counts only once
    /// a working unit is fitted in it: the burnt-out unit the hull lands with is the rack's broken mask,
    /// which is not fitted (<see cref="ShipPartRack.IsBroken"/>).
    /// </para>
    /// <para>
    /// On the ship ROOT beside <see cref="ShipPartRack"/>, inheriting the hull's NetworkObject, the way
    /// <c>ShipPartFire</c> does.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipPartRack))]
    public sealed class ShipSignal : NetworkBehaviour
    {
        [Header("Destination")]
        [Tooltip("Who the crew are, for the relationship table's question \"would this town shoot them?\".")]
        [SerializeField] private FactionDefinition crewFaction;

        [Tooltip("The table hostility is read from — the one every agent in the world acts on.")]
        [SerializeField] private FactionRelationshipTable relationships;

        [Tooltip("A settlement whose centre is closer to the hull than this, metres, is the one the wreck came " +
                 "down beside, and the signal skips it for the next nearest.")]
        [SerializeField, Min(0f)] private float minimumDistance = 300f;

        [Header("What it says")]
        [Tooltip("The intercepted transmission, as the COMMS page prints it. One looped call from whoever is out " +
                 "there; nothing in it may name a place or a people, because which town answers is decided " +
                 "per world.")]
        [SerializeField, TextArea(3, 8)] private string transcript =
            "...any station, any station on this band. If you came down in that fireball, you are not alone " +
            "out here. We have water. We have shelter. Follow this carrier and walk to us. Repeating...";

        [Tooltip("The destination's label on the map table and the personal map.")]
        [SerializeField] private string mapLabel = "SIGNAL";

        private readonly NetworkVariable<SignalDestination> networkDestination = new();

        private SignalDestination destination;
        private ShipPartRack rack;
        private int transmitterSocket = -2;
        private bool spawned;
        private bool missingCatalogReported;
        private string chartedId;

        /// <summary>What the transmitter heard; <c>default</c> until a working one has listened.</summary>
        public SignalDestination Destination => destination;

        public string Transcript => transcript;

        /// <summary>Raised on this machine whenever <see cref="Destination"/> changes.</summary>
        public event Action Changed;

        private ShipPartRack Rack => rack != null ? rack : rack = GetComponent<ShipPartRack>();

        /// <summary>A working long-range transmitter is fitted in this hull's socket.</summary>
        public bool TransmitterWorking => IsTransmitterFitted(Rack, ref transmitterSocket);

        /// <summary>A working long-range transmitter is fitted in <paramref name="ship"/>.</summary>
        public static bool IsTransmitterFitted(ShipPartRack ship)
        {
            int socket = -2;
            return IsTransmitterFitted(ship, ref socket);
        }

        /// <summary>
        /// Looked up once and cached in <paramref name="socket"/> (-2 unresolved, -1 none): a rack's sockets
        /// are fixed by the prefab. A broken unit is not fitted, so it never answers true here.
        /// </summary>
        private static bool IsTransmitterFitted(ShipPartRack ship, ref int socket)
        {
            if (ship == null) return false;

            if (socket == -2)
            {
                socket = -1;
                for (int i = 0; i < ship.Sockets.Count; i++)
                    if (ship.Sockets[i] != null && ship.Sockets[i].Kind == ShipPartKind.Transmitter)
                    {
                        socket = i;
                        break;
                    }
            }

            return socket >= 0 && ship.IsInstalled(socket);
        }

        // ── Netcode lifecycle ────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkDestination.OnValueChanged += OnNetworkChanged;

            if (IsServer) Publish();
            else Adopt(networkDestination.Value);
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkDestination.OnValueChanged -= OnNetworkChanged;
        }

        private void OnNetworkChanged(SignalDestination previous, SignalDestination current) => Adopt(current);

        private void Adopt(SignalDestination value)
        {
            if (Network.Simulates(this)) return;
            Apply(value);
        }

        // ── Server ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (Network.Simulates(this) && !destination.Received && TransmitterWorking && ArrivalDirector.CrewHasLanded)
                Listen();

            Chart();
        }

        /// <summary>SERVER: the transmitter has just started working. Choose where its signal leads, once.</summary>
        private void Listen()
        {
            WorldStreamer streamer = FindFirstObjectByType<WorldStreamer>();
            WorldSiteCatalog catalog = streamer != null && streamer.Config != null ? streamer.Config.siteCatalog : null;
            if (catalog == null)
            {
                // Not "nowhere": that would be saved, and this world would never hear a signal even once the
                // catalog is baked. Stay unheard, loudly, and try again every frame.
                if (!missingCatalogReported)
                    Debug.LogError("[ShipSignal] No baked site catalog on the world's streaming config, so the " +
                                   "transmitter has no towns to hear. Run Tools/SpaceGame/World/Bake Site Catalog.", this);
                missingCatalogReported = true;
                return;
            }

            Vector3 from = transform.position;
            if (SignalDestinationRule.TryChoose(catalog.towns, from, minimumDistance, crewFaction, relationships,
                                                out WorldSiteCatalog.TownEntry town))
            {
                Debug.Log($"[ShipSignal] Transmitter online at ({from.x:0}, {from.z:0}): the signal leads to " +
                          $"{SignalDestinationRule.Describe(town)}, " +
                          $"{SignalDestinationRule.FlatDistance(from, town.position):0} m at " +
                          $"{SignalDestinationRule.Bearing(from, town.position):0}°.", this);
                Set(SignalDestinationRule.ToDestination(town));
                return;
            }

            Debug.LogWarning($"[ShipSignal] Transmitter online at ({from.x:0}, {from.z:0}), but none of the " +
                             $"{catalog.towns.Length} baked town(s) is a fixed, friendly settlement further than " +
                             $"{minimumDistance:0} m: the signal leads nowhere.", this);
            Set(SignalDestination.Nowhere);
        }

        /// <summary>The save system's way in: the record, wholesale. Authority only.</summary>
        public void Restore(SignalDestination restored)
        {
            if (!Network.Simulates(this)) return;
            Set(restored);
        }

        private void Set(SignalDestination value)
        {
            if (value.Equals(destination)) return;
            Apply(value);
            Publish();
        }

        private void Publish()
        {
            if (spawned && IsServer) networkDestination.Value = destination;
        }

        // ── Every machine ────────────────────────────────────────────────────

        private void Apply(SignalDestination value)
        {
            if (value.Equals(destination)) return;
            destination = value;
            Changed?.Invoke();
        }

        /// <summary>
        /// Puts the destination on this machine's map, once per destination. Retried every frame until the
        /// map service exists: it lives in <c>persistentScene</c>, which may wake after the hull. The marker
        /// is a POI under a stable id, so the map's own save and a reload never chart it twice.
        /// </summary>
        private void Chart()
        {
            if (!destination.HasDestination || chartedId == destination.Id) return;

            MapService map = MapService.Instance;
            if (map == null) return;

            map.RegisterPOI(MapPoiId(destination.Id), destination.Position, MapMarkerType.Quest, mapLabel,
                            requiresRevealedChunk: false);
            chartedId = destination.Id;
        }

        /// <summary>The map POI id a destination is charted under.</summary>
        public static string MapPoiId(string townId) => "signal:" + townId;
    }
}
