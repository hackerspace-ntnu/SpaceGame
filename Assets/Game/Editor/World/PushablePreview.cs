// Renders a Raxy with its hands on a cart, so the grip can be judged by looking: wheels on the ground, handles in the fists, the
// way the cart faces. The real code runs (CartPusher poses the cart and reaches the arms; the Raxy is the one HandToolRig poses in
// edit mode), on a flat ground with a collider so the wheels have something to find. No play mode and no scene of its own.
//
// Output: Temp/PushablePreview/<name>.png, one row: from the side, from three quarters in front, from behind, and a close look at
// the hands. Run from Tools > SpaceGame > Pushables > Preview Carts.
using System.IO;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class PushablePreview
    {
        private const string OutputDir = "Temp/PushablePreview";
        private const int Tile = 420;
        private const int Views = 4;
        private const float GripSettled = 10f;

        /// <summary>The scales a settlement places each cart at (1.0 for the two it does not place).</summary>
        private static readonly (string Prefab, float Scale)[] Cases =
        {
            ("Transport/Deco_Handcart", 1.4f), ("Transport/Deco_Handcart", 1.8f), ("Transport/Deco_Handcart_Hover", 1.8f),
            ("Tavern/Deco_FoodCart", 1.8f), ("Mining/Deco_MineCart", 1.45f),
        };

        [MenuItem("Tools/SpaceGame/Pushables/Preview Carts")]
        public static void PreviewAll()
        {
            foreach ((string prefab, float scale) in Cases) Preview(prefab, scale);
        }

        /// <summary>Renders one cart at one scale; returns the file written, or null when the cart is not authored.</summary>
        public static string Preview(string decorationPath, float scale, float armReach = -1f, float handsBelowShoulder = -1f)
        {
            string path = $"Assets/Game/Prefabs/Environment/Decorations/{decorationPath}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<Pushable>() == null)
            {
                Debug.LogWarning($"[PushablePreview] {path} is not an authored cart.");
                return null;
            }

            Directory.CreateDirectory(OutputDir);
            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return null;

            GameObject stage = new GameObject("PushablePreviewStage") { hideFlags = HideFlags.DontSave };
            try
            {
                Vector3 feet = rig.Body.transform.position;
                MakeGround(stage.transform, feet.y);
                var cam = new GameObject("lens").AddComponent<Camera>();
                cam.transform.SetParent(stage.transform, false);
                var key = new GameObject("key").AddComponent<Light>();
                key.transform.SetParent(stage.transform, false);
                key.type = LightType.Directional;
                key.intensity = 1.1f;
                key.transform.rotation = Quaternion.Euler(38f, 155f, 0f);

                rig.Pose(SpaceGame.Items.ItemGrip.HoldStyle.Carry);

                // The rig turns its body until the CHEST faces +Z; the pusher goes by the root's own forward, as in the game, so the
                // root is turned to face the way the body faces.
                float turn = Vector3.SignedAngle(rig.Body.transform.forward, rig.Facing * Vector3.forward, Vector3.up);
                rig.Body.transform.rotation = Quaternion.AngleAxis(turn, Vector3.up) * rig.Body.transform.rotation;
                var cartObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                cartObject.hideFlags = HideFlags.DontSave;
                cartObject.transform.SetParent(stage.transform, false);
                cartObject.transform.localScale = Vector3.one * scale;
                cartObject.transform.position = feet + new Vector3(4f, 0f, 4f);
                Physics.SyncTransforms();

                Pushable cart = cartObject.GetComponent<Pushable>();
                CartPusher pusher = CartPusher.On(rig.Body);
                if (armReach >= 0f || handsBelowShoulder >= 0f)
                {
                    var so = new SerializedObject(pusher);
                    if (armReach >= 0f) so.FindProperty("armReach").floatValue = armReach;
                    if (handsBelowShoulder >= 0f) so.FindProperty("handsBelowShoulder").floatValue = handsBelowShoulder;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (!pusher.Grip(cart))
                {
                    Debug.LogWarning($"[PushablePreview] the Raxy could not take hold of {path}.");
                    return null;
                }

                pusher.Follow(GripSettled);
                Physics.SyncTransforms();
                pusher.Follow(GripSettled);

                string file = $"{OutputDir}/{System.IO.Path.GetFileName(decorationPath)}_x{scale:0.00}.png";
                var sheet = new Texture2D(Tile * Views, Tile, TextureFormat.RGB24, false);
                Bounds all = BoundsOf(rig.Body);
                all.Encapsulate(BoundsOf(cartObject));
                Bounds hands = new Bounds(rig.Animator.GetBoneTransform(HumanBodyBones.RightHand).position, Vector3.one);
                hands.Encapsulate(rig.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
                hands.Encapsulate(cart.HandlePosition);
                hands.Expand(0.8f);

                using (rig.Bake())
                {
                    Shoot(cam, all, 90f, 6f, 1f, sheet, 0);
                    Shoot(cam, all, 215f, 14f, 1f, sheet, 1);
                    Shoot(cam, all, 330f, 10f, 1f, sheet, 2);
                    Shoot(cam, hands, 150f, 12f, 1f, sheet, 3);
                }

                File.WriteAllBytes(file, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);

                float left = Vector3.Distance(Palm(rig, HumanBodyBones.LeftHand), NearestHandle(cart, Palm(rig, HumanBodyBones.LeftHand)));
                float right = Vector3.Distance(Palm(rig, HumanBodyBones.RightHand), NearestHandle(cart, Palm(rig, HumanBodyBones.RightHand)));
                Debug.Log($"[PushablePreview] {file}: palms {left:0.00} / {right:0.00} m from the handles");
                return file;
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }
        }

        private static Vector3 Palm(HandToolRig rig, HumanBodyBones bone)
        {
            Transform hand = rig.Animator.GetBoneTransform(bone);
            return hand.TransformPoint(SpaceGame.Items.HandGripFrame.Derive(rig.Animator, hand, bone == HumanBodyBones.RightHand).LocalPosition);
        }

        private static Vector3 NearestHandle(Pushable cart, Vector3 hand) =>
            Vector3.Distance(cart.HandleLeft.position, hand) < Vector3.Distance(cart.HandleRight.position, hand) ? cart.HandleLeft.position : cart.HandleRight.position;

        private static void MakeGround(Transform parent, float y)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.hideFlags = HideFlags.DontSave;
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, y - 0.5f, 0f);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.5f, 0.38f), hideFlags = HideFlags.DontSave };
        }

        private static void Shoot(Camera cam, Bounds bounds, float yaw, float pitch, float tightness, Texture2D sheet, int column)
        {
            Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            cam.transform.SetPositionAndRotation(bounds.center - direction * (bounds.extents.magnitude * 2.4f / tightness),
                                                 Quaternion.LookRotation(direction, Vector3.up));
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.43f, 0.45f);

            var target = new RenderTexture(Tile, Tile, 24);
            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
            RenderTexture.active = previous;
            cam.targetTexture = null;

            sheet.SetPixels(column * Tile, 0, Tile, Tile, image.GetPixels());
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }

        private static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
