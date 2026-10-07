// The barrier. One broken feature stops; the rest of the session carries on.
//
// This is a generalisation of something the codebase already does in three places, not a new idea:
// NetChannel.Dispatch runs each handler in its own try/catch so one feature's bug cannot stop the
// message carrying somebody else's damage; UnderTerrainGuard and SpawnSyncGuard both measure an
// outcome rather than enumerating causes. What was missing was a single primitive, and — because an
// asmdef cannot reference Assembly-CSharp — a place to put it that all 19 module assemblies can see.
//
// Three rules it will not break:
//
//   * It is never silent. Every fault logs an error naming the owner and the site, and lands in the
//     FaultLedger. A guard that hides the bug it caught is worse than no guard: the bug then ships.
//
//   * It never swallows anything twice over. Once a site is quarantined its body is not entered at
//     all, so a thing throwing every frame costs one log line and not sixty a second.
//
//   * It knows nothing about the game. No chat, no HUD, no player. It raises events and something in
//     Assembly-CSharp decides what a player should be told. That is what keeps this assembly free of
//     references, which is what lets every module use it.
//
// Where NOT to use it: around a single decision whose failure must abort the whole action. Damage,
// ownership transfer and spawning must refuse rather than half-happen — a half-applied change is how
// a session diverges and stays diverged. Barriers belong at fan-out points, where one caller invokes
// N independent plug-ins and the others are entitled to run.
using System;
using System.Collections;
using Unity.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Diagnostics
{
    public static class Fault
    {
        /// <summary>Faults at one site inside one window before it is switched off.</summary>
        public const int MaxFaultsPerWindow = 5;

        /// <summary>How long that window is. See <see cref="FaultBudget"/> for why it exists.</summary>
        public const float WindowSeconds = 10f;

        private static FaultBudget budget = new(MaxFaultsPerWindow, WindowSeconds);

        // Profiler marker (Diagnostics.md → Profiling): one sample per guarded body, so a capture
        // counts the barrier calls and shows the bodies nested under it.
        private const string RunMarkerName = "SpaceGame.Fault.Run";
        private static readonly ProfilerMarker RunMarker = new(RunMarkerName);

        // Instance ids of owners with at least one quarantined site. The budget is keyed by a
        // string, and building that string on every entry is an allocation per call — on the agent
        // loop, one per module per creature per frame. Only an owner listed here can be quarantined,
        // so every other owner is let in without the key ever being built.
        private static readonly HashSet<int> ownersWithQuarantine = new();

        /// <summary>Raised for every fault, quarantining or not.</summary>
        public static event Action<FaultRecord> Raised;

        /// <summary>Raised once per site, on the fault that switched it off.</summary>
        public static event Action<FaultRecord> Quarantined;

        /// <summary>
        /// Statics survive play-mode exit here, and the events hold delegates pointing at objects
        /// from the previous play session. Both are cleared, for the same reason ChatLog clears its.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForPlaySession()
        {
            budget = new FaultBudget(MaxFaultsPerWindow, WindowSeconds);
            ownersWithQuarantine.Clear();
            Raised = null;
            Quarantined = null;
            FaultLedger.Clear();
        }

        /// <summary>
        /// Runs <paramref name="body"/> behind the barrier. Returns true when it completed.
        ///
        /// <para>
        /// False means one of three things and the caller should treat them the same: the owner is
        /// gone, the site is quarantined, or the body threw. In every case the right response is to
        /// carry on with the next thing rather than to retry.
        /// </para>
        /// </summary>
        public static bool Run(Component owner, string site, Action body) =>
            body != null && Run(owner, site, ref body, InvokeAction);

        /// <summary>A body that takes its inputs and hands its outputs back through one struct.</summary>
        public delegate void RefAction<TState>(ref TState state);

        private static readonly RefAction<Action> InvokeAction = (ref Action action) => action();

        /// <summary>
        /// <see cref="Run(Component, string, Action)"/> without the per-call garbage, for barriers
        /// that run every frame per plug-in (the agent module tick).
        ///
        /// <para>
        /// A lambda that captures its inputs allocates a closure on every call. Here the inputs and
        /// the result travel in <paramref name="state"/>, so <paramref name="body"/> can be a
        /// static, capture-free delegate cached once. The key string is built only once something
        /// somewhere is quarantined, or when the body throws. Same contract and return value as
        /// the <c>Action</c> overload, which is this with the action as its state.
        /// </para>
        /// </summary>
        public static bool Run<TState>(Component owner, string site, ref TState state, RefAction<TState> body)
        {
            if (body == null || !TryEnter(owner, site)) return false;

            using ProfilerMarker.AutoScope sample = RunMarker.Auto();
            try
            {
                body(ref state);
                return true;
            }
            catch (Exception e)
            {
                Report(owner, site, e);
                return false;
            }
        }

        /// <summary>
        /// The allocation-free half of <see cref="Run"/>, for hot loops that cannot afford a closure
        /// per call: true when the caller may enter <paramref name="site"/>. The caller then runs its
        /// body in its own try/catch and hands any exception to
        /// <see cref="Report(Component, string, Exception)"/>.
        ///
        /// <para>
        /// False when the owner is gone or the site is quarantined — the same two cases in which
        /// <see cref="Run"/> does not enter its body. The site key is built only for an owner that
        /// already has a quarantined site, so the common case allocates nothing.
        /// </para>
        /// </summary>
        public static bool TryEnter(Component owner, string site)
        {
            // Unity's null: a destroyed object compares equal to null while the C# reference lives.
            // A body whose owner has gone is not a fault, it is a teardown — reporting it would fill
            // the ledger with noise on every scene unload.
            if (owner == null) return false;

            return !ownersWithQuarantine.Contains(owner.GetInstanceID())
                || !budget.IsQuarantined(Key(owner, site));
        }

        /// <summary>
        /// Counts, logs and — on the fault that exhausts the budget — quarantines a throw caught by a
        /// caller that entered through <see cref="TryEnter"/>. It is the same report <see cref="Run"/>
        /// makes, and it counts against the budget, so call it once per caught exception and never
        /// for a body that was not entered.
        /// </summary>
        public static void Report(Component owner, string site, Exception e)
        {
            // A body that destroyed its own owner and then threw: nothing is left to count against
            // or switch off, but the exception is still real and nothing else will show it.
            if (owner == null)
            {
                Debug.LogException(e);
                return;
            }

            Report(owner, site, Key(owner, site), e);
        }

        public static bool IsQuarantined(Component owner, string site) =>
            owner != null && !TryEnter(owner, site);

        /// <summary>
        /// Wraps <paramref name="body"/> so a throw inside it ends the coroutine instead of killing
        /// it where it stands.
        ///
        /// <para>
        /// This is the single highest-value use of the barrier, because an unguarded coroutine that
        /// throws does not resume, does not run its own teardown, and leaves whatever it took —
        /// the cursor, the camera, the player's input, a menu scope — taken forever. The player's
        /// only way out is quitting.
        /// </para>
        /// <para>
        /// <paramref name="onFail"/> is the teardown the routine would have run. It runs behind its
        /// own barrier, so a broken cleanup cannot re-throw out of here.
        /// </para>
        /// </summary>
        public static IEnumerator Coroutine(Component owner, string site, IEnumerator body,
                                            Action onFail = null)
        {
            if (body == null) yield break;

            while (true)
            {
                object current;

                // MoveNext is inside the try and the yield is outside it, because C# forbids
                // yielding from a try that has a catch. This shape is the reason the wrapper is a
                // loop rather than a single "yield return body".
                try
                {
                    if (!body.MoveNext()) yield break;
                    current = body.Current;
                }
                catch (Exception e)
                {
                    if (owner != null) Report(owner, site, Key(owner, site), e);
                    if (onFail != null) Run(owner, site + ".teardown", onFail);
                    yield break;
                }

                yield return current;
            }
        }

        // ------------------------------------------------------------------ internals

        // The instance id rather than the name: two creatures off the same prefab share a name, and
        // quarantining one of them must not switch off the other. Ids are unique per object and
        // stable for its life, which is exactly the scope a budget should have.
        private static string Key(Component owner, string site) =>
            $"{owner.GetInstanceID()}:{site}";

        private static void Report(Component owner, string site, string key, Exception e)
        {
            bool trips = budget.Record(key, Time.realtimeSinceStartup);

            var record = new FaultRecord(site, owner.name, e.ToString(),
                                         budget.CountFor(key), trips, Time.realtimeSinceStartup);

            FaultLedger.Add(record);

            Debug.LogError($"[Fault] {owner.name} · {site} threw (x{record.Count})" +
                           $"{(trips ? " — QUARANTINED, this feature is now off" : string.Empty)}: {e}",
                           owner);

            Raise(Raised, record);

            if (!trips) return;

            ownersWithQuarantine.Add(owner.GetInstanceID());
            Quarantine(owner);
            Raise(Quarantined, record);
        }

        private static void Quarantine(Component owner)
        {
            // A component that can shed one job keeps the others. Anything else is switched off
            // wholesale, which is the only thing that is correct for a component this code knows
            // nothing about.
            if (owner is IQuarantinable shedder)
            {
                try { shedder.OnQuarantined(); }
                catch (Exception e) { Debug.LogError($"[Fault] OnQuarantined on '{owner.name}' threw: {e}", owner); }
                return;
            }

            if (owner is Behaviour behaviour) behaviour.enabled = false;
        }

        // Subscribers are somebody else's code and are entitled to be broken too. A throwing
        // listener must not take the report down with it, or the fault that mattered is lost.
        private static void Raise(Action<FaultRecord> handlers, in FaultRecord record)
        {
            if (handlers == null) return;

            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<FaultRecord>)d)(record); }
                catch (Exception e) { Debug.LogError($"[Fault] a fault listener threw: {e}"); }
            }
        }
    }
}
