// How far a wild creature has come to trust the players, and what that looks like.
//
// Trust is won with gifts (HoardModule counts them) and spent on nothing: it only ever rises. It
// changes three things, all of them visible, so a player can read how close they are:
//   - the creature's body — an animator bool held from halfway, its head carried low;
//   - its temper — HoardModule scales the trespass it feels at its nest down by trust, so a nearly
//     tame creature barely warns and a tame one not at all;
//   - its touch — a tame creature can be petted (PettableModule asks IsTame).
//
// Server-decided, replicated as the RESULT in a NetworkVariable rather than as gift events, the same
// shape as NpcRandomLoadout: a late joiner has to see a tame creature as tame, and change events do
// not replay. Persisted by CreatureTrustSaveable.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents
{
    public class CreatureTrust : NetworkBehaviour
    {
        [Tooltip("Gifts it takes to win this creature over completely.")]
        [SerializeField, Min(1)] private int giftsToTame = 4;

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

        /// <summary>0 wild, 1 tame.</summary>
        public float Trust => trust;

        public bool IsTame => trust >= 1f;

        private void Awake() => animatorDriver = GetComponent<AgentAnimatorDriver>();

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

        private void Publish(float value)
        {
            Set(value);
            if (IsSpawned && IsServer) shared.Value = trust;
        }

        private void Set(float value)
        {
            // Snapped so the last gift reaches exactly 1 whatever the float sum of the others came to.
            trust = Mathf.Clamp01(value > 1f - 1e-3f ? 1f : value);
            if (animatorDriver != null)
                animatorDriver.SetBoolByName(trustingAnimatorBool, trust >= trustingFrom);
        }
    }
}
