using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// A work post for the pieces that are plainly somewhere to work but bring none: a pump jack, a transformer, a pipe manifold, a tool
    /// rack and a chain hoist are the <c>Workshop</c> (a mechanic repairs there); a cauldron and a spit are the <c>Kitchen</c>. Without them
    /// the pump station and the power station have nothing for a resident to do. The post stands in front of the piece (its local +Z, the
    /// way every decoration prefab authors its spots), a metre past its bounds, looking at its middle.
    /// </summary>
    public static class OutpostPosts
    {
        private const string SpotUseDir = "Assets/Game/ScriptableObjects/Settlements/Spots";

        // How far past the piece's bounds the post stands, metres: clear of the NavMesh's half-metre margin round the piece.
        private const float StandOff = 1.1f;

        // How high the post looks at, metres: a person's chest.
        private const float LookHeight = 1.2f;

        private static readonly Dictionary<string, string> UseOf = new()
        {
            ["PumpJack"] = "Workshop",
            ["TransformerBox"] = "Workshop",
            ["Pipe_Manifold"] = "Workshop",
            ["ToolRack"] = "Workshop",
            ["Salvage_ChainHoist"] = "Workshop",
            ["Cauldron"] = "Kitchen",
            ["CookingHearth_Spit"] = "Kitchen",
        };

        /// <summary>Puts a post at each piece that needs one and has none of its own under <paramref name="parent"/>; returns how many.</summary>
        public static int Add(Transform parent, IReadOnlyList<PlacedPiece> pieces)
        {
            int count = 0;
            foreach (PlacedPiece piece in pieces.Where(p => p.instance != null && UseOf.ContainsKey(p.row.kind)))
            {
                if (piece.instance.GetComponentInChildren<SettlementSpot>(true) != null) continue;

                SpotUse use = AssetDatabase.LoadAssetAtPath<SpotUse>($"{SpotUseDir}/{UseOf[piece.row.kind]}.asset");
                Bounds bounds = piece.instance.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                Vector3 front = piece.instance.forward;
                Vector3 reach = new Vector3(Mathf.Abs(front.x) * bounds.extents.x, 0f, Mathf.Abs(front.z) * bounds.extents.z);

                Vector3 stand = new Vector3(bounds.center.x, piece.instance.position.y, bounds.center.z) + front * (reach.x + reach.z + StandOff);
                Vector3 look = new Vector3(bounds.center.x, piece.instance.position.y + LookHeight, bounds.center.z);
                ResidentErrandContentBuilder.AddSpotObject(parent.gameObject, use, $"Spot_{use.name}_{piece.name}", stand, look, null);
                count++;
            }
            return count;
        }
    }
}
