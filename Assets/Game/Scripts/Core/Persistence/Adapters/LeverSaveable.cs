using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a lever pulled.
    ///
    /// <para>
    /// A lever is worth more than it looks: its whole point is the <c>onPulled</c> event, which
    /// enables hidden doors, unlocks gates and reveals bridges. Losing the pull on load does not
    /// merely stand the handle back up — it puts the player in a world where the bridge they built
    /// is gone and the lever that built it is armed again.
    /// </para>
    /// <para>
    /// Restoring through <see cref="LeverInteraction.RestorePulled"/> rather than by posing the
    /// handle is what makes a one-shot lever stay spent: the state goes into the latch, whose
    /// one-way rule then refuses every further press. Whether the event runs again is decided by
    /// the lever's own <c>replayOnJoin</c>, which already answers exactly this question for a late
    /// joiner — state events replay, one-shot effects (a cutscene, a portal) do not.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LeverInteraction))]
    public class LeverSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "lever";       // written into save files — NEVER rename

        private LeverInteraction lever;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private LeverInteraction Lever =>
            lever != null ? lever : lever = GetComponent<LeverInteraction>();

        public string SaveKey => Key;

        public struct State
        {
            public bool pulled;
        }

        public object CaptureState()
        {
            if (Lever == null) return null;

            return Lever.IsPulled ? new State { pulled = true } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Lever == null) return;

            if (state == null) { Lever.RestorePulled(false); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Lever.RestorePulled(restored.pulled);
        }
    }
}
