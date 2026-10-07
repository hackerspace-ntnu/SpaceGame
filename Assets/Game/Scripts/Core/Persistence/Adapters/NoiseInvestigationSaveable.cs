using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a noise an agent was walking toward — a gunshot, an explosion, a footstep.
    ///
    /// A noise is an event with no source left in the world: <c>NoiseEmitter</c> fires once and the
    /// sound is gone, so a guard who reloads mid-investigation has nothing to rediscover and simply
    /// stops. That reads as the world forgetting the shot you just fired, which is exactly the state
    /// a player is most likely to save in.
    ///
    /// <b>Only the investigation branch.</b> The aggro branch of <c>OnNoiseHeard</c> hands the
    /// instigator to <c>AgentTargeting</c> and clears the investigation, and
    /// <see cref="AgentStateSaveable"/> owns the target.
    /// </summary>
    [RequireComponent(typeof(NoiseReceiverModule))]
    public class NoiseInvestigationSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "noise";           // written into save files — NEVER rename

        private NoiseReceiverModule receiver;

        private NoiseReceiverModule Receiver =>
            receiver != null ? receiver : receiver = GetComponent<NoiseReceiverModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool isInvestigating;
            public Vector3 investigatePosition;
            public float investigateTimer;
        }

        public object CaptureState()
        {
            if (Receiver == null || !Receiver.IsInvestigating) return null;

            return new State
            {
                isInvestigating = true,
                investigatePosition = Receiver.InvestigatePosition,
                investigateTimer = Receiver.InvestigateTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Receiver == null) return;

            if (state == null)
            {
                Receiver.RestoreInvestigation(false, Vector3.zero, 0f);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Receiver.RestoreInvestigation(restored.isInvestigating, restored.investigatePosition,
                                          restored.investigateTimer);
        }
    }
}
