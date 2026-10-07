// A trader's stock list as a shareable asset.
//
// Optional — TraderInteraction takes offers inline too. This exists so a caravan of six identical
// scavengers can share one stock list, and so a trader spawned by NpcWorldSim (which instantiates
// from a prefab and has nowhere to author per-instance offers) still has something to sell.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Trading
{
    [CreateAssetMenu(menuName = "Trading/Trader Profile")]
    public class TraderProfile : ScriptableObject
    {
        [Tooltip("How this trader opens. Shown as the question when the player talks to them.")]
        [TextArea(1, 3)]
        public string greeting = "Got anything worth having?";

        [Tooltip("Label on the accept button of that question.")]
        public string acceptLabel = "Let's trade";

        [Tooltip("Label on the decline button.")]
        public string declineLabel = "Not now";

        [Tooltip("What they say if you turn them down.")]
        [TextArea(1, 3)]
        public string declineLine = "Suit yourself.";

        [Tooltip("What they say when every offer is out of stock.")]
        [TextArea(1, 3)]
        public string soldOutLine = "Cleaned out. Come back when I've been out again.";

        [Tooltip("The stock. Each entry is one swap they will make.")]
        public List<TradeOffer> offers = new();

        /// <summary>
        /// A per-trader working copy.
        ///
        /// Taken because stock is decremented as the player buys, and an asset is shared: without
        /// this, buying the last water cell from one scavenger would empty every scavenger in the
        /// world that shares the profile — and would persist into the next session, because a
        /// mutated ScriptableObject is written back to disk in the editor.
        /// </summary>
        public List<TradeOffer> CloneOffers()
        {
            var copy = new List<TradeOffer>(offers.Count);

            foreach (TradeOffer offer in offers)
            {
                if (offer == null) continue;

                copy.Add(new TradeOffer
                {
                    wants = offer.wants,
                    wantsCount = Mathf.Max(1, offer.wantsCount),
                    gives = offer.gives,
                    givesCount = Mathf.Max(1, offer.givesCount),
                    pitch = offer.pitch,
                    stock = offer.stock,
                });
            }

            return copy;
        }
    }
}
