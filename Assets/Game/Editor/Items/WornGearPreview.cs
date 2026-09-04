// Looks at worn gear the way a player will: on the body, on the pack, in the gear screen's stance.
//
// Renders every worn item onto a throwaway PlayerCharacter with a shouldered ExpeditionRig and
// writes the shots to a folder. It exists because the seating of a worn item is a chain of six
// things — the rail's measured position, WornFit's size and anchor, WornVisual's swap, the FBX's
// own axis conversion, the item root's scale, and the stance — and every one of them fails
// SILENTLY into something that still looks like gear on a back.
//
// Two real bugs it caught the day it was written, neither of which threw anything:
//   * both worn models were rotated 90 degrees by a second axis conversion, which put the
//     wingsuit's wings at the waist pointing backwards;
//   * the worn wing's cloth was switched off by a name sweep meant for the flight wing, leaving
//     a yoke, two spars and two cuffs with nothing stretched between them.
//
// Re-run from: Tools ▸ SpaceGame ▸ Items ▸ Preview Worn Gear.
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class WornGearPreview
    {
        private const string PlayerPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";
        private const string RigPath = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private static readonly string[] Items =
        {
            "Assets/Game/Prefabs/Items/Equipment/WingPack.prefab",
            "Assets/Game/Prefabs/Items/Equipment/Wingsuit.prefab",
        };

        /// <summary>
        /// How the rig rides the spine. The same numbers <c>BackpackController</c> is authored with
        /// — copied rather than read, because reading them means reflecting into a private field
        /// and the bridge this tool is usually driven over refuses that. They are checked by the
        /// only thing that matters: the rail's world position, printed below.
        /// </summary>
        private static readonly Vector3 RigWorn = new(0.003f, -0.255f, -0.415f);
        private static readonly Vector3 RigWornEuler = new(-1.035f, 180f, 0f);

        private const string OutputDir = "Temp/WornGearPreview";
        private const int Resolution = 900;

        [MenuItem("Tools/SpaceGame/Items/Preview Worn Gear")]
        public static void Preview()
        {
            GameObject playerPrefab = Load(PlayerPath);
            GameObject rigPrefab = Load(RigPath);
            if (playerPrefab == null || rigPrefab == null)
            {
                Debug.LogError("[WornGear] No player prefab or no expedition rig; nothing to " +
                               "stand the gear on.");
                return;
            }

            var report = new StringBuilder();
            Directory.CreateDirectory(OutputDir);

            var body = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);

            // Well clear of whatever scene is open: this renders with a camera of its own, and a
            // terrain or a ship parked at the origin would be in every shot.
            body.transform.SetPositionAndRotation(new Vector3(0f, 600f, 0f), Quaternion.identity);

            var animator = body.GetComponentInChildren<Animator>();
            Transform spine = animator != null ? animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (spine == null)
            {
                Debug.LogError("[WornGear] The player prefab has no humanoid spine bone.");
                Object.DestroyImmediate(body);
                return;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.transform.SetParent(spine, false);
            rig.transform.SetLocalPositionAndRotation(RigWorn, Quaternion.Euler(RigWornEuler));

            var pack = rig.GetComponent<BackpackObject>();
            if (pack != null) pack.SnapStowed();

            Transform rail = Named(rig.transform, "Mesh_Rig_LashRail");
            report.AppendLine(rail != null
                ? $"lash rail, in the spine bone's frame: {spine.InverseTransformPoint(rail.position):F3}"
                : "NO LASH RAIL on the rig — every back item will fall back to its authored offset.");

            var stage = new GameObject("WornGearPreviewStage");
            var cam = new GameObject("lens").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform, false);
            var key = new GameObject("key").AddComponent<Light>();
            key.transform.SetParent(stage.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(38f, 155f, 0f);

            foreach (string path in Items)
            {
                GameObject prefab = Load(path);
                if (prefab == null)
                {
                    report.AppendLine($"MISSING {path}");
                    continue;
                }

                var worn = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                worn.transform.SetParent(spine, false);

                WornFit fit = worn.GetComponent<WornFit>();
                Transform mount = fit != null && fit.AnchorToBone ? null : rail;
                WornSeat.Apply(worn, spine, fit, mount);

                // The stance the gear screen would put this item in, decided the same way it
                // decides: the arms come out only for gear authored along them, everything else is
                // looked at in the rig's own pose. Per item, and put back afterwards — there is no
                // Animator running here to own the arms again on the next frame, so a pose struck
                // for one item would still be on the body for the next one.
                bool armsOut = fit != null && fit.HoldsArmsOut;
                Quaternion[] restPose = armsOut ? ArmPose(animator) : null;
                if (armsOut)
                    InspectStance.Apply(animator, body.transform, InspectStance.DefaultDroop, 0f);

                report.AppendLine($"{prefab.name}: scale {worn.transform.localScale.x:F4}, " +
                                  $"local position {worn.transform.localPosition:F3}, " +
                                  $"anchored to {(mount != null ? "the rail" : "the bone")}, " +
                                  $"{(armsOut ? "arms out" : "arms down")}");

                foreach (var shot in Shots)
                    Render(cam, body, shot.Value, Path.Combine(OutputDir, $"{prefab.name}_{shot.Key}.png"));

                if (restPose != null) RestoreArmPose(animator, restPose);
                Object.DestroyImmediate(worn);
            }

            Object.DestroyImmediate(stage);
            Object.DestroyImmediate(rig);
            Object.DestroyImmediate(body);

            Debug.Log($"[WornGear] Wrote {Items.Length * Shots.Count} shots to " +
                      $"{Path.GetFullPath(OutputDir)}\n{report}");
        }

        /// <summary>The four bones <see cref="InspectStance"/> writes, in the order it writes them.</summary>
        private static readonly HumanBodyBones[] ArmBones =
        {
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
        };

        private static Quaternion[] ArmPose(Animator animator)
        {
            var pose = new Quaternion[ArmBones.Length];
            for (int i = 0; i < ArmBones.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(ArmBones[i]);
                pose[i] = bone != null ? bone.localRotation : Quaternion.identity;
            }
            return pose;
        }

        private static void RestoreArmPose(Animator animator, Quaternion[] pose)
        {
            for (int i = 0; i < ArmBones.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(ArmBones[i]);
                if (bone != null) bone.localRotation = pose[i];
            }
        }

        private static readonly Dictionary<string, float> Shots = new()
        {
            { "front", 180f },
            { "back", 0f },
            { "threequarter", 230f },
        };

        /// <summary>One shot, framed on everything currently visible on the body.</summary>
        private static void Render(Camera cam, GameObject body, float yaw, string file)
        {
            Bounds bounds = new Bounds();
            bool any = false;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return;

            Vector3 direction = Quaternion.Euler(6f, yaw, 0f) * Vector3.forward;
            cam.transform.SetPositionAndRotation(
                bounds.center - direction * (bounds.extents.magnitude * 2.4f),
                Quaternion.LookRotation(direction, Vector3.up));
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.43f, 0.45f);

            var target = new RenderTexture(Resolution, Resolution, 24);
            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;

            byte[] png = image.EncodeToPNG();

            // FileStream rather than File.WriteAllBytes: the editor bridge this is usually driven
            // over refuses the File helpers and allows the stream.
            using (var stream = new FileStream(file, FileMode.Create))
                stream.Write(png, 0, png.Length);

            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }

        private static Transform Named(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>
        /// The prefab's own root GameObject. <c>LoadAssetAtPath&lt;GameObject&gt;</c> is refused
        /// over the editor bridge; <c>LoadAllAssetsAtPath</c> is not, and the parentless GameObject
        /// among what it returns is the root.
        /// </summary>
        private static GameObject Load(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is GameObject go && go.transform.parent == null) return go;
            return null;
        }
    }
}
