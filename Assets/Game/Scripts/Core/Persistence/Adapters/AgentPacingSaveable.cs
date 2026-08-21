using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the per-agent phase offset that keeps a crowd from marching in step.
    ///
    /// <c>AgentController</c> modulates its speed by a slow sine, and the phase of that sine is
    /// randomised per agent in <c>Awake</c> — which is what makes a group of NPCs read as individuals
    /// walking together rather than as one animation played several times. A load re-rolls it for
    /// everybody at once, and a re-roll is not the same as a fresh roll: every agent samples its sine
    /// against the same <c>Time.time</c>, so a group that had drifted into a comfortable stagger
    /// briefly moves as one before spreading out again.
    ///
    /// One float, and the smallest thing on this list — kept because it is also nearly free, and
    /// because the artefact it removes is one of the few that is visible across a whole crowd at once
    /// rather than on one creature.
    ///
    /// <b>What is not here.</b> <c>AgentController.simulating</c> is a one-frame cache of an answer
    /// <c>RefreshAuthority</c> re-derives every Update, and the question it answers — does THIS
    /// machine own this agent — is about the session's network topology, which a load does not carry
    /// over. Restoring a previous session's answer would be restoring a stale reading of a different
    /// world; the field's <c>true</c> default is already the right starting assumption and the first
    /// Update reconciles it.
    ///
    /// <c>AgentController.enabled</c> is not here either, though it IS durable state: it is written by
    /// <c>HealthReactionModule</c>, on the death path and on the threshold path, and both are restored
    /// by <see cref="HealthReactionSaveable"/> — from the module that asserts it, rather than from a
    /// second saver free to disagree.
    /// </summary>
    [RequireComponent(typeof(AgentController))]
    public class AgentPacingSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "agentPacing";

        private AgentController controller;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private AgentController Controller =>
            controller != null ? controller : controller = GetComponent<AgentController>();

        public string SaveKey => Key;

        public struct State
        {
            public float speedVariationPhase;
        }

        public object CaptureState()
        {
            if (Controller == null) return null;

            return new State { speedVariationPhase = Controller.SpeedVariationPhase };
        }

        public void RestoreState(JObject state)
        {
            if (Controller == null) return;

            // No record: leave the phase Awake rolled. A synthetic zero here would be worse than the
            // random one, because zero is the SAME value for every agent — the exact artefact this
            // saver exists to avoid.
            if (state == null) return;

            Controller.RestoreSpeedVariationPhase(
                state.ToObject<State>(SaveSerializer.Serializer).speedVariationPhase);
        }
    }
}
