// Where each prop of one generated building is, as one short per prop: the settlement rest it stands at, or
// in somebody's hands. The server decides (errands) and every machine
// shows it from the list, so a late joiner gets the current picture with the spawn and nothing is animated.
//
// Lives on the networked wrapper Settlement.Generate puts round a building that has props or gates — a loose
// scene object, never part of a building prefab, so its NetworkObject hash is the scene's own. Saved here too:
// the rests are scene content, so an index restores to the same rest as long as the settlement is not regenerated.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementPropSync : NetworkBehaviour, IPersistentEntity, ISaveable
    {
        public const short Carried = -1;
        // Written by saves from before players stopped being able to take props: that prop is gone for good, and stays
        // gone. Nothing writes it any more; it is read back as "not at any rest", like Carried.
        public const short Taken = -2;
        public const string Key = "props";       // written into save files — NEVER rename

        private readonly NetworkList<short> states = new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private SettlementProp[] props;
        private short[] decided;
        private Settlement settlement;

        public IReadOnlyList<SettlementProp> Props => EnsureProps();
        public string SaveKey => Key;

        private bool Mirrors => IsSpawned && !IsServer;
        private SettlementProps Registry
        {
            get
            {
                if (settlement == null) settlement = GetComponentInParent<Settlement>(true);
                return settlement != null ? settlement.Props : null;
            }
        }

        private void Awake() => EnsureProps();

        // Offline and on the server the decided array is the truth; a client waits for its spawn.
        private void Start()
        {
            if (Mirrors) return;
            EnsureDecided();
            ShowAll();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                EnsureDecided();
                states.Clear();
                foreach (short state in decided) states.Add(state);
                return;
            }

            states.OnListChanged += OnStatesChanged;
            ShowAll();
        }

        public override void OnNetworkDespawn() => states.OnListChanged -= OnStatesChanged;

        private void OnStatesChanged(NetworkListEvent<short> change) => ShowAll();

        public short StateOf(SettlementProp prop)
        {
            int i = System.Array.IndexOf(EnsureProps(), prop);
            if (i < 0) return Taken;
            if (Mirrors) return i < states.Count ? states[i] : HomeOf(prop);
            EnsureDecided();
            return decided[i];
        }

        /// <summary>Server only (errands go through <see cref="SettlementProps"/>).</summary>
        internal void Set(SettlementProp prop, short state)
        {
            if (!Network.Decides) return;

            int i = System.Array.IndexOf(EnsureProps(), prop);
            if (i < 0) return;
            EnsureDecided();
            decided[i] = state;
            if (IsSpawned && IsServer) states[i] = state;
            Show(i);
        }

        // ── persistence ──────────────────────────────────────────────────────────────────────────

        public struct State
        {
            public short[] rests;
        }

        public object CaptureState()
        {
            EnsureDecided();
            for (int i = 0; i < props.Length; i++)
                if (decided[i] != HomeOf(props[i])) return new State { rests = (short[])decided.Clone() };
            return null;
        }

        public void RestoreState(JObject state)
        {
            EnsureProps();
            short[] restored = state?.ToObject<State>(SaveSerializer.Serializer).rests;
            decided = new short[props.Length];
            // A prop saved in a resident's hands goes home: errand rounds restart on load, so nobody is carrying it.
            for (int i = 0; i < props.Length; i++)
                decided[i] = restored != null && i < restored.Length && restored[i] != Carried ? restored[i] : HomeOf(props[i]);

            if (IsSpawned && IsServer)
                for (int i = 0; i < props.Length && i < states.Count; i++) states[i] = decided[i];
            ShowAll();
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private SettlementProp[] EnsureProps() => props ??= GetComponentsInChildren<SettlementProp>(true);

        private void EnsureDecided()
        {
            if (decided != null && decided.Length == EnsureProps().Length) return;
            decided = new short[props.Length];
            for (int i = 0; i < props.Length; i++) decided[i] = HomeOf(props[i]);
        }

        private short HomeOf(SettlementProp prop)
        {
            SettlementProps registry = Registry;
            int rest = registry != null ? registry.IndexOf(prop.Home) : -1;
            return rest >= 0 ? (short)rest : Carried;
        }

        private void ShowAll()
        {
            for (int i = 0; i < EnsureProps().Length; i++) Show(i);
        }

        // Without a settlement (a building dropped into a scene by hand) a prop simply stands at home.
        private void Show(int i)
        {
            SettlementProp prop = props[i];
            if (prop == null) return;

            SettlementProps registry = Registry;
            if (registry == null) { prop.ShowAt(prop.Home); return; }

            short state = StateOf(prop);
            prop.ShowAt(state >= 0 ? registry.Rest(state) : null);
        }
    }
}
