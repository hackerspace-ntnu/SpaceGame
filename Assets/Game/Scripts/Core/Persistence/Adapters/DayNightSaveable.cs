using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what hour the world is at.
    ///
    /// A global saver rather than something hanging off a <see cref="SaveableEntity"/>, for the
    /// same reason as <see cref="GameStateSaveable"/>: the time of day describes the session, not
    /// an object in it, so it has to be written even when no chunk is loaded.
    ///
    /// One float is all there is to store. <see cref="DayNightCycle"/> derives the hour from a
    /// clock shared by every machine, so restoring a world is not a matter of replaying elapsed
    /// time — it is re-stating which reading of that clock counts as this hour, which is what
    /// <see cref="DayNightCycle.RestoreTimeOfDay"/> does.
    ///
    /// Place it on the same GameObject as the cycle it saves (the Sun), where it finds it for free.
    /// </summary>
    public class DayNightSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "sky";

        public string SaveKey => Key;

        [Tooltip("Optional. Left empty the cycle on this GameObject is used, then the first one in " +
                 "the loaded scenes.")]
        [SerializeField] private DayNightCycle cycle;

        public struct State
        {
            public float timeOfDay;
        }

        public object CaptureState()
        {
            DayNightCycle sky = Resolve();

            // Null stores nothing, which is the honest answer for a world with no sun in it —
            // better than writing a zero that a later load would read back as midnight.
            return sky == null ? null : new State { timeOfDay = sky.TimeOfDay };
        }

        public void RestoreState(JObject state)
        {
            DayNightCycle sky = Resolve();
            if (sky == null || state == null) return;

            if (state["timeOfDay"] is { Type: JTokenType.Float or JTokenType.Integer } hour)
                sky.RestoreTimeOfDay(hour.Value<float>());
        }

        /// <summary>
        /// The cycle this adapter speaks for.
        /// <para>
        /// Resolved on demand rather than in Awake because <see cref="SaveManager"/> applies state
        /// that a load staged earlier the moment this saver registers — inside OnEnable — and a
        /// reference that was only going to be filled in later would make that restore silently do
        /// nothing.
        /// </para>
        /// </summary>
        private DayNightCycle Resolve()
        {
            if (cycle != null) return cycle;

            cycle = GetComponent<DayNightCycle>();
            if (cycle == null) cycle = FindFirstObjectByType<DayNightCycle>();

            return cycle;
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
