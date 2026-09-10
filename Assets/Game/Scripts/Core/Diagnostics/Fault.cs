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
        public static bool Run(Component owner, string site, Action body)
        {
            if (body == null) return false;

            // Unity's null: a destroyed object compares equal to null while the C# reference lives.
            // A body whose owner has gone is not a fault, it is a teardown — reporting it would fill
            // the ledger with noise on every scene unload.
            if (owner == null) return false;

            string key = Key(owner, site);
            if (budget.IsQuarantined(key)) return false;

            try
            {
                body();
                return true;
            }
            catch (Exception e)
            {
                Report(owner, site, key, e);
                return false;
            }
        }

        public static bool IsQuarantined(Component owner, string site) =>
            owner != null && budget.IsQuarantined(Key(owner, site));

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
