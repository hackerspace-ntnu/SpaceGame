// The Shock Harpoon: the Raxy hunter's electric harpoon gun model, made into an artifact that fires.
// Builds the item prefab, the one-arc VFX it crackles with, the electrocuted-body look (ShockLook and its
// materials), the item asset, its icon and its network entry.
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class ShockHarpoonBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/Tools/Hunting/tool_electricharpoongun.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ShockHarpoon.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/ShockHarpoon.asset";
        private const string ArcPath = "Assets/Game/Prefabs/VisualEffects/Lightning/HarpoonShockArc.prefab";
        private const string ArcMaterialPath = "Assets/Game/Art/Materials/Weapons/ConjurerLightning.mat";
        private const string LookPath = "Assets/Game/Resources/" + ShockLook.ResourcePath + ".asset";
        private const string XRayMaterialPath = "Assets/Game/Art/Materials/Effects/ShockXRay.mat";
        private const string BoneMaterialPath = "Assets/Game/Art/Materials/Effects/ShockBone.mat";
        private const string SocketMaterialPath = "Assets/Game/Art/Materials/Effects/ShockSocket.mat";
        private const string XRayShader = "SpaceGame/XRayShell";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";

        // Bone is lit, so its roundness reads, and glows so it shows in the dark and blooms; the
        // sockets are the one dark thing.
        private static readonly Color BoneColour = new Color(0.86f, 0.92f, 1f);
        private static readonly Color BoneGlow = new Color(0.45f, 0.8f, 1.5f);
        private static readonly Color SocketColour = new Color(0.01f, 0.02f, 0.04f);

        // Parts of the hunting-tool export (decorations.blend, Coll_Deco_Tool_ElectricHarpoonGun).
        private const string HarpoonPart = "Tool_ElectricHarpoonGun_Harpoon";
        private const string BarrelPart = "Tool_ElectricHarpoonGun_Barrel";
        // The cable is the model's own: the cloth-wrapped lead coiled on the gun's Wire part.
        private const string WirePart = "Tool_ElectricHarpoonGun_Wire";
        private const string CableMaterial = "Mat_Deco_Fabric_Rust";

        // Held as long as the Raxy's own (HandToolRoster), in both hands like the other long guns.
        private const float HoldSize = 1.21f;


        [MenuItem("Tools/SpaceGame/Items/Build Shock Harpoon")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException($"[ShockHarpoon] no model at {ModelPath}");

            LightningBoltEffect arc = BuildArc();
            BuildLook(arc);
            InventoryItem item = EnsureItem();
            GameObject prefab = BuildPrefab(model, item, arc);

            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();

            // CreateFor binds a first icon; GenerateFor re-renders one the item already owns.
            bool drawn = item.icon == null
                ? BatchIconGenerator.CreateFor(item, out string iconNote)
                : BatchIconGenerator.GenerateFor(item, out iconNote);
            if (!drawn)
                Debug.LogWarning($"[ShockHarpoon] no inventory icon: {iconNote}", item);

            string sync = NetworkPrefabRegistrar.Sync(out int added, out int total);
            Debug.Log($"[ShockHarpoon] built {PrefabPath}, {ItemPath}, {ArcPath} and {LookPath}. Network prefabs: {sync} ({added} added, {total} total).");
        }

        /// <summary>One short, thin arc. Local-only VFX: spawned by every machine, never networked.</summary>
        private static LightningBoltEffect BuildArc()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ArcMaterialPath);
            if (material == null) throw new FileNotFoundException($"[ShockHarpoon] no arc material at {ArcMaterialPath}");

            var root = new GameObject("HarpoonShockArc");
            try
            {
                var line = root.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;

                var bolt = root.AddComponent<LightningBoltEffect>();
                var so = new SerializedObject(bolt);
                SerializedFields.Set(so, "line", line);
                SerializedFields.SetInt(so, "segments", 10);
                SerializedFields.SetFloat(so, "spread", 0.18f);
                SerializedFields.SetFloat(so, "maxOffset", 0.12f);
                SerializedFields.SetFloat(so, "restrikeRate", 40f);
                SerializedFields.SetFloat(so, "duration", 0.12f);
                SerializedFields.SetFloat(so, "holdFraction", 0.3f);
                SerializedFields.SetFloat(so, "startWidth", 0.035f);
                SerializedFields.SetFloat(so, "endWidth", 0.02f);
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, ArcPath).GetComponent<LightningBoltEffect>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The electrocuted body: X-ray shell, bones, sockets, and every number of the effect.</summary>
        private static void BuildLook(LightningBoltEffect arc)
        {
            var look = AssetDatabase.LoadAssetAtPath<ShockLook>(LookPath);
            if (look == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LookPath) ?? ".");
                look = ScriptableObject.CreateInstance<ShockLook>();
                AssetDatabase.CreateAsset(look, LookPath);
            }

            look.xrayShell = EnsureMaterial(XRayMaterialPath, XRayShader);
            look.bone = EnsureMaterial(BoneMaterialPath, LitShader);
            look.bone.SetColor("_BaseColor", BoneColour);
            look.bone.SetColor("_EmissionColor", BoneGlow);
            look.bone.EnableKeyword("_EMISSION");
            look.bone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            look.socket = EnsureMaterial(SocketMaterialPath, UnlitShader);
            look.socket.SetColor("_BaseColor", SocketColour);
            look.arc = arc;

            EditorUtility.SetDirty(look.bone);
            EditorUtility.SetDirty(look.socket);
            EditorUtility.SetDirty(look);
        }

        private static Material EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new FileNotFoundException($"[ShockHarpoon] no shader '{shaderName}'");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.shader = shader;
                return material;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static InventoryItem EnsureItem()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }
            // Saves store this id, and only OnValidate would otherwise write it: see InventoryItem.ID.
            if (string.IsNullOrEmpty(item.ID)) item.ID = AssetDatabase.AssetPathToGUID(ItemPath);

            item.itemName = "Shock Harpoon";
            // Unmistakably a weapon pointed at you (MenacingItemTests).
            item.menacing = true;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static GameObject BuildPrefab(GameObject model, InventoryItem item, LightningBoltEffect arc)
        {
            var root = new GameObject("ShockHarpoon");
            try
            {
                var drawn = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                drawn.name = "Model";

                Transform harpoon = Part(drawn.transform, HarpoonPart);
                Transform barrel = Part(drawn.transform, BarrelPart);
                Material cable = MaterialNamed(Part(drawn.transform, WirePart).GetComponent<Renderer>(), CableMaterial);

                // The model's muzzle end is its +Z (HandTools.md: Blender -Y is Unity +Z).
                Vector3 forward = root.transform.forward;
                (Vector3 tail, Vector3 tip) = EndsAlong(harpoon, forward);

                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.position = barrel.TransformPoint(EndsAlong(barrel, forward).far);

                // The line is the cable's point list (GrappleRope writes it); WireTube draws it as
                // a round cable in the model's own cloth-wrapped lead.
                var wireObject = new GameObject("Wire");
                wireObject.transform.SetParent(root.transform, false);
                var wire = wireObject.AddComponent<LineRenderer>();
                wire.useWorldSpace = true;
                wire.enabled = false;
                wireObject.AddComponent<MeshFilter>();
                var cableSkin = wireObject.AddComponent<MeshRenderer>();
                cableSkin.sharedMaterial = cable;
                wireObject.AddComponent<WireTube>();

                WireCurrent current = BuildCurrent(root.transform, wire);

                ShockHarpoonArtifact artifact = root.AddComponent<ShockHarpoonArtifact>();
                var so = new SerializedObject(artifact);
                SerializedFields.Set(so, "loadedHarpoon", harpoon);
                SerializedFields.SetVector3(so, "harpoonTip", tip);
                SerializedFields.SetVector3(so, "harpoonTail", tail);
                SerializedFields.Set(so, "muzzle", muzzle);
                SerializedFields.Set(so, "wireLine", wire);
                SerializedFields.Set(so, "arcPrefab", arc);
                SerializedFields.Set(so, "current", current);
                SerializedFields.SetEnumByName(so, "useSoundId", "None");
                // A cable, not a rope: a light whip in flight, a shallow hang when set.
                SerializedFields.SetFloat(so, "wire.whipAmplitude", 0.25f);
                SerializedFields.SetFloat(so, "wire.whipWaves", 1.2f);
                SerializedFields.SetFloat(so, "wire.slackSag", 0.5f);
                SerializedFields.SetFloat(so, "wire.maxSag", 1.2f);
                so.ApplyModifiedPropertiesWithoutUndo();

                PickupableItem pickup = root.AddComponent<PickupableItem>();
                so = new SerializedObject(pickup);
                SerializedFields.Set(so, "item", item);
                SerializedFields.SetEnumByName(so, "pickupId", "InteractPickupMetal");
                so.ApplyModifiedPropertiesWithoutUndo();

                ItemGrip grip = root.AddComponent<ItemGrip>();
                so = new SerializedObject(grip);
                SerializedFields.SetInt(so, "holdStyle", (int)ItemGrip.HoldStyle.TwoHanded);
                SerializedFields.SetFloat(so, "holdSize", HoldSize);
                SerializedFields.Set(so, "sizeReference", drawn.transform);
                so.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<Unity.Netcode.NetworkObject>();
                root.AddComponent<SpaceGame.Core.NetRelay>();
                ItemWorldPresence.Apply(root);
                SaveablePolicy.Ensure(root, out _);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return CarriedGearItems.StampOnDisk(PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The arc laid along the cable while the body on the end is shocked.</summary>
        private static WireCurrent BuildCurrent(Transform root, LineRenderer wire)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ArcMaterialPath);
            var go = new GameObject("WireCurrent");
            go.transform.SetParent(root, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            var current = go.AddComponent<WireCurrent>();
            var so = new SerializedObject(current);
            SerializedFields.Set(so, "wire", wire);
            so.ApplyModifiedPropertiesWithoutUndo();
            return current;
        }

        private static Material MaterialNamed(Renderer renderer, string name)
        {
            foreach (Material m in renderer.sharedMaterials)
                if (m != null && m.name == name) return m;
            throw new InvalidDataException($"[ShockHarpoon] {renderer.name} has no material '{name}'.");
        }

        private static Transform Part(Transform model, string name)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new InvalidDataException($"[ShockHarpoon] {ModelPath} has no part '{name}'.");
        }

        /// <summary>
        /// The two ends of a part's mesh along a world direction, in the part's OWN space: the
        /// mesh's centre line pushed out to its nearest and farthest vertex along that direction.
        /// Measured off the vertices rather than the bounds, because the FBX node's rotation is
        /// whatever the .blend gave it (Artifacts.md Gotchas).
        /// </summary>
        private static (Vector3 near, Vector3 far) EndsAlong(Transform part, Vector3 worldDirection)
        {
            Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
            Vector3 along = part.InverseTransformDirection(worldDirection).normalized;

            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 v in mesh.vertices)
            {
                float d = Vector3.Dot(v, along);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }

            Vector3 centre = mesh.bounds.center;
            Vector3 axisPoint = centre - along * Vector3.Dot(centre, along);
            return (axisPoint + along * min, axisPoint + along * max);
        }
    }
}
