// A resident's hands on a cart: the deciding machine's half of pushing.
//
// A resident whose archetype pushes a cart takes hold of the nearest free cart within reach of its body once it is working at its
// post, and lets go when the work ends, when something overrides the plan, when the resident goes indoors or dies, and when the
// cart goes away from under it. Wherever the resident walks while it holds the cart, the cart goes too: it is posed from the
// body (CartPusher), not driven. No cart in reach means no grip, and the work is done empty-handed; nothing is mimed.
//
// Server only. Every other machine learns the claim from the cart id ResidentPresence publishes. A claim is never saved: after a
// load the plan puts the resident at its post again and it takes hold again; the cart stays where the PushableLedger left it.
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class ResidentPushing
    {
        private readonly GameObject body;
        private Pushable cart;
        private bool thrownOff;

        public ResidentPushing(GameObject body) => this.body = body;

        public bool IsPushing => cart != null;

        /// <summary>The id of the cart in the resident's hands; 0 when it holds none.</summary>
        public int CartId => cart != null ? cart.Id : ResidentPresence.NoCart;

        /// <summary>Brings the hands in line with the plan: <paramref name="wanted"/> is whether the resident should be holding a cart now.</summary>
        public void Sync(bool wanted)
        {
            if (!Network.Decides) return;

            if (cart != null && (!wanted || thrownOff)) Release();
            if (cart != null || !wanted) return;

            Pushable found = Pushable.NearestFree(body.transform.position, ResidentTuning.Instance.cartReach);
            if (found == null || !found.TryClaim(body.transform)) return;

            cart = found;
            cart.Released += OnReleased;
            CartPusher.On(body).Grip(cart);
        }

        /// <summary>Lets go and leaves the cart standing where it is.</summary>
        public void Release()
        {
            Pushable let = cart;
            if (let == null) return;

            cart = null;
            thrownOff = false;
            let.Released -= OnReleased;
            let.Release(body.transform);
        }

        // The cart was carried away (its chunk unloaded): the hands let go at the next Sync.
        private void OnReleased(Pushable released, Transform who)
        {
            if (body != null && who == body.transform && released == cart) thrownOff = true;
        }
    }
}
