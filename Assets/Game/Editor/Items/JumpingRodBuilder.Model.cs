// The half of the jumping rod build that deals with the imported FBX: loading it, nesting it,
// checking it is the model this builder was written against, and measuring it.
//
// Split from JumpingRodBuilder.cs because the two halves fail for different reasons and are read
// at different times. That file says what the rod IS; this one is what you open when a re-export
// has changed something underneath it.
//
// Everything positional is MEASURED rather than typed from the generator's constants. A Blender
// export bakes its Z-up-to-Y-up conversion into the transform of every root object, so an
// assumption about which way the model arrived is the one mistake here that produces a prefab
// that looks right in the inspector and is wrong in play.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static partial class JumpingRodBuilder
    {
        /// <summary>The parts the wiring binds by name. A rename must fail the build, not the ride.</summary>
        private static readonly string[] RequiredParts =
        {
            "Mesh_JumpingRod_Shaft", "Mesh_JumpingRod_Piston", "Mesh_JumpingRod_Spring",
            "Mesh_JumpingRod_Foot", "Mesh_JumpingRod_SpringSeat",
        };

        private static GameObject LoadModel()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                Debug.LogError($"[JumpingRod] No model at {ModelPath}. Run " +
                               "_Source~/models/gear/jumping_rod_export.py first.");
            return model;
        }

        /// <summary>
        /// Nest the FBX and unpack it, so the parts can be reparented. An unpacked instance also
        /// stops a model reimport silently rearranging a prefab wired against it.
        /// </summary>
        private static GameObject NestModel(GameObject model, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(parent, false);
            instance.name = "Model";
            return instance;
        }

        private static Dictionary<string, Transform> PartsOf(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

        /// <summary>
        /// The parts the wiring binds, and the one assumption about the import that has to hold.
        /// Both fail loudly: a renamed mesh or a model that arrived lying down would otherwise
        /// produce a prefab that inspects perfectly and squashes sideways.
        /// </summary>
        private static bool Verify(Dictionary<string, Transform> parts, GameObject root)
        {
            string[] missing = RequiredParts.Where(n => !parts.ContainsKey(n)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError($"[JumpingRod] The model is missing: {string.Join(", ", missing)}. " +
                               "Was it renamed in jumping_rod.blend, or exported from the wrong file?");
                return false;
            }

            Vector3 size = MeasuredBounds(root.transform, root).size;
            if (size.y < size.x || size.y < size.z)
            {
                Debug.LogError($"[JumpingRod] The model did not arrive standing up — measured " +
                               $"{size.x:F2} x {size.y:F2} x {size.z:F2} m. The FBX axis conversion " +
                               "has changed; fix jumping_rod_export.py rather than rotating it here.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The piston carries the foot and the spring seat with it, so one driven transform moves
        /// all three. Left as siblings, the foot would stay planted in the sand while the piston it
        /// is bolted to slid up the shaft without it.
        /// </summary>
        private static Transform BindPistonAssembly(Dictionary<string, Transform> parts)
        {
            Transform piston = parts["Mesh_JumpingRod_Piston"];

            parts["Mesh_JumpingRod_Foot"].SetParent(piston, true);
            parts["Mesh_JumpingRod_SpringSeat"].SetParent(piston, true);

            return piston;
        }

        /// <summary>Renderer bounds of everything under <paramref name="root"/>, in <paramref name="space"/>.</summary>
        private static Bounds MeasuredBounds(Transform space, GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            var bounds = new Bounds(space.InverseTransformPoint(renderers[0].bounds.center),
                                    Vector3.zero);
            foreach (Renderer r in renderers)
                foreach (Vector3 corner in Corners(r.bounds))
                    bounds.Encapsulate(space.InverseTransformPoint(corner));

            return bounds;
        }

        /// <summary>
        /// All eight corners. A bounds must be re-measured corner by corner when it crosses into
        /// another space — transforming the centre and size alone is only correct while the two
        /// frames are axis-aligned, which is exactly what this build must not assume.
        /// </summary>
        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                         (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
        }

        private static Transform MakeChild(GameObject root, string name, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            t.localPosition = localPosition;
            return t;
        }

        private static Component AddByName(GameObject go, string fullName)
        {
            System.Type type = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(fullName))
                .FirstOrDefault(t => t != null);

            if (type == null)
            {
                Debug.LogError($"[JumpingRod] No such component: {fullName}.");
                return null;
            }

            return go.AddComponent(type);
        }
    }
}
