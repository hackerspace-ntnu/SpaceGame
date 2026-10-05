using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// Collision an outpost has to be walked in. The decoration prefabs hull their pieces for scenery, and a resident is a NavMesh agent a
    /// metre wide that the bake keeps 0.67 m (two 0.33 m voxels) clear of every obstacle, in a region of at least 2 m2. Left as they are, the
    /// outposts' decks are not floors to it (a 1.4 m walkway leaves nothing, a 2.4 m bell-frame deck 1 m2), the ladders lead nowhere, and
    /// the ground is cut into islands by tent poles. Inside an outpost, then:
    /// <list type="bullet">
    /// <item>The pieces in <see cref="Fits"/> (<c>BellFrame</c>, <c>Scaffold_Walkway</c>) swap their own colliders for a deck wider than the
    /// model, so a resident on its centre line is still on the model's deck, with walls round the model's edge the bake ignores so a
    /// player cannot step onto the overhang, and the rails the model has likewise.</item>
    /// <item>A thin pole (a game object of capsules under 0.3 m across), a roof, and anything standing up on a roof or deck (its origin more
    /// than <see cref="Elevated"/> above the ground) is moved to the layer the bake ignores. It still stops a player and stands in the
    /// picture; to a resident it is something to brush past, not a wall.</item>
    /// </list>
    /// The piece's own prefab is untouched. Measured off each prefab's own colliders, in the piece's Unity axes.
    /// </summary>
    public static class OutpostCollision
    {
        // The layer the world NavMesh bake leaves out (WorldNavMesh.asset's layer mask) and the physics matrix still collides.
        private const string BakeIgnoredLayer = "Ignore Raycast";

        // Metres: a game object of capsules at most this wide is a pole, not a wall.
        private const float ThinPoleRadius = 0.15f;

        // Metres: a piece whose origin is this far above the ground stands on a roof or a deck.
        private const float Elevated = 2f;

        private static readonly HashSet<string> Roofs = new() { "Roof_RustLeanTo", "Roof_RustPatchwork", "Roof_RustFlat" };

        private readonly struct Part
        {
            public readonly Vector3 centre;
            public readonly Vector3 size;
            public readonly bool ignoredByBake;

            public Part(Vector3 centre, Vector3 size, bool ignoredByBake = false)
            {
                this.centre = centre;
                this.size = size;
                this.ignoredByBake = ignoredByBake;
            }
        }

        private static readonly Dictionary<string, Part[]> Fits = new()
        {
            // The deck top at 3.27, four corner posts, and a deck 3 m square (the model's is 2 m) walled in at its visible edge on the three
            // sides the ladder does not climb from (the ladder is on the -X side).
            ["BellFrame"] = new[]
            {
                new Part(new Vector3(0f, 3.21f, 0f), new Vector3(3f, 0.12f, 3f)),
                new Part(new Vector3(1.05f, 2.2f, 1.05f), new Vector3(0.3f, 4.4f, 0.3f), ignoredByBake: true),
                new Part(new Vector3(-1.05f, 2.2f, 1.05f), new Vector3(0.3f, 4.4f, 0.3f), ignoredByBake: true),
                new Part(new Vector3(1.05f, 2.2f, -1.05f), new Vector3(0.3f, 4.4f, 0.3f), ignoredByBake: true),
                new Part(new Vector3(-1.05f, 2.2f, -1.05f), new Vector3(0.3f, 4.4f, 0.3f), ignoredByBake: true),
                new Part(new Vector3(1.0f, 3.8f, 0f), new Vector3(0.1f, 1f, 3f), ignoredByBake: true),
                new Part(new Vector3(0f, 3.8f, 1.0f), new Vector3(3f, 1f, 0.1f), ignoredByBake: true),
                new Part(new Vector3(0f, 3.8f, -1.0f), new Vector3(3f, 1f, 0.1f), ignoredByBake: true),
            },
            // The deck (top at 3.0) made 2.6 m wide (the model's is 1.44), its two rails at the model's edge, and the six legs it stands on.
            ["Scaffold_Walkway"] = new[]
            {
                new Part(new Vector3(0f, 2.9f, 0f), new Vector3(5f, 0.2f, 2.6f)),
                new Part(new Vector3(0f, 3.58f, 0.72f), new Vector3(5f, 1.15f, 0.08f), ignoredByBake: true),
                new Part(new Vector3(0f, 3.58f, -0.72f), new Vector3(5f, 1.15f, 0.08f), ignoredByBake: true),
                new Part(new Vector3(2.41f, 1.45f, 0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
                new Part(new Vector3(2.41f, 1.45f, -0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
                new Part(new Vector3(0f, 1.45f, 0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
                new Part(new Vector3(0f, 1.45f, -0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
                new Part(new Vector3(-2.41f, 1.45f, 0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
                new Part(new Vector3(-2.41f, 1.45f, -0.72f), new Vector3(0.12f, 2.9f, 0.12f)),
            },
        };

        private static readonly string[] OwnColliders = { "Colliders", "Colliders_Fit" };

        /// <summary>Makes the piece walkable-in: its stand-in colliders if it has them, else the walk-through rules. <paramref name="parent"/> holds what is added.</summary>
        public static void Apply(Transform parent, Transform piece, string kind)
        {
            if (Fits.TryGetValue(kind, out Part[] parts)) Fit(parent, piece, parts);
            else WalkThrough(piece, kind);
        }

        private static void Fit(Transform parent, Transform piece, Part[] parts)
        {
            foreach (string own in OwnColliders)
            {
                Transform colliders = piece.Find(own);
                if (colliders != null) colliders.gameObject.SetActive(false);
            }

            var container = new GameObject($"Collision_{piece.name}").transform;
            container.SetParent(parent, false);
            container.SetPositionAndRotation(piece.position, piece.rotation);
            container.localScale = piece.lossyScale;   // the author sized some of these pieces up

            foreach (Part part in parts)
            {
                var box = new GameObject(part.ignoredByBake ? "WalkThrough" : "Solid");
                box.transform.SetParent(container, false);
                box.transform.localPosition = part.centre;
                if (part.ignoredByBake) box.layer = LayerMask.NameToLayer(BakeIgnoredLayer);
                box.AddComponent<BoxCollider>().size = part.size;
            }
        }

        private static void WalkThrough(Transform piece, string kind)
        {
            int ignored = LayerMask.NameToLayer(BakeIgnoredLayer);
            bool overhead = Roofs.Contains(kind) || piece.position.y > Elevated;
            foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true))
            {
                GameObject owner = collider.gameObject;
                if (overhead || IsPole(owner)) owner.layer = ignored;
            }
        }

        // Every collider on the object is a capsule no wider than a pole: an object that also holds a box or a wider capsule is a wall.
        private static bool IsPole(GameObject owner)
        {
            foreach (Collider collider in owner.GetComponents<Collider>())
            {
                Vector3 scale = owner.transform.lossyScale;
                if (collider is not CapsuleCollider capsule || capsule.radius * Mathf.Max(scale.x, scale.z) > ThinPoleRadius) return false;
            }
            return true;
        }
    }
}
