// A town's notice board that hangs the town's own wanted posters by itself: the hunt the crew have open here, then
// every hunt the town offers now, then the ones claimed here (stamped CLAIMED, most recent first), as many
// as it has slots. A hunt appears the moment its owner starts offering it — an earlier hunt claimed, goodwill earned —
// with nothing placed by hand. The town is the Settlement above the board, and its SettlementBounties says what it offers.
//
// Every machine, local only: what hangs is worked out from replicated state (the open hunt, the claimed list, this
// machine's own goodwill), so nothing is sent or saved. The server's own copy is what BountyBoard.Take measures the
// taker's distance against.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Presentation;
using SpaceGame.World.Posters;

namespace SpaceGame.World.Bounties
{
    [DisallowMultipleComponent]
    public sealed class BountyNoticeBoard : MonoBehaviour
    {
        [Tooltip("Where posters hang on the board's face, in the order they fill. A slot's own scale sizes the poster; " +
                 "the poster's printed face looks along the slot's forward.")]
        [SerializeField] private Transform[] slots = System.Array.Empty<Transform>();

        [Tooltip("Seconds between checks of what the town offers. Offers change with goodwill and claimed hunts, neither " +
                 "of which announces itself here, so the board looks again this often.")]
        [SerializeField, Min(0.1f)] private float refreshSeconds = 1f;

        private readonly List<BountyHunt> wanted = new();
        private readonly List<BountyHunt> showing = new();
        private readonly List<WantedPoster> hung = new();
        private SettlementBounties owner;
        private bool ownerLooked;
        private float nextRefresh;

        private void OnDisable()
        {
            TakeAllDown();
            ownerLooked = false;
        }

        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshSeconds;

            BountyBoard board = BountyBoard.Instance;
            if (!BountyBoard.IsRunning || Owner() == null)
            {
                TakeAllDown();
                return;
            }

            Choose(owner, board, GameplayMenuScope.LocalPlayerTransform, slots.Length, wanted);
            if (!SameAs(showing, wanted)) Rehang();
        }

        /// <summary>
        /// The hunts <paramref name="town"/> shows on a board of <paramref name="capacity"/> slots, in slot order: the open
        /// one if it is the town's, then those offered to <paramref name="reader"/> newest first (later in the town's
        /// list), then the claimed ones most recent first.
        /// </summary>
        public static void Choose(SettlementBounties town, BountyBoard board, Transform reader, int capacity, List<BountyHunt> into)
        {
            into.Clear();
            BountyHunt open = board.Open;
            IReadOnlyList<SettlementBounties.Entry> entries = town.Entries;

            if (open != null && Owns(entries, open)) into.Add(open);

            for (int i = entries.Count - 1; i >= 0 && into.Count < capacity; i--)
            {
                BountyHunt hunt = entries[i]?.hunt;
                if (hunt == null || hunt == open || into.Contains(hunt) || board.IsClaimed(BountyCatalog.IndexOf(hunt))) continue;
                if (town.Offers(hunt, board, reader)) into.Add(hunt);
            }

            IReadOnlyList<int> claimed = board.Claimed;
            for (int i = claimed.Count - 1; i >= 0 && into.Count < capacity; i--)
            {
                BountyHunt hunt = BountyCatalog.At(claimed[i]);
                if (hunt != null && Owns(entries, hunt) && !into.Contains(hunt)) into.Add(hunt);
            }

            if (into.Count > capacity) into.RemoveRange(capacity, into.Count - capacity);
        }

        private static bool Owns(IReadOnlyList<SettlementBounties.Entry> entries, BountyHunt hunt)
        {
            foreach (SettlementBounties.Entry entry in entries)
                if (entry != null && entry.hunt == hunt) return true;
            return false;
        }

        private static bool SameAs(List<BountyHunt> a, List<BountyHunt> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The town's SettlementBounties, looked up once: the board is part of the town and never moves out of it.</summary>
        private SettlementBounties Owner()
        {
            if (ownerLooked) return owner;
            ownerLooked = true;
            Settlement town = GetComponentInParent<Settlement>();
            owner = town != null ? town.GetComponent<SettlementBounties>() : null;
            return owner;
        }

        private void Rehang()
        {
            TakeAllDown();
            // Recorded whole, so a hunt with no poster is reported once rather than on every refresh.
            showing.AddRange(wanted);
            for (int i = 0; i < wanted.Count; i++)
            {
                WantedPoster prefab = wanted[i].Poster;
                if (prefab != null) hung.Add(Instantiate(prefab, slots[i], false));
                else Debug.LogError($"[Bounties] '{wanted[i].name}' has no poster to hang on {name}.", wanted[i]);
            }
        }

        private void TakeAllDown()
        {
            foreach (WantedPoster poster in hung)
                if (poster != null) Destroy(poster.gameObject);
            hung.Clear();
            showing.Clear();
        }
    }
}
