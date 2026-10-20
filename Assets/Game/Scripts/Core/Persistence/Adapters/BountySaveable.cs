using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.World.Bounties;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the crew's bounty book: the hunt they have open (where it was taken, where its site is, how far they
    /// are), the server's run of it (its cast, its clock), and every hunt handed in.
    ///
    /// <para>
    /// A global saver beside <see cref="ObjectiveSaveable"/>: the book belongs to the world, not to any object in it.
    /// <b>Ids, never catalog places</b>, so adding a hunt never moves an old save onto another. The cast are their own
    /// saved entities, named here by identity and found again by the board as their chunks load.
    /// </para>
    /// <para>
    /// Saves written before hunts had a cast still load: their <c>target</c> becomes one Target piece, and
    /// <c>targetSpawned</c> the run's <c>spawned</c>. Saves written while a failed hunt ended for good list ended hunts
    /// as <c>closed</c>: only the Claimed ones are kept, so an expired hunt can be taken again.
    /// </para>
    /// </summary>
    public class BountySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "bounties";   // written into save files — NEVER rename

        [Tooltip("Optional. Left empty, the board on this GameObject is used, then the live one.")]
        [SerializeField] private BountyBoard board;

        public string SaveKey => Key;

        // How a save's `closed` entry said a hunt was handed in. Every other way it ended was a failure.
        private const string ClaimedClosure = "Claimed";

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

            /// <summary>The ids of every hunt handed in, in the order they were.</summary>
            public List<string> claimed;
        }

        /// <summary>One ended hunt as saves wrote it while a failed hunt ended for good: its id, and how it ended by name.</summary>
        private struct ClosedEntry
        {
            public string id;
            public string closure;
        }

        /// <summary>The fields of older saves. Read, never written.</summary>
        private struct OldState
        {
            public SaveRef target;
            public bool targetSpawned;
            public List<ClosedEntry> closed;
        }

        public object CaptureState()
        {
            BountyBoard live = Resolve();
            if (live == null || VersusSession.IsActive) return null;

            var claimed = new List<string>();
            foreach (int place in live.Claimed)
                if (BountyCatalog.At(place) is BountyHunt handedIn) claimed.Add(handedIn.Id);

            BountyHunt open = live.Open;
            BountyHuntRunState run = live.Run;
            if (open == null && claimed.Count == 0 && run.pendingDespawn.Count == 0) return null;

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
                claimed = claimed,
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
            IEnumerable<string> claimed = restored.claimed ?? ClaimedOf(old.closed);

            live.Restore(open, phase, restored.town, restored.townRadius, site, run, Places(claimed));
        }

        /// <summary>The catalog places of <paramref name="ids"/>; one no longer in the catalog is dropped, with a warning.</summary>
        private static List<int> Places(IEnumerable<string> ids)
        {
            var places = new List<int>();
            foreach (string id in ids)
            {
                int place = BountyCatalog.IndexOf(BountyCatalog.ById(id));
                if (place >= 0) places.Add(place);
                else Debug.LogWarning($"[Save] The claimed bounty '{id}' no longer exists; it is dropped from the book.");
            }
            return places;
        }

        /// <summary>
        /// The handed-in ids of a save that listed ended hunts as <c>closed</c>. The rest failed, and a failed hunt is
        /// no longer over for good: it is offered again. None with no such list.
        /// </summary>
        private static IEnumerable<string> ClaimedOf(List<ClosedEntry> closed) =>
            closed == null ? Enumerable.Empty<string>() : closed.Where(entry => entry.closure == ClaimedClosure).Select(entry => entry.id);

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
