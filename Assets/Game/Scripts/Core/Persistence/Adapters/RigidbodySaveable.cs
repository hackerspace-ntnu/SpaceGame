using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a Rigidbody's motion.
    ///
    /// Position and rotation are NOT stored here — the world store already carries them on the
    /// entity record, because it has to place an object before any of its components exist. What is
    /// left is the state that would otherwise be lost: a crate slid to a stop stays stopped, and a
    /// vehicle left rolling keeps its momentum.
    ///
    /// <b>Motion, and only motion — <c>isKinematic</c> is neither written nor read.</b> The flag
    /// used to be both, and it cost the game every loaded world: the player came back in a body
    /// physics could not move. It belongs to whichever live component wants it — <c>MountModule</c>
    /// while a rider is seated, <c>NetworkRigidbody</c> while ownership settles or for somebody
    /// else's player, <c>NetAuthority</c> on a machine that does not simulate the thing, and
    /// <c>WorldItem</c> and <c>GroundAnchorOnLand</c> once a thing has landed
    /// — and every one of them re-asserts it for itself on the far side of a load. A file that had
    /// an opinion about it could only ever overrule them, and the opinion it recorded was not even
    /// about the game: it was whatever netcode teardown had just done to the body on the way out.
    ///
    /// Old files still carry the field. They load fine and it is ignored — see
    /// <see cref="SaveSerializer"/>'s <c>MissingMemberHandling</c>, which exists for exactly this.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "rigidbody";

        private Rigidbody body;

        private Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        public string SaveKey => Key;

        public struct State
        {
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        /// <summary>
        /// The body's motion, or nothing at all when it has none to report.
        ///
        /// <b>A held body says nothing rather than saying zero.</b> Kinematic means somebody else is
        /// posing this body, so its velocity is not a fact about the entity — and the moment a
        /// capture is most likely to find it held is the worst one to believe: a save written from
        /// <c>SaveManager.OnApplicationQuit</c>, or the despawn capture in
        /// <c>PlayerSaveService.Unbind</c>, both run while netcode is tearing the session down and
        /// has already made every body kinematic. Recording that produced the file that could not be
        /// played. Returning null drops the entry instead (see <c>StateBag.Set</c>), which is the
        /// honest answer and is inert on load.
        ///
        /// The consequence worth naming: momentum still does not survive a quit, because there is no
        /// live reading left to take by then. It did not survive before either — the same capture
        /// recorded zero — so nothing is lost here. Making it work means capturing before teardown
        /// starts rather than during it, which is a change to when a save happens, not to what one
        /// says.
        /// </summary>
        public object CaptureState()
        {
            if (Body == null || Body.isKinematic) return null;

            return new State
            {
                velocity = Body.linearVelocity,
                angularVelocity = Body.angularVelocity,
            };
        }

        /// <summary>
        /// Gives the body back the motion it had.
        ///
        /// The LIVE body decides whether that lands, never the record: Unity throws when a velocity
        /// is assigned to a kinematic body, and the body in front of us is the one that would throw.
        /// A rider restored into their seat, or a crate restored onto the floor it settled on, is
        /// held by the component that holds it and is left alone.
        /// </summary>
        public void RestoreState(JObject state)
        {
            if (Body == null) return;

            if (Body.isKinematic) return;

            // No payload means the body had no motion worth recording — see CaptureState — so the
            // right answer is a body at rest, not a body keeping whatever it happens to be carrying.
            //
            // This half of the handoff is what makes the other half safe. TransformSaveable and
            // WorldSaveStore both decline to zero velocity when a RigidbodySaveable is present,
            // because it "owns momentum". They decided that from the COMPONENT existing, while the
            // component only actually assigned a velocity when a PAYLOAD existed — and a kinematic
            // capture drops the payload, which is the common case at quit. So a moving object could
            // be teleported to its saved pose and keep the velocity it had at its old one, then fly
            // off. Now this method always has an answer, so the ownership claim is true.
            if (state == null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Body.linearVelocity = restored.velocity;
            Body.angularVelocity = restored.angularVelocity;
        }
    }
}
