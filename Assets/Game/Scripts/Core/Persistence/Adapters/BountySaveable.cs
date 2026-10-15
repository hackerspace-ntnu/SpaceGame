using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.World.Bounties;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the crew's bounty book: the hunt they have open (where it was taken, where its site is, how far they
    /// are), the server's run of it (its cast, its clock), and every hunt that has ended and how.
    ///
    /// <para>
    /// A global saver beside <see cref="ObjectiveSaveable"/>: the book belongs to the world, not to any object in it.
    /// <b>Ids, never catalog places</b>, so adding a hunt never moves an old save onto another. The cast are their own
    /// saved entities, named here by identity and found again by the board as their chunks load.
    /// </para>
    /// <para>
    /// Saves written before hunts had a cast still load: their <c>claimed</c> ids become Claimed closures, their
    /// <c>target</c> one Target piece, and <c>targetSpawned</c> the run's <c>spawned</c>.
    /// </para>
    /// </summary>
    public class BountySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "bounties";   // written into save files — NEVER rename

        [Tooltip("Optional. Left empty, the board on this GameObject is used, then the live one.")]
        [SerializeField] private BountyBoard board;

        public string SaveKey => Key;

        /// <summary>One ended hunt: its id, and <see cref="BountyClosure"/> by name.</summary>
        public struct ClosedEntry
        {
            public string id;
            public string closure;
        }

        public struct State
        {
            /// <summary>The open hunt's id; null with none open.</summary>
            public string open;

            /// <summary><see cref="BountyPhase"/> by name.</summary>
            public string phase;

            public Vector3 town;
            public float townRadius;

            /// <summary>Whether <see cref="site"/> was written. A flag rather than a <c>Vector3?</c>: the save serializer converts Vector3, not its nullable.</summary>
            public bool hasSite;

            public Vector3 site;

            /// <summary>The server's run of the open hunt, and the cast of ended ones still waiting to be taken away.</summary>
            public BountyHuntRunState run;

            /// <summary>Every hunt that has ended, in the order they ended.</summary>
            public List<ClosedEntry> closed;
        }

        /// <summary>The fields of a save written before hunts had a cast. Read, never written.</summary>
        private struct OldState
        {
            public SaveRef target;
            public bool targetSpawned;
            public List<string> claimed;
        }

        public object CaptureState()
        {
            BountyBoard live = Resolve();
            if (live == null || VersusSession.IsActive) return null;

            var closed = new List<ClosedEntry>();
            foreach (ClosedHunt hunt in live.Closed)
                if (BountyCatalog.At(hunt.place) is BountyHunt ended)
                    closed.Add(new ClosedEntry { id = ended.Id, closure = hunt.closure.ToString() });

            BountyHunt open = live.Open;
            BountyHuntRunState run = live.Run;
            if (open == null && closed.Count == 0 && run.pendingDespawn.Count == 0) return null;

            BountyProgress progress = live.Progress;
            return new State
            {
                open = open != null ? open.Id : null,
                phase = progress.Phase.ToString(),
                town = progress.Town,
                townRadius = progress.TownRadius,
                hasSite = open != null,
                site = progress.Site,
                run = run,
                closed = closed,
            };
        }

        public void RestoreState(JObject state)
        {
            BountyBoard live = Resolve();
            if (live == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            OldState old = state.ToObject<OldState>(SaveSerializer.Serializer);

            BountyHunt open = BountyCatalog.ById(restored.open);
            if (restored.open != null && open == null)
                Debug.LogWarning($"[Save] The open bounty '{restored.open}' no longer exists; the crew have none open.");

            if (!Enum.TryParse(restored.phase, out BountyPhase phase)) phase = BountyPhase.Asking;

            Vector3 site = restored.hasSite ? restored.site : open != null ? open.SitePosition : Vector3.zero;
            BountyHuntRunState run = restored.run ?? RunFromOldSave(old);
            List<ClosedHunt> closed = restored.closed != null ? Closed(restored.closed) : ClaimedFromOldSave(old);

            live.Restore(open, phase, restored.town, restored.townRadius, site, run, closed);
        }

        private static List<ClosedHunt> Closed(List<ClosedEntry> entries)
        {
            var closed = new List<ClosedHunt>();
            foreach (ClosedEntry entry in entries)
            {
                int place = BountyCatalog.IndexOf(BountyCatalog.ById(entry.id));
                if (place < 0)
                {
                    Debug.LogWarning($"[Save] The ended bounty '{entry.id}' no longer exists; it is dropped from the book.");
                    continue;
                }

                if (!Enum.TryParse(entry.closure, out BountyClosure closure))
                {
                    Debug.LogWarning($"[Save] Bounty '{entry.id}' ended in an unknown way '{entry.closure}'; it is read as Expired.");
                    closure = BountyClosure.Expired;
                }

                closed.Add(new ClosedHunt(place, closure));
            }
            return closed;
        }

        /// <summary>An old save's handed-in ids: every one was Claimed, there being no other way to end then.</summary>
        private static List<ClosedHunt> ClaimedFromOldSave(OldState old)
        {
            var closed = new List<ClosedHunt>();
            if (old.claimed == null) return closed;

            foreach (string id in old.claimed)
            {
                int place = BountyCatalog.IndexOf(BountyCatalog.ById(id));
                if (place >= 0) closed.Add(new ClosedHunt(place, BountyClosure.Claimed));
            }
            return closed;
        }

        /// <summary>An old save's single target is the run's one piece: the poster's own agent, at no spot.</summary>
        private static BountyHuntRunState RunFromOldSave(OldState old)
        {
            var run = new BountyHuntRunState { spawned = old.targetSpawned };
            if (old.target.IsSet)
                run.pieces.Add(new BountyHuntRunState.HuntPiece { body = old.target, target = true, spot = string.Empty });
            return run;
        }

        /// <summary>Resolved on demand: a staged record is applied the moment this registers, which can be before its siblings woke.</summary>
        private BountyBoard Resolve()
        {
            if (board == null) board = GetComponent<BountyBoard>();
            if (board == null) board = BountyBoard.Instance;
            return board;
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
