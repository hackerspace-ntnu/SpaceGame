// An animal that has to be won over before it lets anybody on its back.
//
// Trust is counted in pets: every pet the server accepts (PettableModule's Petted, raised on every machine) is
// counted on the machine that owns the creature, and the count is announced as absolute state (NetMsg.TrustSet), so
// a late joiner and a machine that missed one message both end up agreeing. Once trusting it stays trusting.
//
// What trust gates is the saddle: SaddleSocket refuses to fit one, and SaddlePlacement says why, until IsTrusting.
// A creature without this component is unaffected — every other saddled animal in the game fits one straight away.
// Saved by CreatureTrustSaveable: nothing in the world could tell a trusting animal from a wary one after a load.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class CreatureTrust : MonoBehaviour
    {
        [Tooltip("Pets it takes before the animal lets a saddle on. Each one waits out the PettableModule's cooldown, " +
                 "so this is also roughly how many reaction animations the player watches.")]
        [SerializeField, Min(1)] private int petsToTrust = 3;

        [Tooltip("The head trigger that is petted. Found among the children when empty.")]
        [SerializeField] private PettableModule pettable;

        private int pets;

        /// <summary>Raised on every machine whenever the count changes, live or restored.</summary>
        public event Action Changed;

        public int Pets => pets;
        public int PetsToTrust => petsToTrust;
        public bool IsTrusting => pets >= petsToTrust;
        public float Trust01 => Mathf.Clamp01((float)pets / petsToTrust);

        private void Awake()
        {
            if (pettable == null) pettable = GetComponentInChildren<PettableModule>(true);
            if (pettable == null)
                Debug.LogError($"[CreatureTrust] '{name}' has no PettableModule, so it can never come to trust anybody.", this);
        }

        private void OnEnable()
        {
            if (pettable != null) pettable.Petted += OnPetted;
            this.NetOn(NetMsg.TrustSet, OnTrustSet);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback += OnPeerJoined;
        }

        private void OnDisable()
        {
            if (pettable != null) pettable.Petted -= OnPetted;
            this.NetOff(NetMsg.TrustSet, OnTrustSet);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback -= OnPeerJoined;
        }

        /// <summary>Restore-only: the count a save recorded. Called on the machine that loads, the server.</summary>
        public void Restore(int saved) => Apply(saved);

        private void OnPetted(GameObject petter)
        {
            if (!Network.Owns(this) || IsTrusting) return;

            Apply(pets + 1);
            Announce();
        }

        private void OnTrustSet(in NetArg arg, ulong sender) => Apply(arg.A);

        private void OnPeerJoined(ulong clientId)
        {
            if (pets > 0 && Network.Owns(this) && clientId != NetworkManager.ServerClientId) Announce();
        }

        private void Announce() => this.NetToAll(NetMsg.TrustSet, new NetArg { A = pets });

        private void Apply(int value)
        {
            value = Mathf.Clamp(value, 0, petsToTrust);
            if (value == pets) return;

            pets = value;
            Changed?.Invoke();
        }
    }
}
