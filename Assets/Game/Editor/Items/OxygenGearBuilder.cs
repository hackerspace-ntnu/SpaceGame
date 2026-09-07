// Builds the two carried supply units the oxygen plant deals in:
//
//   Prefabs/Items/Supplies/OxygenTank.prefab  the tank, at any fill level
//   Prefabs/Items/Supplies/Battery.prefab     the slab battery the plant runs on
//
// plus one InventoryItem each under Resources/Items/Supplies and their entries in the network
// prefab list the NetworkManager actually reads.
//
// ONE tank prefab, not two. Until 2026-09-04 a tank's charge was its IDENTITY -- OxygenTank and
// OxygenTankEmpty were separate assets -- because ItemState does not replicate and an id does.
// A tank the player reads to a percent cannot work that way (a hundred assets for a hundred
// readings, and a hundred more per tank type), so the charge is a fraction on the instance and
// SupplyCharge carries it through every container. See SupplyReservoir and Oxygen.md.
//
// PowerCell was renamed to Battery in the same pass, by MoveAsset rather than by writing a new
// file: a move PRESERVES the GUID, and an InventoryItem's ID is its GUID, so every existing save
// file and every prefab reference keeps resolving. Creating Battery.asset fresh would have made a
// second item and orphaned every cell already in a player's world.
//
// A script rather than hand-authored YAML because these prefabs nest an imported FBX, and the file
// ids Unity assigns inside a model are decided at import time — a hand-written prefab referencing
// guessed ids loads with a missing model and no error.
//
// Re-runnable, and re-running REPLACES each prefab wholesale. Every tunable therefore belongs in
// the table below rather than in the Inspector, or the next run quietly undoes it.
//
// Re-run from: Tools > SpaceGame > Items > Build Oxygen Gear
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class OxygenGearBuilder
    {
        private const string TankModel = "Assets/Game/Art/Models/Props/oxygen_tank.fbx";
        private const string CellModel = "Assets/Game/Art/Models/Props/power_cell.fbx";

        private const string PrefabFolder = "Assets/Game/Prefabs/Items/Supplies/";
        private const string AssetFolder = "Assets/Game/Resources/Items/Supplies/";

        public const string TankAsset = AssetFolder + "OxygenTank.asset";
        public const string BatteryAsset = AssetFolder + "Battery.asset";

        /// <summary>
        /// What the battery's item asset and prefab used to be called. Renamed rather than
        /// replaced, so their GUIDs -- and therefore the item's ID in every save file -- survive.
        /// </summary>
        private const string LegacyCellAsset = AssetFolder + "PowerCell.asset";
        private const string LegacyCellPrefab = PrefabFolder + "PowerCell.prefab";

        /// <summary>
        /// The drained tank, merged into <see cref="TankAsset"/> on 2026-09-04.
        ///
        /// <para>
        /// DELETED rather than renamed, because unlike the cell it has no successor to be renamed
        /// INTO: the surviving tank asset already exists with its own GUID. A world saved with an
        /// empty tank in it therefore names an item this build cannot resolve, and the pack's own
        /// restore drops it with a warning naming the id. That is a real, one-time loss of one item
        /// per affected save, accepted because the alternative -- keeping a second tank asset alive
        /// as an alias -- is a permanent second way for a tank to exist.
        /// </para>
        /// </summary>
        private const string MergedEmptyTankAsset = AssetFolder + "OxygenTankEmpty.asset";
        private const string MergedEmptyTankPrefab = PrefabFolder + "OxygenTankEmpty.prefab";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>
        /// The two containers whose authored starting contents are where these items enter the
        /// game. Both builders read their starting lists off the existing prefab and write them
        /// back, so stocking them here survives a rebuild of either.
        /// </summary>
        private const string GearWallPrefab = "Assets/Game/Prefabs/Items/Equipment/InventoryWall.prefab";
        private const string ExpeditionRigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        /// <summary>
        /// Metres along the longest axis once held — the <c>BigTool</c> bracket of
        /// <see cref="ItemScaleLadder"/>.
        ///
        /// <para>
        /// Not the size the models were built at. Both objects are true-scale hardware (a 0.54 m
        /// bottle, a 0.55 m brick) and this hand is roughly 1.7x a human's, so life size reads as a
        /// toy in it. 0.90 is two thirds of the Dragon Bazooka anchor: bulky enough to read as a
        /// two-handed object and short of the guns, which is the silhouette the bracket buys.
        /// </para>
        /// </summary>
        private const float HoldSize = 0.90f;

        /// <summary>
        /// The bottle's size on the pack mat, in the frame <c>packSize</c> is authored in.
        ///
        /// <para>
        /// <b>Not the roster's usual "true size rounded up to the next pitch plus a cell".</b> That
        /// rule gave 0.72, which is right for an item standing on its base and wrong for this one:
        /// the bottle LIES DOWN now (see <see cref="Supply.Lay"/>), so its length is part of its
        /// FOOTPRINT rather than sticking up out of it, and 0.72 laid down is 4 x 8 = 32 cells —
        /// half the leaf, and more than either back panel can hold.
        /// </para>
        /// <para>
        /// 0.50 draws it 0.525 m long, which is life size to within 3%, and lands on <b>3 x 6 = 18
        /// cells</b>: exactly a back panel, and comfortably inside the leaf, the rack and both
        /// wings. The two cell counts come out at 2.53 and 5.56 of a cell — neither near an integer,
        /// which is the trap the power cell's own size note describes.
        /// </para>
        /// </summary>
        private const float TankPackSize = 0.50f;

        /// <summary>
        /// The cell's size on the mat: its true 0.55 m rounded up to the next pitch, and the extra
        /// cell of margin deliberately NOT taken.
        ///
        /// <para>
        /// At 0.72 the slab measures exactly the leaf's eight cells across, and eight cells is a
        /// float division landing exactly on an integer — which rounds either way and so decides at
        /// random whether the item fits the leaf at all. 0.63 is 7 x 3 = 21 cells with a column to
        /// spare. See Oxygen.md's Gotchas.
        /// </para>
        /// </summary>
        private const float CellPackSize = 0.63f;

        /// <summary>
        /// The bar's unlit track. Dark glass rather than black: a black gauge reads as a hole punched
        /// in the object instead of as an instrument with nothing in it.
        ///
        /// <para>
        /// The three LIT colours are not here. They are constants on <see cref="SupplyGauge"/>,
        /// because two of the three things that draw this bar are display copies with every script
        /// stripped off them — a colour authored into a prefab field would be honoured on the item in
        /// your hand and silently ignored on the same item lying on the mat beside it.
        /// </para>
        /// </summary>
        private static readonly Color GaugeEmpty = new Color(0.05f, 0.08f, 0.06f);

        /// <summary>The two bar materials, shared by every supply item — see EnsureGaugeMaterial.</summary>
        private const string FillMatPath = "Assets/Game/Art/Materials/Items/SupplyGaugeFill.mat";

        private const string TrackMatPath = "Assets/Game/Art/Materials/Items/SupplyGaugeTrack.mat";

        /// <summary>
        /// Metres the track stands off the gauge face, and the thickness of each of the two boxes.
        ///
        /// <para>
        /// Small, but never zero. Both models bury their gauge in a recess whose walls the bar has to
        /// clear, and two coplanar faces z-fight rather than stack — the trap the model scripts
        /// themselves record as "every decorative sub-part overshoots or is buried, never meets".
        /// At 0.8 mm the pair adds 2 mm to a 0.54 m bottle: below the eye at arm's length, and enough
        /// separation that no view angle flickers.
        /// </para>
        /// </summary>
        private const float BarProud = 0.0008f;

        private const float BarThickness = 0.0008f;

        /// <summary>
        /// How far the TRACK oversizes the lit geometry it covers, in metres.
        ///
        /// <para>
        /// It has to hide that geometry, not merely sit beside it: both models light their gauge
        /// permanently — the bottle's strip is an emissive material and the battery's ladder has
        /// three of five bars baked on — so anything left showing round the edge of the track reads
        /// as charge that is not there. 3 mm clears the battery's slot surrounds, which stand 2.5 mm
        /// proud of the bars themselves, and still lands inside the bottle's slate bezel.
        /// </para>
        /// </summary>
        private const float BarMargin = 0.003f;

        /// <summary>
        /// How far the fill is inset from the track across the bar, so the track reads as a channel
        /// the fill runs in rather than as a second slab the same size. Also what keeps a FULL bar
        /// legible as a bar: with no visible track behind it, 100% and "no gauge at all" look alike.
        /// </summary>
        private const float BarInset = 0.0015f;

        /// <summary>One supply unit: which model, which files, and how it reads.</summary>
        private readonly struct Supply
        {
            public readonly string Model;
            public readonly string Prefab;
            public readonly string Asset;
            public readonly string Name;

            /// <summary>What this holds, which is what decides the receptacle that takes it.</summary>
            public readonly SupplyKind Kind;

            /// <summary>A full one, in the kind's own unit: SECONDS of air, or WATT-HOURS.</summary>
            public readonly float Capacity;

            /// <summary>How full one enters the world, 0..1.</summary>
            public readonly float StartingCharge;

            /// <summary>The mesh carrying the gauge.</summary>
            public readonly string ReadoutPart;

            /// <summary>
            /// The emissive material on that mesh, BY NAME. Never a submesh index: these models are
            /// one mesh per PART and up to nine materials deep, so the index is an accident of the
            /// export order, and a stale one silently picks out the enamel instead of the lamp -
            /// which looks like a broken shader rather than like a wrong number. The sibling
            /// <c>OxygenGeneratorBuilder</c> has always resolved its lamps this way.
            /// </summary>
            public readonly string ReadoutMaterial;

            public readonly float PackSize;

            /// <summary>
            /// Euler degrees the MODEL is turned by inside the prefab, or zero for one that is
            /// already lying the way it should.
            ///
            /// <para>
            /// This is the only thing that decides how an item lies on a surface, because
            /// <c>BackpackItemVisual</c> seats a copy with the ITEM's own up along the surface
            /// NORMAL and never turns it over: a bottle modelled standing on its skirt stands off
            /// a vertical back panel by its whole length. Turning the geometry is the fix, and the
            /// grip's <c>rotationOffset</c> is given the inverse so the pose in the HAND does not
            /// move — the rule <c>ItemPackOrientation</c> exists to apply, put in the builder
            /// instead because a builder-owned prefab is replaced wholesale on the next run.
            /// </para>
            /// </summary>
            public readonly Vector3 Lay;

            public Supply(string model, string file, string name, SupplyKind kind, float capacity,
                          float startingCharge, string readoutPart, string readoutMaterial,
                          float packSize, Vector3 lay)
            {
                Lay = lay;
                Model = model;
                Prefab = PrefabFolder + file + ".prefab";
                Asset = AssetFolder + file + ".asset";
                Name = name;
                Kind = kind;
                Capacity = capacity;
                StartingCharge = startingCharge;
                ReadoutPart = readoutPart;
                ReadoutMaterial = readoutMaterial;
                PackSize = packSize;
            }
        }

        /// <summary>The bottle's contents window: a dark plate carrying one lit strip.</summary>
        private const string TankGauge = "Mesh_OxygenTank_Gauge";

        /// <summary>
        /// The battery's accent plate, which carries its charge ladder.
        ///
        /// <para>
        /// It had no readout wired at all until the fill bar existed, and the reason was sound while
        /// it lasted: the ladder is <b>five bars with three baked lit</b>, and lit and unlit are not
        /// separable - the lit three share one submesh with nothing else and the unlit two share
        /// theirs with the surround plate, so no amount of submesh painting can drive them. The
        /// charge was read off the machine and off the reticle instead. The bar COVERS the ladder
        /// rather than driving it.
        /// </para>
        /// </summary>
        private const string CellGauge = "Mesh_PowerCell_Slab_Face";

        /// <summary>
        /// The palette's readout emissive, and the material both gauges are found by. It is the lit
        /// geometry on each model - the bottle's strip, the battery's three baked-on bars - which is
        /// what says where the instrument is and how big it is.
        /// </summary>
        private const string GaugeMaterial = "Mat_Emissive_Green_CRT";

        /// <summary>
        /// A quarter turn back about X, which lays the bottle on its side and points its GAUGE
        /// along the item's own +Y — the axis every surface seats along its normal, so the gauge
        /// ends up facing out of whatever the bottle is lying on and is readable there.
        ///
        /// <para>
        /// The sign is the half that is easy to get wrong, so it is derived rather than guessed:
        /// Unity's X rotation carries +Z to (0, sin, cos), so at -90 the model's +Z — its gauge
        /// flank, measured at z +0.0975 on the barrel — lands on +Y, and at +90 it lands on -Y,
        /// buried in the surface. <c>OxygenSystemTests.TheBottleLiesDownWithItsGaugeOutward</c>
        /// measures it off the built prefab rather than trusting this note.
        /// </para>
        /// </summary>
        private static readonly Vector3 BottleLiesDown = new Vector3(-90f, 0f, 0f);

        /// <summary>
        /// Seconds of air a full tank holds: thirty minutes. The one number that decides whether
        /// the open world reads as a journey or as a stopwatch, and the only one to move when it
        /// reads wrong -- SuitOxygen's drain is fixed at one second per second so that a capacity
        /// IS a duration.
        /// </summary>
        private const float TankSeconds = 30f * 60f;

        /// <summary>Watt-hours a full battery holds. Twenty-five tank fills at the plant's 4%.</summary>
        private const float BatteryWattHours = 1000f;

        /// <summary>
        /// Both enter the world FULL.
        ///
        /// <para>
        /// A tank stocked empty was the obvious alternative -- an empty tank is what the plant is
        /// for -- and it is wrong now that running out of air kills: the arrival is a crash landing,
        /// and starting it with sixty seconds of suit reserve and nothing else makes the opening
        /// minute a scramble against a system the player has not been taught yet.
        /// </para>
        /// </summary>
        private const float StockedFull = 1f;

        private static readonly Supply[] Roster =
        {
            new(TankModel, "OxygenTank", "Oxygen Tank", SupplyKind.Oxygen, TankSeconds, StockedFull,
                TankGauge, GaugeMaterial, TankPackSize, BottleLiesDown),

            new(CellModel, "Battery", "Battery", SupplyKind.Power, BatteryWattHours, StockedFull,
                CellGauge, GaugeMaterial, CellPackSize, Vector3.zero),
        };

        [MenuItem("Tools/SpaceGame/Items/Build Oxygen Gear")]
        public static void Build()
        {
            MigrateLegacyAssets();

            var built = new List<GameObject>();

            foreach (Supply supply in Roster)
            {
                GameObject prefab = BuildOne(supply);
                if (prefab == null) return;
                built.Add(prefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // A NetworkObject added by script ships GlobalObjectIdHash 0, and NGO silently drops
            // all but one prefab when several share a hash. The hash is filled in by the
            // component's own OnValidate, which only resolves against the saved ASSET — so each
            // prefab has to be re-imported and reserialized or the corrected value, and the
            // SaveableEntity's prefabId beside it, never reach the YAML.
            string[] paths = Roster.Select(s => s.Prefab).ToArray();
            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.Refresh();

            RouteIntoTheGame();

            if (!Verify()) return;

            Debug.Log("[OxygenGear] Built " + built.Count + " supply items under " + PrefabFolder +
                      " and " + AssetFolder + ". Run Tools/Generate All Item Icons for the " +
                      "inventory icons, then Tools/SpaceGame/Build Oxygen Generator Prefab.");
        }

        // ─────────────────────────── One item ───────────────────────────

        private static GameObject BuildOne(Supply supply)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(supply.Model);
            if (model == null)
            {
                Debug.LogError("[OxygenGear] No model at " + supply.Model +
                               " — run the _Source~ export for it first.");
                return null;
            }

            GameObject root = BuildHierarchy(model, supply);
            if (root == null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(supply.Prefab) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, supply.Prefab);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError("[OxygenGear] Prefab save failed for " + supply.Prefab +
                               " — is this a read-only editor clone?");
                return null;
            }

            InventoryItem asset = EnsureItemAsset(supply, prefab);
            WireItemIntoPickup(prefab, asset);
            RegisterNetworkPrefab(prefab);

            return prefab;
        }

        private static GameObject BuildHierarchy(GameObject model, Supply supply)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(supply.Prefab));

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // Before anything is measured: the grip point, the footprint and the fitted collider
            // are all read off the ROOT's frame, so a turn applied after them would leave every
            // one describing a pose the item no longer has.
            modelInstance.transform.localRotation = Quaternion.Euler(supply.Lay);

            Renderer readout = FindReadout(modelInstance, supply, out int gaugeSubmesh);
            if (readout == null)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            // ── Pickup / world presence ──
            // One prefab is both the thing in the hand and the thing lying in the sand, so it
            // carries both sets of components, component for component with the other items.
            NetworkObject netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddByName(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider the shape of the item, the world sizing and the netcode that
            // lets another machine watch it be shoved about. One shared block — see
            // ItemWorldPresence for what nine hand-written copies of it cost.
            ItemWorldPresence.Apply(root);

            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            // ── Grip ──
            // The hand closes on the middle of the object rather than on its origin, which for both
            // of these models is a corner of the base. Measured, not typed, so a remodel follows.
            Transform gripPoint = AddGripPoint(root, modelInstance);

            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            Field.Set(gripSo, "gripPoint", gripPoint);
            Field.SetFloat(gripSo, "holdSize", HoldSize);
            Field.SetFloat(gripSo, "packSize", supply.PackSize);
            Field.Set(gripSo, "sizeReference", modelInstance.transform);

            // The exact inverse of the turn above. EquipItemSocket seats an item as
            // `handRotation * Euler(rotationOffset)`, so turning the contents by R and the offset
            // by R-inverse multiplies back out: the mat gets the new lie and the HAND keeps the
            // pose it was tuned with.
            Field.SetVector3(gripSo, "rotationOffset",
                             Quaternion.Inverse(Quaternion.Euler(supply.Lay)).eulerAngles);

            // Both are hugged rather than gripped: a pressure bottle with no handle (its wire bail
            // was cut in the model) and a two-handed brick.
            Field.SetEnum(gripSo, "holdStyle", (int)ItemGrip.HoldStyle.TwoHanded);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            // ── The item's own behaviour ──
            //
            // Two components, because a reservoir and a verb are two things: the tank is a
            // SupplyReservoir any item may hold, and DockableSupply is the verb-less UsableItem
            // that gives this one its hold pose and nothing else. DockableSupply's
            // [RequireComponent] adds the reservoir for us, so it is fetched rather than added --
            // a second AddComponent of a [DisallowMultipleComponent] type returns null.
            root.AddComponent<DockableSupply>();

            var reservoir = root.GetComponent<SupplyReservoir>();
            var supplySo = new SerializedObject(reservoir);
            Field.SetEnum(supplySo, "kind", (int)supply.Kind);
            Field.SetFloat(supplySo, "capacity", supply.Capacity);
            Field.SetFloat(supplySo, "startingCharge", supply.StartingCharge);
            Field.Set(supplySo, "readout", readout);
            supplySo.ApplyModifiedPropertiesWithoutUndo();

            // The drain policy is left at its defaults, all zero: nothing empties a bottle or a
            // battery by carrying it. They are drained by the machine they are fitted to and
            // refilled by the plant, both of which write the charge directly.

            // Last, because it is measured off the model in the ROOT's frame and the lay-down turn
            // above is part of that frame.
            if (!AddFillBar(root, modelInstance, readout, gaugeSubmesh, supply))
            {
                Object.DestroyImmediate(root);
                return null;
            }

            return root;
        }

        // ─────────────────────────── The fill bar ───────────────────────────

        /// <summary>
        /// Build the charge bar over the model's own gauge face: a dark track, and a fill that grows
        /// from one end of it. Both are plain boxes on the PREFAB ROOT — see <see cref="SupplyGauge"/>
        /// for why the runtime finds them by name.
        ///
        /// <para>
        /// Children of the root rather than of the gauge mesh, for the reason
        /// <see cref="AddGripPoint"/> gives: this prefab owns its own transforms and an FBX re-import
        /// cannot null them. It also puts every number below in METRES, because the root is at unit
        /// scale while the imported nodes under it are not.
        /// </para>
        /// </summary>
        private static bool AddFillBar(GameObject root, GameObject modelInstance, Renderer readout,
                                       int submesh, Supply supply)
        {
            if (!MeasureGauge(root, readout, submesh, modelInstance, out Vector3 centre,
                              out Quaternion rotation, out Vector2 size))
                return false;

            Material fillMaterial = EnsureGaugeMaterial(FillMatPath, readout, submesh, SupplyGauge.Full);
            Material trackMaterial = EnsureGaugeMaterial(TrackMatPath, readout, submesh, GaugeEmpty);
            if (fillMaterial == null || trackMaterial == null) return false;

            Vector3 outward = rotation * Vector3.forward;

            // The track first and the fill a step further out, so neither z-fights the model face
            // nor the other.
            Box(root.transform, SupplyGauge.TrackName, trackMaterial,
                centre + outward * BarProud, rotation,
                new Vector3(size.x, size.y, BarThickness));

            // The anchor sits at the LOW end of the bar and is scaled along its own +X; the box hangs
            // off it by half a length so the pair grows from that end. Baked at the authored starting
            // charge, which is what makes the prefab, its generated icon and every stripped display
            // copy read correctly with no script having run.
            var anchor = new GameObject(SupplyGauge.AnchorName).transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = centre + outward * (BarProud + BarThickness)
                                          - rotation * Vector3.right * (size.x * 0.5f);
            anchor.localRotation = rotation;
            anchor.localScale = new Vector3(supply.StartingCharge, 1f, 1f);

            // Twice the track's thickness, so the fill is BURIED in it rather than resting on it.
            // Two parallel faces meeting exactly on a plane is the flicker the model scripts
            // themselves warn about ("every decorative sub-part overshoots or is buried, never
            // meets"), and the fill's back face would otherwise land precisely on the track's front.
            Box(anchor, SupplyGauge.FillName, fillMaterial,
                new Vector3(size.x * 0.5f, 0f, 0f), Quaternion.identity,
                new Vector3(size.x, Mathf.Max(size.y - BarInset * 2f, BarInset), BarThickness * 2f));

            return true;
        }

        /// <summary>
        /// Where the bar goes, in the root's frame: the middle of the gauge face, a rotation whose +X
        /// runs along the bar and whose +Z points out of the model, and the bar's size in metres.
        ///
        /// <para>
        /// Measured off the emissive submesh's own vertices rather than typed in, because both
        /// <c>.blend</c> files are hand-edited finals whose numbers no longer match the scripts that
        /// first generated them. The lit geometry IS the instrument: on the bottle it is the contents
        /// strip, on the battery the three baked-on ladder bars.
        /// </para>
        /// <para>
        /// <b>Then mirrored to symmetry about the middle of the GAUGE MESH.</b> That is what makes one
        /// rule fit both. The battery's lit three-of-five cover only one side of a ladder that is
        /// symmetric about its plate, and mirroring recovers all five; the bottle's strip is already
        /// centred on its own plate, so mirroring is a no-op there. Reading the lit extent literally
        /// on the battery builds a bar 60% the length of the housing it sits in, and it looks
        /// deliberate rather than broken.
        /// </para>
        /// <para>
        /// The reference is the gauge mesh and <b>not the whole model</b>, which was the first
        /// attempt and is wrong for a reason worth keeping: a model's bounds centre is pulled about
        /// by everything it happens to contain, and the bottle's sits 6 mm off its own gauge. Mirrored
        /// about that, the bar came out 36% too long and visibly off-centre on its plate — while the
        /// battery, which is symmetric, looked perfect. The gauge mesh IS the instrument's housing,
        /// so its middle is the middle of the instrument by construction.
        /// </para>
        /// </summary>
        private static bool MeasureGauge(GameObject root, Renderer readout, int submesh,
                                         GameObject modelInstance, out Vector3 centre,
                                         out Quaternion rotation, out Vector2 size)
        {
            centre = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector2.zero;

            var filter = readout.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || submesh >= mesh.subMeshCount)
            {
                Debug.LogError("[OxygenGear] " + readout.name + " has no readable mesh for submesh " +
                               submesh + ", so the gauge cannot be measured.");
                return false;
            }

            int[] triangles = mesh.GetTriangles(submesh);
            if (triangles.Length == 0)
            {
                Debug.LogError("[OxygenGear] Submesh " + submesh + " of " + readout.name +
                               " is empty, so there is no lit geometry to size the bar from.");
                return false;
            }

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * readout.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;

            var lit = new Bounds(toRoot.MultiplyPoint3x4(vertices[triangles[0]]), Vector3.zero);
            for (int i = 1; i < triangles.Length; i++)
                lit.Encapsulate(toRoot.MultiplyPoint3x4(vertices[triangles[i]]));

            // The whole gauge mesh: the plate, bezel and surround the lit part is set into.
            var plate = new Bounds(toRoot.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
                plate.Encapsulate(toRoot.MultiplyPoint3x4(vertices[i]));

            // The gauge face is a thin slab: its shallowest axis points out of the model, and the
            // longest of the other two is the one a bar runs along.
            int thin = Axis(lit.size, smallest: true);
            int along = Axis(lit.size, smallest: false, exclude: thin);
            int across = 3 - thin - along;

            Bounds model = ItemBounds.Measure(root, modelInstance.transform);

            Vector3 outward = Vector3.zero;
            outward[thin] = lit.center[thin] >= model.center[thin] ? 1f : -1f;

            Vector3 alongAxis = Vector3.zero;
            alongAxis[along] = 1f;

            // Mirror about the gauge's own midline — see the note above.
            float middle = plate.center[along];
            float half = Mathf.Max(Mathf.Abs(lit.min[along] - middle),
                                   Mathf.Abs(lit.max[along] - middle));

            centre = lit.center;
            centre[along] = middle;
            centre[thin] = outward[thin] > 0f ? lit.max[thin] : lit.min[thin];

            size = new Vector2(half * 2f + BarMargin * 2f, lit.size[across] + BarMargin * 2f);

            var basis = new Matrix4x4();
            basis.SetColumn(0, alongAxis);
            basis.SetColumn(1, Vector3.Cross(outward, alongAxis));
            basis.SetColumn(2, outward);
            basis.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            rotation = basis.rotation;

            return true;
        }

        /// <summary>The index of the largest or smallest component of <paramref name="v"/>.</summary>
        private static int Axis(Vector3 v, bool smallest, int exclude = -1)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i == exclude) continue;
                if (best < 0 || (smallest ? v[i] < v[best] : v[i] > v[best])) best = i;
            }

            return best;
        }

        /// <summary>One box of the bar, sized in metres in its parent's frame.</summary>
        private static void Box(Transform parent, string name, Material material,
                                Vector3 position, Quaternion rotation, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;

            // A primitive arrives with a collider. On an item that is picked up, dropped and stowed
            // that collider would join the prefab's own fitted one and change what the world, the
            // pack and the interaction ray all think this shape is.
            Object.DestroyImmediate(box.GetComponent<Collider>());

            box.GetComponent<MeshRenderer>().sharedMaterial = material;

            Transform t = box.transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = size;
        }

        /// <summary>
        /// One of the two bar materials, as a copy of the model's OWN gauge material so it inherits
        /// the palette's shader and emissive setup rather than guessing at one.
        ///
        /// <para>
        /// Real material assets and not a <c>MaterialPropertyBlock</c>, because a property block is
        /// not serialized and <c>Awake</c> never runs on a prefab in the editor: a block-only bar
        /// would be right in play and wrong in every generated icon, on the pack mat and on the
        /// ship's gear wall. The runtime still tints the fill through <see cref="SupplyGauge"/> —
        /// this is what it reads as before anything has painted it.
        /// </para>
        /// </summary>
        private static Material EnsureGaugeMaterial(string path, Renderer readout, int submesh,
                                                    Color colour)
        {
            Material source = readout.sharedMaterials[submesh];
            if (source == null)
            {
                Debug.LogError("[OxygenGear] The gauge submesh has no material to copy for " + path);
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }

            // Re-pointed and re-coloured on every run, not only at creation: a .mat freezes the
            // shader defaults it was BORN with, so one written against an older palette keeps them
            // forever and the constants here quietly become a lie.
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            EmissiveLamp.Bake(material, colour);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// A child at the measured middle of the model, which is what lands in the palm.
        ///
        /// A child of the ROOT rather than of the model, so the wiring points at a transform this
        /// prefab owns and a re-import of the FBX cannot null it.
        /// </summary>
        private static Transform AddGripPoint(GameObject root, GameObject modelInstance)
        {
            Bounds bounds = ItemBounds.Measure(root, modelInstance.transform);

            var point = new GameObject("GripPoint").transform;
            point.SetParent(root.transform, false);
            point.localPosition = bounds.center;
            return point;
        }

        /// <summary>
        /// The mesh carrying the gauge, and the submesh its emissive material sits on — resolved by
        /// NAME, because the index is an accident of the export order.
        /// </summary>
        private static Renderer FindReadout(GameObject modelInstance, Supply supply, out int submesh)
        {
            submesh = -1;

            Renderer found = modelInstance.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(r => r.name == supply.ReadoutPart);

            if (found == null)
            {
                Debug.LogError("[OxygenGear] No '" + supply.ReadoutPart + "' in " + supply.Model +
                               " — the model script names the parts this builder wires; re-export, " +
                               "or update both.");
                return null;
            }

            Material[] materials = found.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                // Unity suffixes an imported material with " (Instance)" in some contexts, and the
                // palette's own names are unique, so a prefix test is both enough and stable.
                if (materials[i] == null || !materials[i].name.StartsWith(supply.ReadoutMaterial))
                    continue;

                submesh = i;
                break;
            }

            if (submesh < 0)
            {
                Debug.LogError("[OxygenGear] " + supply.ReadoutPart + " in " + supply.Model +
                               " carries no '" + supply.ReadoutMaterial + "' among its " +
                               materials.Length + " materials (" +
                               string.Join(", ", materials.Select(m => m != null ? m.name : "null")) +
                               "). The gauge is found by that material; the model changed.");
                return null;
            }

            return found;
        }

        // ─────────────────────────── The item asset ───────────────────────────

        private static InventoryItem EnsureItemAsset(Supply supply, GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(supply.Asset) ?? ".");

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(supply.Asset);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, supply.Asset);
            }

            asset.itemName = supply.Name;
            asset.itemPrefab = prefab;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one of
        /// the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem asset)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            if (pickup == null)
            {
                Debug.LogError("[OxygenGear] PickupableItem missing on " + prefab.name + ".");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        /// Netcode regenerates and nothing consults. Dropping a hotbar slot routes through
        /// PlayerDropService to GameServices.World.Spawn, which needs the entry — and it fails on
        /// CLIENTS ONLY, so playing as the host can never find it missing.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError("[OxygenGear] No list at " + NetworkPrefabsPath + ".");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ─────────────────────────── Into the game ───────────────────────────

        /// <summary>
        /// Stock the two containers the player actually starts from.
        ///
        /// <para>
        /// This is the whole route in. Without it the plant is unreachable — a machine that needs a
        /// cell nobody can obtain — and these two are the diegetic answer: the ship's gear wall
        /// holds the stores (a spare bottle and the cell, three metres from the plant that eats
        /// them), and the expedition rig carries the bottle you set out with.
        /// </para>
        /// <para>
        /// The rig's is not a nicety. Until 2026-09-03 an oxygen bottle was MODELLED INTO the rig —
        /// <c>Mesh_Rig_OxygenTank</c>, authored as "a fixed fitting, not an item" — so the pack
        /// showed a bottle the player could see and could never take off. That geometry is deleted;
        /// this is what puts a real one back, and a real one lifts off the mat and goes on again.
        /// </para>
        /// <para>
        /// The rig gets the CHARGED bottle (it replaces one that always read as full) and the wall
        /// the drained one, so the plant has something to do on the first visit.
        /// </para>
        /// </summary>
        private static void RouteIntoTheGame()
        {
            var tank = AssetDatabase.LoadAssetAtPath<InventoryItem>(TankAsset);
            var battery = AssetDatabase.LoadAssetAtPath<InventoryItem>(BatteryAsset);
            if (tank == null || battery == null) return;

            // A SPARE tank on the wall as well as the one on the rig, which the container can now
            // actually hold: PackItemKey gives every placement its own instance handle, so two of
            // one asset are two placements. Before it, a second tank was silently refused.
            Stock(GearWallPrefab, "the ship's gear wall", tank, battery);
            Stock(ExpeditionRigPrefab, "the expedition rig", tank);
        }

        /// <summary>
        /// Rename what 2026-09-04 renamed, and delete what it merged.
        ///
        /// <para>
        /// <see cref="AssetDatabase.MoveAsset"/> rather than a fresh <c>CreateAsset</c>, because a
        /// move preserves the GUID and an <c>InventoryItem</c>'s <c>ID</c> IS its GUID. Every save
        /// file naming a power cell, and every prefab holding a reference to one, keeps resolving
        /// to the same item under its new name.
        /// </para>
        /// <para>
        /// Idempotent: each step is skipped once its destination exists, so the ordinary run of
        /// this builder on an already-migrated project does nothing at all.
        /// </para>
        /// </summary>
        private static void MigrateLegacyAssets()
        {
            Rename(LegacyCellAsset, BatteryAsset);
            Rename(LegacyCellPrefab, PrefabFolder + "Battery.prefab");

            Delete(MergedEmptyTankAsset);
            Delete(MergedEmptyTankPrefab);
        }

        private static void Rename(string from, string to)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(from) == null) return;
            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null) return;

            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[OxygenGear] Could not rename " + from + " to " + to + ": " + error);
                return;
            }

            Debug.Log("[OxygenGear] Renamed " + from + " to " + to + " (GUID preserved).");
        }

        private static void Delete(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) == null) return;

            AssetDatabase.DeleteAsset(path);
            Debug.LogWarning("[OxygenGear] Deleted " + path + " — a tank's charge is a number now, " +
                             "not a second asset. Worlds saved with an empty tank in them will log " +
                             "one unresolved item and lose it.");
        }

        /// <summary>
        /// Add items to one <see cref="PackContainer"/>'s authored starting list, skipping any it
        /// already holds. Idempotent, because this runs on every build of the roster.
        /// </summary>
        private static void Stock(string prefabPath, string what, params InventoryItem[] items)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var container = prefab != null ? prefab.GetComponent<PackContainer>() : null;

            if (container == null)
            {
                Debug.LogWarning("[OxygenGear] No PackContainer at " + prefabPath +
                                 ", so the new items are in the registry but nowhere in the world.");
                return;
            }

            var so = new SerializedObject(container);
            SerializedProperty list = so.FindProperty("startingMainItems");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("[OxygenGear] " + what + " has no startingMainItems list.");
                return;
            }

            // Dangling entries first. Deleting an item asset does not remove references to it —
            // it nulls them in place, silently — so merging the two tanks left a hole in this very
            // list, and a hole here is an item the container tries to stow on every spawn and
            // cannot resolve. Pruned before the adds so the count below is honest.
            int pruned = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != null) continue;

                list.DeleteArrayElementAtIndex(i);
                pruned++;
            }

            var added = new List<string>();
            foreach (InventoryItem item in items)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == item) present = true;

                if (present) continue;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
                added.Add(item.itemName);
            }

            if (added.Count == 0 && pruned == 0) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);

            Debug.Log("[OxygenGear] " + what + ": stocked " +
                      (added.Count > 0 ? string.Join(", ", added) : "nothing") +
                      (pruned > 0 ? ", pruned " + pruned + " dangling entry/entries" : "") + ".");
        }

        // ─────────────────────────── Proof ───────────────────────────

        /// <summary>
        /// Re-read everything this run wrote, off disk, and assert it landed. Unity's AssetDatabase
        /// goes read-only in some sessions and discards prefab and asset saves outright without
        /// raising anything, so a run that reports success having written nothing is a real outcome
        /// rather than a hypothetical one.
        /// </summary>
        private static bool Verify()
        {
            var problems = new List<string>();
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);

            foreach (Supply supply in Roster)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(supply.Prefab);
                var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(supply.Asset);

                if (prefab == null) { problems.Add("no prefab at " + supply.Prefab); continue; }
                if (asset == null) problems.Add("no item asset at " + supply.Asset);
                else if (asset.itemPrefab != prefab)
                    problems.Add(supply.Asset + " does not point at its prefab");

                var netObject = prefab.GetComponent<NetworkObject>();
                if (netObject == null) problems.Add(supply.Name + " has no NetworkObject");
                else if (netObject.PrefabIdHash == 0)
                    problems.Add(supply.Name + " has GlobalObjectIdHash 0");

                var entity = prefab.GetComponent<SaveableEntity>();
                if (entity == null) problems.Add(supply.Name + " has no SaveableEntity");
                else if (string.IsNullOrEmpty(entity.PrefabId))
                    problems.Add(supply.Name + " has no stamped prefabId — run " +
                                 "Tools/Save System/Wire Saveable Prefabs, stopped");

                var grip = prefab.GetComponent<ItemGrip>();
                if (grip == null) problems.Add(supply.Name + " has no ItemGrip");
                else
                {
                    if (!Mathf.Approximately(grip.HoldSize, HoldSize))
                        problems.Add(supply.Name + " holdSize reads " + grip.HoldSize.ToString("F3"));
                    if (!Mathf.Approximately(grip.PackSize, supply.PackSize))
                        problems.Add(supply.Name + " packSize reads " + grip.PackSize.ToString("F3"));
                }

                if (prefab.GetComponent<DockableSupply>() == null)
                    problems.Add(supply.Name + " has no DockableSupply, so it equips with no hold " +
                                 "pose and carries no item state");

                var supplyItem = prefab.GetComponent<SupplyReservoir>();
                if (supplyItem == null) problems.Add(supply.Name + " has no SupplyReservoir");
                else
                {
                    // The three numbers that make a reservoir what it is. Checked off the saved
                    // asset rather than trusted, because a capacity that failed to serialise reads
                    // as the component's own default -- a battery that silently became a
                    // thirty-minute tank would still fill, still drain and still be wrong.
                    if (supplyItem.Kind != supply.Kind)
                        problems.Add(supply.Name + " is the wrong SupplyKind");
                    if (!Mathf.Approximately(supplyItem.Capacity, supply.Capacity))
                        problems.Add(supply.Name + " capacity reads " + supplyItem.Capacity);
                    if (!Mathf.Approximately(supplyItem.StartingCharge, supply.StartingCharge))
                        problems.Add(supply.Name + " startingCharge reads " + supplyItem.StartingCharge);
                }

                // The bar, off disk. It is generated geometry rather than a serialized field, so a
                // discarded prefab save loses it silently and leaves an item whose gauge simply
                // never moves — which looks like a broken drain rather than like a failed build.
                SupplyGauge gauge = SupplyGauge.Bind(prefab.transform);
                if (!gauge.Exists)
                    problems.Add(supply.Name + " has no " + SupplyGauge.AnchorName +
                                 " — the fill bar did not survive the prefab save");
                else if (prefab.transform.Find(SupplyGauge.TrackName) == null)
                    problems.Add(supply.Name + " has no " + SupplyGauge.TrackName +
                                 ", so its permanently-lit gauge geometry shows through an empty bar");

                problems.AddRange(ItemWorldPresence.ProblemsWith(prefab)
                                                   .Select(p => supply.Name + ": " + p));

                if (list == null || !list.Contains(prefab))
                    problems.Add(supply.Name + " is not in " + NetworkPrefabsPath);
            }

            if (problems.Count == 0)
            {
                Debug.Log("[OxygenGear] VERIFIED off disk: " + Roster.Length + " items, holdSize " +
                          HoldSize.ToString("F2") + ", packSize " + TankPackSize.ToString("F2") +
                          "/" + CellPackSize.ToString("F2") + ", both gauged, all registered for " +
                          "clients.");
                return true;
            }

            Debug.LogError("[OxygenGear] NOT VERIFIED:\n  " + string.Join("\n  ", problems));
            return false;
        }

        // ─────────────────────────── Shared ───────────────────────────

        /// <summary>
        /// PickupableItem is internal to Assembly-CSharp, so it cannot be named from an editor
        /// assembly at all.
        /// </summary>
        private static void AddByName(GameObject go, string fullName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(fullName);
            if (type == null)
            {
                Debug.LogError("[OxygenGear] No such component: " + fullName + ".");
                return;
            }

            go.AddComponent(type);
        }

        /// <summary>
        /// Private [SerializeField] fields are not reachable from an editor script any other way,
        /// and widening the runtime API for a build-time convenience would be the wrong trade. A
        /// missing name warns loudly rather than silently doing nothing.
        /// </summary>
        private static class Field
        {
            public static void Set(SerializedObject so, string name, Object value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.objectReferenceValue = value;
            }

            public static void SetFloat(SerializedObject so, string name, float value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.floatValue = value;
            }

            public static void SetInt(SerializedObject so, string name, int value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.intValue = value;
            }

            public static void SetBool(SerializedObject so, string name, bool value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.boolValue = value;
            }

            public static void SetEnum(SerializedObject so, string name, int value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.enumValueIndex = value;
            }

            public static void SetVector3(SerializedObject so, string name, Vector3 value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.vector3Value = value;
            }

            public static void SetColor(SerializedObject so, string name, Color value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.colorValue = value;
            }

            private static SerializedProperty Find(SerializedObject so, string name)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p == null)
                    Debug.LogWarning("[OxygenGear] " + so.targetObject.GetType().Name +
                                     " has no serialized field '" + name +
                                     "' — it was renamed; this value is unset.");
                return p;
            }
        }
    }
}
