// The questions a spawn position has to answer that a terrain heightmap cannot.
//
// Everything protecting the spawn path used to be measured against the terrain: is this position
// under the surface, is it below the surface after the chunk finished loading, where is the surface
// if every probe missed. That is the right instrument outdoors and the wrong one indoors, and the
// game's only spawn point is indoors — a child of ShipRV, standing on the CargoBayFloor box.
//
// Measured in this project: the cargo bay floor sits at y=100.91 and the sand under the ship at
// y=100.00. Nine tenths of a metre is the entire margin. Park the ship a metre lower, or drive it
// (it is a drivable Rigidbody, and terrain in this world runs from 100 m to 167 m) onto ground that
// rises under the hull, and every terrain rule inverts at once: the under-terrain test vetoes every
// candidate inside the bay, the fallback answers with the height of the SAND — which is below the
// floor the player is supposed to stand on — and the clamp pushes anything that survived up into
// the same place. All three put the player inside the floor.
//
// So the rules here measure geometry instead. A floor is something you can stand on whether or not
// the heightmap agrees, and a ceiling is what tells you the heightmap was never the right question.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public static class SpawnClearance
    {
        /// <summary>Lift used to start a probe just clear of the surface it is measuring from.</summary>
        private const float Skin = 0.05f;

        /// <summary>
        /// How far up to look for a roof. Generous: it has to clear the tallest interior in the
        /// game, and a false "outdoors" costs more than a false "indoors" — outdoors is the answer
        /// that lets the terrain fallback drop a player through a floor.
        /// </summary>
        private const float RoofProbeDistance = 60f;

        // Sixteen is past the point of deciding: the first solid non-terrain collider in the list
        // already means "not clear", so an overflow can only drop hits that would have said the
        // same thing.
        private static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>
        /// Whether a body standing on <paramref name="groundPoint"/> would be inside something.
        ///
        /// <para>
        /// This is the test that actually prevents "spawned inside the floor", and it is the one
        /// that was missing: <c>SpawnPoint.blockingLayers</c> is the only clearance check the spawn
        /// path had, it is a per-instance layer mask, and on ShipRV it is set to Nothing — so
        /// nothing was ever checked there.
        /// </para>
        /// <para>
        /// Terrain is deliberately excluded. A capsule standing on a dune overlaps the slope behind
        /// it, so counting the terrain here would reject most legitimate outdoor spawns; whether a
        /// position is buried in the ground is a heightmap question and is answered as one. What is
        /// left is exactly the structure geometry a player can be trapped in.
        /// </para>
        /// <para>
        /// Other players are excluded too, and that exclusion is what makes a second player able to
        /// spawn at all. The game's only spawn point scatters inside 0.5 m on a floor 3.7 m by
        /// 8.5 m, so once one player is standing on it their own 3 m capsule overlaps every
        /// candidate the point can produce. Indoors there is no terrain fallback by design, so the
        /// answer was "not yet" forever and the second player waited out the caller's whole timeout
        /// before being dropped on the first one regardless. Standing briefly inside another player
        /// is not a trap — two dynamic capsules resolve apart on the next physics step — whereas
        /// being inside a hull is, and that is the distinction this test is meant to draw.
        /// </para>
        /// </summary>
        public static bool HasRoomToStand(Vector3 groundPoint, float height, float radius)
        {
            float bottom = Skin + radius;
            Vector3 lower = groundPoint + Vector3.up * bottom;
            Vector3 upper = groundPoint + Vector3.up * Mathf.Max(Skin + height - radius, bottom);

            int count = Physics.OverlapCapsuleNonAlloc(lower, upper, radius, Overlaps, ~0,
                                                       QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (Overlaps[i] == null || Overlaps[i] is TerrainCollider) continue;
                if (IsPlayerBody(Overlaps[i])) continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tag carried by the player character's root, and by nothing else in the project.
        ///
        /// Public because <see cref="SpawnManager"/> asks the same question from the other side:
        /// this test excludes player bodies from "is there room to stand", so somebody else has to
        /// be the one that knows where they already are. One spelling of the tag, one place.
        /// </summary>
        public const string PlayerTag = "Player";

        /// <summary>
        /// Whether this collider is part of a player's body.
        ///
        /// Asked of the attached Rigidbody rather than the collider, because the capsule lives on a
        /// child called "Collider" while the tag is on the root that owns the body — and because
        /// the Rigidbody stays the player's own even while they are parented to a mount, where
        /// <c>transform.root</c> would answer with the vehicle instead.
        ///
        /// The tag, not a component: <c>PlayerController</c> would work for players, but the
        /// obvious way to extend this to NPCs — matching <c>AgentController</c> — would also match
        /// ShipRV, whose hull and cargo bay floor are exactly the geometry this test exists to
        /// catch. Nothing but PlayerCharacter carries this tag, and the project defines no custom
        /// tags at all, so it cannot widen by accident.
        /// </summary>
        private static bool IsPlayerBody(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;

            return body != null
                ? body.CompareTag(PlayerTag)
                : collider.transform.root.CompareTag(PlayerTag);
        }

        /// <summary>
        /// Whether something built is overhead — a hull, a roof, a deck.
        ///
        /// The point of asking is not the shelter. It is that a position with a ceiling over it was
        /// authored against a floor, not against the terrain, so every terrain rule in the spawn
        /// path is answering a question nobody asked about it.
        /// </summary>
        public static bool IsSheltered(Vector3 position)
        {
            return Physics.Raycast(position + Vector3.up * Skin, Vector3.up, out RaycastHit hit,
                                   RoofProbeDistance, ~0, QueryTriggerInteraction.Ignore)
                   && hit.collider is not TerrainCollider;
        }

        /// <summary>
        /// Whether <paramref name="position"/> has a built floor within <paramref name="maxDrop"/>
        /// beneath it, as opposed to open air or bare terrain.
        ///
        /// Used to decide whether a position may be lifted to the terrain surface. Something
        /// standing on a floor is already supported, and raising it to the height of the ground
        /// outside moves it into that floor rather than out of trouble.
        /// </summary>
        public static bool StandsOnStructure(Vector3 position, float maxDrop)
        {
            return Physics.Raycast(position + Vector3.up * Skin, Vector3.down, out RaycastHit hit,
                                   maxDrop + Skin, ~0, QueryTriggerInteraction.Ignore)
                   && hit.collider is not TerrainCollider;
        }
    }
}
