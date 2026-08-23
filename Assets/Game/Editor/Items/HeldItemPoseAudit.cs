// Where every held item actually ends up in the hand, measured rather than eyeballed.
//
// GripFrameTests pins the frame — which way the hand points. Nothing pinned the other half:
// where each individual artifact sits once EquipItemSocket has seated it. That half is authored
// per prefab in ItemGrip, it is judged by looking at the thing, and it goes wrong quietly. An
// item whose grip point sits at the edge of its own mesh is seated exactly as asked and still
// hangs off the hand, because seating puts the GRIP POINT in the palm and lets the mesh fall
// where it may.
//
// So the numbers worth having are per item, and there are only two:
//
//   palmDist   how far the mesh's centre ends up from the palm, in metres.
//   gripNorm   where the grip point sits inside the item's own mesh, 0..1 per axis.
//              0.5 is dead centre; near 0 or 1 means the hand closes on the very end of the
//              model. That is legitimate for a staff or a top-handled extinguisher and wrong
//              for a pistol, which is why this reports rather than asserts.
//
// Run it from Tools/SpaceGame/Items/Audit Held Item Poses after touching a grip, a model, or
// HandGripFrame itself.
using System.Text;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class HeldItemPoseAudit
    {
        private const string PlayerPrefab =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        [MenuItem("Tools/SpaceGame/Items/Audit Held Item Poses")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (prefab == null)
            {
                Debug.LogError("HeldItemPoseAudit: no player prefab at " + PlayerPrefab);
                return;
            }

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.hideFlags = HideFlags.DontSave;
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            try
            {
                var anim = player.GetComponentInChildren<Animator>(true);
                Transform hand = anim != null && anim.isHuman
                    ? anim.GetBoneTransform(HumanBodyBones.RightHand)
                    : null;

                if (hand == null)
                {
                    // Almost always the avatar silently downgrading to generic on re-export.
                    Debug.LogError("HeldItemPoseAudit: no humanoid right hand on the player rig.");
                    return;
                }

                HandGripFrame frame = HandGripFrame.Derive(anim, hand, true);
                var socket = new EquipItemSocket(hand, frame, 1f);
                Vector3 palm = socket.GripPosition;

                var sb = new StringBuilder();
                sb.AppendLine("Held item pose audit — frame from " + frame.Source);
                sb.AppendLine();
                sb.AppendLine("item                 palmDist   size   gripNorm (0..1 in its own mesh)");

                var items = Resources.LoadAll<InventoryItem>("Items");
                for (int i = 0; i < items.Length; i++)
                {
                    InventoryItem it = items[i];
                    if (it == null || it.itemPrefab == null) continue;

                    GameObject held = socket.Equip(it.itemPrefab);
                    if (held == null)
                    {
                        sb.AppendLine(string.Format("{0,-20}  <equip failed>", it.name));
                        continue;
                    }

                    if (!TryMeasure(held, out Bounds world, out Bounds local))
                    {
                        // No mesh is normal for a pure-effect item; say so rather than print zeros.
                        sb.AppendLine(string.Format("{0,-20}  (no mesh)", it.name));
                        Object.DestroyImmediate(held);
                        continue;
                    }

                    var grip = held.GetComponent<ItemGrip>();
                    Transform gp = grip != null ? grip.GripPoint : held.transform;
                    Vector3 gl = held.transform.InverseTransformPoint(gp.position);
                    Vector3 n = Normalise(gl, local);

                    sb.AppendLine(string.Format("{0,-20} {1,8:F3} {2,6:F2}   ({3,5:F2},{4,5:F2},{5,5:F2}) {6}{7}",
                        it.name,
                        Vector3.Distance(palm, world.center),
                        world.size.magnitude,
                        n.x, n.y, n.z,
                        grip == null ? "  no ItemGrip" : "",
                        AtEdge(n) ? "  <-- gripped at an edge of its own mesh" : ""));

                    Object.DestroyImmediate(held);
                }

                Debug.Log(sb.ToString());
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        /// <summary>
        /// World bounds of what the player sees, and the same extents in the item root's local
        /// space. Mesh-derived for the same reason EquipItemSocket measures that way: line and
        /// particle renderers report effects that are not the object, and the Lasso's rope
        /// renderer alone spans metres.
        /// </summary>
        private static bool TryMeasure(GameObject item, out Bounds world, out Bounds local)
        {
            world = new Bounds();
            local = new Bounds();
            bool any = false;

            Matrix4x4 toRoot = item.transform.worldToLocalMatrix;
            var filters = item.GetComponentsInChildren<MeshFilter>(true);

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;

                var renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (!filters[i].gameObject.activeInHierarchy) continue;

                if (!any)
                {
                    world = renderer.bounds;
                    local = Transformed(mesh.bounds, toRoot * filters[i].transform.localToWorldMatrix);
                    any = true;
                }
                else
                {
                    world.Encapsulate(renderer.bounds);
                    local.Encapsulate(Transformed(mesh.bounds, toRoot * filters[i].transform.localToWorldMatrix));
                }
            }

            return any;
        }

        private static Bounds Transformed(Bounds b, Matrix4x4 m)
        {
            Vector3 c = b.center;
            Vector3 e = b.extents;
            var result = new Bounds();

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));

                Vector3 p = m.MultiplyPoint3x4(corner);
                if (i == 0) result = new Bounds(p, Vector3.zero);
                else result.Encapsulate(p);
            }

            return result;
        }

        private static Vector3 Normalise(Vector3 p, Bounds b)
        {
            return new Vector3(
                b.size.x < 1e-6f ? 0.5f : (p.x - b.min.x) / b.size.x,
                b.size.y < 1e-6f ? 0.5f : (p.y - b.min.y) / b.size.y,
                b.size.z < 1e-6f ? 0.5f : (p.z - b.min.z) / b.size.z);
        }

        /// <summary>Grip within 10% of a face of the item's own bounding box.</summary>
        private static bool AtEdge(Vector3 n)
        {
            return n.x < 0.1f || n.x > 0.9f
                || n.y < 0.1f || n.y > 0.9f
                || n.z < 0.1f || n.z > 0.9f;
        }
    }
}
