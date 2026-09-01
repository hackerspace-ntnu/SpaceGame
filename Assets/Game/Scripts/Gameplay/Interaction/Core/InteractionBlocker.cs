// Glass: something you can see through and cannot reach through.
//
// Interactor.ResolveAlongRay treats a trigger as a detection volume rather than a surface — it
// answers only when it carries the interactable itself, and is otherwise transparent, so the ray
// passes on to whatever is really there. That is what stops a deck-wide carry volume answering for
// every control standing inside it. It also means anything with no solid collider is not in the way
// at all, and a hull is allowed to have holes in its collision: the PlayerShip's canopy dome
// deliberately carries none, because a convex hull of the glass fills the cockpit and would brain a
// three-metre pilot. The four cockpit chairs' boarding volumes were therefore in open sight of
// anyone outside the ship, and pressing E over the dome put the presser in the pilot's seat.
//
// Put this on a trigger collider standing in for something solid to look through, and the ray stops
// there: nothing behind it can be used, and nothing physical changes, because a trigger collides
// with nothing and every movement, ground and clearance query in this project asks with
// QueryTriggerInteraction.Ignore.
//
// It blocks from OUTSIDE only, which is the whole trick — a ray that starts inside a collider is not
// reported as hitting it, so the pilot standing under the canopy still reaches their chair. A blocker
// therefore has to ENCLOSE the space it protects rather than merely stand between it and one
// viewpoint: hugging the shape of the PlayerShip's dome left the cockpit open over its aft rim, and
// only a box over the whole glazed opening closed it.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class InteractionBlocker : MonoBehaviour
    {
    }
}
