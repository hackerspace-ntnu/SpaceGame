// The wanted posters hung in loaded scenes, and what each one offers: its hunt to take, a "not yet" while its owner
// does not offer it to the reader, or a CLAIMED stamp once the hunt has been handed in. Posters register themselves
// (WantedPoster.OnEnable); the board restamps them whenever the claimed hunts change. Every machine, local only: the
// stamp is drawn from the replicated claimed list, never sent.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Presentation;
using SpaceGame.World.Posters;

namespace SpaceGame.World.Bounties
{
    public static class BountyPosters
    {
        private const string StampName = "BountyStamp";

        private static readonly List<WantedPoster> Hung = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Hung.Clear();

        public static void Hang(WantedPoster poster)
        {
            if (poster == null || Hung.Contains(poster)) return;
            Hung.Add(poster);
            Stamp(poster);
        }

        public static void TakeDown(WantedPoster poster) => Hung.Remove(poster);

        /// <summary>The hung poster of <paramref name="bounty"/> nearest <paramref name="point"/> within <paramref name="range"/>, or null.</summary>
        public static WantedPoster Nearest(BountyHunt bounty, Vector3 point, float range)
        {
            WantedPoster nearest = null;
            float best = range * range;
            foreach (WantedPoster poster in Hung)
            {
                if (poster == null || BountyCatalog.For(poster) != bounty) continue;
                float d = (poster.transform.position - point).sqrMagnitude;
                if (d > best) continue;
                best = d;
                nearest = poster;
            }
            return nearest;
        }

        /// <summary>What <paramref name="poster"/> offers its reader, or null when it is no bounty's or this is no story world.</summary>
        public static IPosterOffer OfferFor(WantedPoster poster)
        {
            BountyHunt bounty = BountyCatalog.For(poster);
            return bounty != null && BountyBoard.IsRunning ? new Offer(bounty) : null;
        }

        /// <summary>Stamps or clears every hung poster after the claimed hunts changed.</summary>
        public static void RestampAll()
        {
            foreach (WantedPoster poster in Hung) Stamp(poster);
        }

        private static void Stamp(WantedPoster poster)
        {
            if (poster == null) return;

            BountyBoard board = BountyBoard.Instance;
            BountyHunt bounty = BountyCatalog.For(poster);
            bool claimed = board != null && bounty != null && board.IsClaimed(BountyCatalog.IndexOf(bounty));

            // A reload or a late join can take a stamp back off: the claimed list it was laid from was replaced.
            Transform existing = poster.transform.Find(StampName);
            if (!claimed)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            TextMeshPro stamp = existing != null ? existing.GetComponent<TextMeshPro>() : poster.Sheet != null ? MakeStamp(poster, board.Stamp) : null;
            if (stamp == null) return;

            stamp.gameObject.SetActive(true);
            stamp.text = board.Stamp.text;
            stamp.color = board.Stamp.colour;
        }

        /// <summary>A rubber stamp across the sheet's face, laid just in front of it. Its words and ink are the caller's.</summary>
        private static TextMeshPro MakeStamp(WantedPoster poster, BountyBoard.StampLook look)
        {
            Bounds sheet = poster.Sheet.bounds;
            Transform face = poster.transform;

            var go = new GameObject(StampName);
            go.transform.SetParent(face, false);
            go.transform.position = sheet.center + face.forward * look.lift;
            // TextMeshPro reads from its -Z side; the printed face looks along the poster's +Z.
            go.transform.rotation = Quaternion.LookRotation(-face.forward, face.up) * Quaternion.Euler(0f, 0f, look.angle);

            var text = go.AddComponent<TextMeshPro>();
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0f;
            // How wide the sheet is across the poster: the world bounds' extent along its right.
            Vector3 right = face.right;
            float width = Mathf.Abs(right.x) * sheet.size.x + Mathf.Abs(right.y) * sheet.size.y + Mathf.Abs(right.z) * sheet.size.z;
            text.rectTransform.sizeDelta = new Vector2(width * look.widthShare, width * look.widthShare * look.heightShare);
            return text;
        }

        private sealed class Offer : IPosterOffer
        {
            private readonly BountyHunt bounty;
            private readonly int index;

            public Offer(BountyHunt bounty)
            {
                this.bounty = bounty;
                index = BountyCatalog.IndexOf(bounty);
            }

            private static BountyBoard Board => BountyBoard.Instance;

            private static BountyProgress Progress => Board != null ? Board.Progress : default;

            private bool Claimed => Board != null && Board.IsClaimed(index);

            /// <summary>Whether the hunt's owner puts it on offer to the local player now. Not loaded, it offers nothing.</summary>
            private bool Offered
            {
                get
                {
                    SettlementBounties owner = SettlementBounties.Owner(bounty);
                    return owner != null && owner.Offers(bounty, Board, GameplayMenuScope.LocalPlayerTransform);
                }
            }

            public string Stamp => Claimed ? Board.Stamp.text : string.Empty;

            public Color StampColour => Claimed ? Board.Stamp.colour : Color.clear;

            public bool CanTake => BountyRules.CanTake(Progress, index) && !Claimed && Offered;

            public string Prompt
            {
                get
                {
                    BountyProgress progress = Progress;
                    if (Claimed) return string.Empty;
                    if (progress.HasOpen && progress.OpenIndex == index) return "YOU HAVE TAKEN THIS BOUNTY: ASK AROUND TOWN";
                    if (progress.HasOpen) return "FINISH YOUR CURRENT BOUNTY FIRST";
                    if (!Offered) return "NOT YET";
                    return "Y: TAKE THIS BOUNTY";
                }
            }

            public void Take()
            {
                if (Board != null) Board.RequestTake(bounty);
            }
        }
    }
}
