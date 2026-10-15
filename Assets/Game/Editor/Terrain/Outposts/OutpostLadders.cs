using System.Collections.Generic;
using SpaceGame.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// The ladders an outpost's pieces carry in their meshes. A <c>Deco_BellFrame</c> and a <c>Deco_Scaffold_Tower</c> are
    /// modelled with a ladder up one face but ship no <see cref="Ladder"/>, so nobody can climb it. Each is measured off
    /// the .blend (Blender local axes, mapped to the piece's Unity axes with (x, y, z) -> (-x, z, -y)) and given one here:
    /// foot on the rung line at the ground, the step-off height, and a point on the deck behind the rungs.
    /// </summary>
    public static class OutpostLadders
    {
        private readonly struct Spec
        {
            public readonly Vector3 foot;
            public readonly float topHeight;
            public readonly Vector3 exit;
            public readonly bool residentsClimb;

            public Spec(Vector3 foot, float topHeight, Vector3 exit, bool residentsClimb)
            {
                this.foot = foot;
                this.topHeight = topHeight;
                this.exit = exit;
                this.residentsClimb = residentsClimb;
            }
        }

        // BellFrame: the ladder leans up the +X face (Blender), rails from x 1.95 at the ground to 1.05 at 3.46, the plank deck
        // topping out at 3.27. The climbing line is the rail's middle; the exit is 0.7 m in from the deck's edge.
        // Scaffold_Tower: a vertical ladder up the -X end, decks at 3.0 / 6.0 / 9.0 m; it ends on the top deck, 0.75 m in. The top deck is
        // 2.5 x 1.4 m, too small for the NavMesh to keep, so the player climbs it and residents do not (a link with no mesh at its end only
        // warns, after 30 s, on every load).
        private static readonly Dictionary<string, Spec> Specs = new()
        {
            ["BellFrame"] = new Spec(new Vector3(-1.55f, 0f, -0.37f), 3.27f, new Vector3(-0.35f, 3.27f, -0.37f), residentsClimb: true),
            ["Scaffold_Tower"] = new Spec(new Vector3(1.15f, 0f, -0.17f), 9.15f, new Vector3(0.4f, 9.15f, -0.17f), residentsClimb: false),
        };

        public static bool Has(string kind) => Specs.ContainsKey(kind);

        /// <summary>Adds the piece's ladder under <paramref name="parent"/>, in the piece's own pose. False for a piece with none.</summary>
        public static bool TryAdd(Transform parent, Transform piece, string kind)
        {
            if (!Specs.TryGetValue(kind, out Spec spec)) return false;

            var ladderObject = new GameObject($"Ladder_{piece.name}");
            ladderObject.transform.SetParent(parent, false);
            ladderObject.transform.SetPositionAndRotation(piece.TransformPoint(spec.foot), piece.rotation);

            Transform top = Marker(ladderObject.transform, "Top", piece.TransformPoint(new Vector3(spec.foot.x, spec.topHeight, spec.foot.z)));
            Transform exit = Marker(ladderObject.transform, "Exit", piece.TransformPoint(spec.exit));
            var ladder = ladderObject.AddComponent<Ladder>();
            ladder.Configure(top, exit);
            var so = new SerializedObject(ladder);
            so.FindProperty("navigable").boolValue = spec.residentsClimb;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static Transform Marker(Transform parent, string name, Vector3 position)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.position = position;
            return marker;
        }
    }
}
