// Put a saddle on an animal.
//
// Aimed, so the aim is read on the holder's machine in OnRequestUse and travels in the NetArg --
// the server's Camera.main is the HOST's camera, and recomputing the ray there would saddle
// whatever the host happened to be looking at.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Fits a saddle to whatever the holder is pointing at, if that animal has a socket for one,
    /// and is spent doing it.
    ///
    /// <para>
    /// Taking it off is deliberately NOT this item's job: you may not be holding a saddle when you
    /// want one removed, and the removed saddle has to go somewhere anyway. The saddle carries its
    /// own "take it off" verb (<see cref="SaddleRemover"/>) and returns itself to the ground.
    /// </para>
    /// </summary>
    public class SaddleArtifact : ToolItem
    {
        /// <summary>Server: whether an animal is wearing a saddle is world state, not the holder's body.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Tooltip("How far away an animal can be saddled from.")]
        [SerializeField] private float range = 4.5f;

        [Tooltip("Played on every machine when it goes on. Cosmetic.")]
        [SerializeField] private SfxId fitSound = SfxId.NpcMumbleFriendly;

        /// <summary>Owner-side: the only machine whose aim is honest.</summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            RaycastHit? hit = aimProvider != null ? aimProvider.GetRayCast(range) : null;
            if (!hit.HasValue) return;

            // The SOCKET's object, not the collider that was hit -- an animal's collider is on its
            // root but a ray can just as easily land on a child, and the id has to name the thing
            // the server will resolve.
            SaddleSocket socket = hit.Value.collider.GetComponentInParent<SaddleSocket>();
            if (socket != null) arg = arg.With(socket.gameObject);
        }

        protected override void Use()
        {
            GameObject target = UseArg.Resolve();
            if (target == null) return;

            SaddleSocket socket = target.GetComponent<SaddleSocket>();
            if (socket == null) return;

            // Asked directly rather than through Request: Use() is already the server, and this
            // needs the ANSWER. The saddle is spent by going onto an animal, so a click that
            // saddled nothing -- one already wearing one, a socket with no prefab -- must leave the
            // item in the hotbar. Removing it puts one back (SpillAndReturn), so across the whole
            // loop there is exactly one saddle: in your pack, or on the animal.
            if (!socket.Fit()) return;

            Deplete();
        }

        protected override void Present()
        {
            Sfx.Play(fitSound, transform.position, GetInstanceID());
        }

        private void OnValidate() => range = Mathf.Max(0.5f, range);
    }
}
