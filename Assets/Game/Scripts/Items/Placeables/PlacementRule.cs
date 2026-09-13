// What "placing" means for one kind of placeable.
//
// PlaceableItem owns the LOOP and nothing else: aim on the holder's machine, ask whether this may
// go here, put it there on the server, and spend the item only if the world actually changed. What
// differs between placeables is not that loop -- it is the two questions inside it:
//
//   criteria   may this go HERE?      flat ground / an animal that can wear a saddle / a wall stud
//   logic      what does placing DO?  spawn a prefab / fit a saddle to a socket / weld a panel
//
// So they are a strategy on the item, not branches in the system. The system never learns what a
// bone or an animal is; SaddlePlacement does. Adding a placeable that attaches to something new
// means one new rule and no change here.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything a rule needs to judge a placement, gathered once on the machine whose aim is
    /// honest and then carried to the server in a <see cref="SpaceGame.Core.NetArg"/>.
    /// </summary>
    public readonly struct PlacementAim
    {
        /// <summary>Where the ray landed.</summary>
        public readonly Vector3 Point;

        /// <summary>The surface normal there. Zero when it came off the wire, which does not carry it.</summary>
        public readonly Vector3 Normal;

        /// <summary>What the ray hit, resolved on whichever machine is asking.</summary>
        public readonly GameObject Target;

        /// <summary>Which way the placer was facing, in degrees.</summary>
        public readonly float Yaw;

        public PlacementAim(Vector3 point, Vector3 normal, GameObject target, float yaw)
        {
            Point = point;
            Normal = normal;
            Target = target;
            Yaw = yaw;
        }

        /// <summary>Whether the ray hit anything at all. Zero is "aimed at open sky".</summary>
        public bool IsValid => Point != Vector3.zero || Target != null;
    }

    /// <summary>
    /// A placeable's criteria and its placement logic. Sits on the ITEM's prefab beside
    /// <see cref="PlaceableItem"/>, which finds it with GetComponent.
    /// </summary>
    public abstract class PlacementRule : MonoBehaviour
    {
        /// <summary>
        /// May it go here? Asked on the holder's machine before the request leaves, so a refused
        /// placement costs no round trip and no item — and asked AGAIN on the server, because the
        /// first answer came from a machine that does not decide anything.
        /// </summary>
        public abstract bool CanPlace(in PlacementAim aim);

        /// <summary>
        /// Server. Put it there, and return whether the world actually changed.
        ///
        /// <para>
        /// The bool is the whole contract with <see cref="PlaceableItem"/>: it spends the item on
        /// true and leaves it in the hotbar on false, so a rule that quietly declines can never
        /// eat what the player was holding.
        /// </para>
        /// </summary>
        public abstract bool Place(in PlacementAim aim, GameObject placer);

        /// <summary>
        /// What the HUD says when the aim is somewhere this rule refuses. Null for no explanation
        /// — a lantern over a cliff face needs none, a saddle aimed at a rock might.
        /// </summary>
        public virtual string RefusalHint(in PlacementAim aim) => null;
    }
}
