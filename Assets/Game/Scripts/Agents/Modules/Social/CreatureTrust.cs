// How far a wild creature has come to trust the players, and what that looks like.
//
// Trust is won and spent on nothing: it only ever rises. Two ways in, tuned per creature:
//   - gifts — HoardModule counts the items it is brought (giftsToTame);
//   - pets  — every pet the server accepts (PettableModule's Petted) counts (petsToTame). 0 means
//             petting earns nothing, and then a creature that has to be won over lets nobody touch
//             it until gifts have tamed it.
// It changes what a player can see and do, so they can read how close they are:
//   - the creature's body — an animator bool held from trustingFrom, its head carried low;
//   - its temper — HoardModule scales the trespass it feels at its nest down by trust;
//   - its touch — PettableModule asks AcceptsPetting;
//   - its back — SaddleSocket refuses a saddle, and SaddlePlacement says why, until IsTame.
//
// Server-decided, replicated as the RESULT in a NetworkVariable rather than as gift or pet events,
// the same shape as NpcRandomLoadout: a late joiner has to see a tame creature as tame, and change
// events do not replay. Persisted by CreatureTrustSaveable.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class CreatureTrust : NetworkBehaviour
    {
        [Tooltip("Gifts it takes to win this creature over completely.")]
        [SerializeField, Min(1)] private int giftsToTame = 4;

        [Tooltip("Pets it takes to win this creature over completely. Each one waits out the PettableModule's " +
                 "cooldown, so this is also roughly how many reaction animations the player watches. 0: petting " +
                 "earns nothing, and the creature lets nobody pet it until it is tame.")]
        [SerializeField, Min(0)] private int petsToTame;

        [Tooltip("The head trigger that is petted. Found among the children when empty.")]
        [SerializeField] private PettableModule pettable;

        [Tooltip("Animator bool held once trust reaches trustingFrom: the body language of an animal " +
                 "that has decided you are not a threat. Empty for none.")]
        [SerializeField] private string trustingAnimatorBool = "Trusting";

        [Tooltip("Trust at which the body language changes, 0–1.")]
        [SerializeField, Range(0f, 1f)] private float trustingFrom = 0.5f;

        // Server-written, everyone-read. Mirrored into `trust` on clients.
        private readonly NetworkVariable<float> shared = new(
            writePerm: NetworkVariableWritePermission.Server,
            readPerm: NetworkVariableReadPermission.Everyone);

        private float trust;
        private AgentAnimatorDriver animatorDriver;

        /// <summary>Raised on every machine whenever trust changes, live or restored.</summary>
        public event Action Changed;

        /// <summary>0 wild, 1 tame.</summary>
        public float Trust => trust;

        public bool IsTame => trust >= 1f;

        /// <summary>Whether it lets anybody pet it yet: always when petting is how it is won, else once tame.</summary>
        public bool AcceptsPetting => petsToTame > 0 || IsTame;

        public int PetsToTame => petsToTame;

        /// <summary>Trust expressed as pets, for a readout. 0 for a creature that is not won by petting.</summary>
        public int PetsWon => Mathf.RoundToInt(trust * petsToTame);

        private void Awake()
        {
            animatorDriver = GetComponent<AgentAnimatorDriver>();
            if (pettable == null) pettable = GetComponentInChildren<PettableModule>(true);
            if (pettable == null && petsToTame > 0)
                Debug.LogError($"[CreatureTrust] '{name}' is won by petting but has no PettableModule.", this);
        }

        private void OnEnable()
        {
            if (pettable != null) pettable.Petted += OnPetted;
        }

        private void OnDisable()
        {
            if (pettable != null) pettable.Petted -= OnPetted;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                shared.Value = trust;
                return;
            }

            // Read once here as well as subscribing: a late joiner gets the current value with the
            // spawn and never sees a change event for it.
            shared.OnValueChanged += Mirror;
            Set(shared.Value);
        }

        public override void OnNetworkDespawn() => shared.OnValueChanged -= Mirror;

        private void Mirror(float previous, float next) => Set(next);

        /// <summary>Authority only: one gift accepted.</summary>
        public void Gift() => Publish(trust + 1f / giftsToTame);

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void Restore(float value) => Publish(value);

        // Raised on every machine; counted once, where the creature is decided.
        private void OnPetted(GameObject petter)
        {
            if (petsToTame == 0 || IsTame || !Network.Owns(this)) return;
            Publish(trust + 1f / petsToTame);
        }

        private void Publish(float value)
        {
            Set(value);
            if (IsSpawned && IsServer) shared.Value = trust;
        }

        private void Set(float value)
        {
            // Snapped so the last gift or pet reaches exactly 1 whatever the float sum of the others came to.
            float next = Mathf.Clamp01(value > 1f - 1e-3f ? 1f : value);
            if (next == trust) return;

            trust = next;
            if (animatorDriver != null)
                animatorDriver.SetBoolByName(trustingAnimatorBool, trust >= trustingFrom);
            Changed?.Invoke();
        }
    }
}
