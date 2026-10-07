using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists an alert an ally raised: where it said to look, and how long is left to look there.
    ///
    /// Without it a squad that was converging on a reported position goes idle the moment the world
    /// reloads — the alert is a one-shot push from <c>AlertBroadcaster</c>, so nothing re-sends it and
    /// the receivers have no way to rediscover what they were told. The moment a save is most likely
    /// to be taken is also the moment this state is most likely to be interesting: somebody just
    /// spotted the player.
    ///
    /// <b>The alerted target itself is not stored here.</b> <c>ReceiveAlert</c> hands it straight to
    /// <c>AgentTargeting</c>, and <see cref="AgentStateSaveable"/> owns that key. Storing it twice
    /// would be two answers to one question.
    /// </summary>
    [RequireComponent(typeof(AlertReceiverModule))]
    public class AlertResponseSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "alert";           // written into save files — NEVER rename

        private AlertReceiverModule receiver;

        private AlertReceiverModule Receiver =>
            receiver != null ? receiver : receiver = GetComponent<AlertReceiverModule>();

        public string SaveKey => Key;

        public struct State
        {
            public Vector3 alertPosition;

            /// <summary>Seconds of investigation left. Zero is the same as no alert.</summary>
            public float alertTimer;
        }

        public object CaptureState()
        {
            if (Receiver == null || Receiver.AlertTimer <= 0f) return null;

            return new State
            {
                alertPosition = Receiver.AlertPosition,
                alertTimer = Receiver.AlertTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Receiver == null) return;

            if (state == null)
            {
                Receiver.RestoreAlert(Vector3.zero, 0f);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Receiver.RestoreAlert(restored.alertPosition, restored.alertTimer);
        }
    }
}
