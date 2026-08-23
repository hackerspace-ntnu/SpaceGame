using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World.Weather;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the weather: the storms that are blowing, what time the weather thinks it is, and
    /// when the next storm is due.
    ///
    /// <para>
    /// A global saver rather than something on a <see cref="SaveableEntity"/>, for the same reason
    /// as <see cref="DayNightSaveable"/>: a storm is a thirty-byte record on a session-wide list, not
    /// an object in a chunk, and the schedule that produces the next one belongs to the session too.
    /// </para>
    /// <para>
    /// <b>The clock is the load-bearing half.</b> Every storm records the moment it began, and its
    /// position, intensity and wander are all functions of how long ago that was. Both clocks
    /// underneath this project restart at zero every session, so saving the records alone would
    /// restore a set of storms that all began in the future. <see cref="StormClock"/> answers that
    /// the way <c>DayNightCycle</c> answers the same question for the sun — an anchor saying which
    /// reading of the shared clock counts as now — and this saver stores one number for it.
    /// </para>
    /// <para>
    /// Place it on the same GameObject as the <c>SandstormManager</c> (which is the
    /// <c>NetworkGameManager</c>'s object, by that class's own instruction).
    /// </para>
    /// </summary>
    public class SandstormSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "weather";       // written into save files — NEVER rename

        public string SaveKey => Key;

        [Tooltip("Optional. Left empty, the director on this GameObject is used, then the first one " +
                 "in the loaded scenes. A world with no director simply saves no schedule.")]
        [SerializeField] private SandstormDirector director;

        /// <summary>
        /// One storm, in a form a file can hold.
        ///
        /// Deliberately not <see cref="StormInstance"/> itself. That struct carries a
        /// <c>Vector2</c>, and this project's JSON layer walks a Unity vector's derived properties
        /// (<c>normalized</c>, and from there round and round) into a StackOverflowException unless
        /// it is handed a converter. Two floats cost nothing and cannot do that.
        /// </summary>
        public struct StormRecord
        {
            public int id;
            public byte profileIndex;
            public uint seed;
            public float originX;
            public float originZ;
            public float headingDegrees;
            public double startTime;
            public float duration;
        }

        public struct State
        {
            /// <summary>What the weather clock read when this was written.</summary>
            public double clock;

            public StormRecord[] storms;

            /// <summary>Where the id counter had got to. Restored storms must not collide with new ones.</summary>
            public int nextStormId;

            public float directorCountdown;
            public int directorActiveStormId;
            public bool hasDirector;
        }

        public object CaptureState()
        {
            IReadOnlyList<StormInstance> live = Sandstorms.Records;
            var storms = new StormRecord[live.Count];

            for (int i = 0; i < live.Count; i++)
            {
                StormInstance storm = live[i];
                storms[i] = new StormRecord
                {
                    id = storm.Id,
                    profileIndex = storm.ProfileIndex,
                    seed = storm.Seed,
                    originX = storm.Origin.x,
                    originZ = storm.Origin.y,
                    headingDegrees = storm.HeadingDegrees,
                    startTime = storm.StartTime,
                    duration = storm.Duration,
                };
            }

            SandstormDirector d = Resolve();

            return new State
            {
                clock = Sandstorms.WeatherTime,
                storms = storms,
                nextStormId = Sandstorms.NextId,
                directorCountdown = d != null ? d.Countdown : 0f,
                directorActiveStormId = d != null ? d.ActiveStormId : 0,
                hasDirector = d != null,
            };
        }

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            // The clock first, and it has to be. Every StartTime below is a reading of it, and the
            // manager resolves storms the moment its records change — against whatever the clock
            // says at that instant.
            Sandstorms.RestoreClock(restored.clock);

            var storms = new List<StormInstance>();

            if (restored.storms != null)
            {
                for (int i = 0; i < restored.storms.Length; i++)
                {
                    StormRecord record = restored.storms[i];
                    storms.Add(new StormInstance
                    {
                        Id = record.id,
                        ProfileIndex = record.profileIndex,
                        Seed = record.seed,
                        Origin = new Vector2(record.originX, record.originZ),
                        HeadingDegrees = record.headingDegrees,
                        StartTime = record.startTime,
                        Duration = record.duration,
                    });
                }
            }

            Sandstorms.RestoreStorms(storms, restored.nextStormId);

            if (!restored.hasDirector) return;

            SandstormDirector d = Resolve();
            if (d != null) d.RestoreSchedule(restored.directorCountdown, restored.directorActiveStormId);
        }

        /// <summary>
        /// The director this adapter speaks for.
        ///
        /// Resolved on demand rather than in Awake, because <see cref="SaveManager"/> applies staged
        /// state the moment this saver registers — inside OnEnable — and a reference filled in later
        /// would make that restore silently do nothing.
        /// </summary>
        private SandstormDirector Resolve()
        {
            if (director != null) return director;

            director = GetComponent<SandstormDirector>();
            if (director == null) director = FindFirstObjectByType<SandstormDirector>();

            return director;
        }

        private void OnEnable()
        {
            // Before registering, never after. StormClock is static and outlives a scene load, so a
            // quickload — or the menu and then a DIFFERENT world — would inherit the last world's
            // weather time. Resetting here and registering on the next line means a restore that
            // arrives during registration still wins, and a world with no saved weather starts at
            // zero rather than at whatever the previous session had reached.
            Sandstorms.ResetClock();

            SaveManager.RegisterGlobalSaver(this);
        }

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
