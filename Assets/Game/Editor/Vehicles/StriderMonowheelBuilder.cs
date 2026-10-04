// The drivable monowheels: five Strider machines, each with a Strider at the tiller, and one
// riderless wheel for the player. Each wraps one of the parallel session's art prefabs
// (Monowheel_<Variant>, ring spin and sand) as a connected nested child named Body, and adds
// everything that makes it a vehicle: the ring and chassis colliders, the Rigidbody,
// MonowheelMotor (one physics motor for a rider's stick and an NPC's MoveIntent), MonowheelLean
// (a roll on Body only), the saddle a player can take, and the netcode and save stack.
//
//   StriderMonowheel_Runner / _Hauler / _Patched    single: a Strider driver (NpcPassenger)
//   StriderMonowheel_Double / _DoubleWide           double: the driver plus three gunners
//                                                   (VesselSeats + MountedGunners)
//   PlayerMonowheel                                 the Runner with nobody aboard, no side and no
//                                                   brain: a vehicle that sits where it was left
//
// The mount itself never attacks: the riders shoot. The Strider wheels ride in the walking city's
// formation and travel to goals, but only while somebody drives (MonowheelDriverGate), and stop dead
// when killed (MonowheelWreck) before HealthReactionModule's corpse despawn takes them away. The
// player's has no behaviour module but the mount-aware MountModule + SteerModule, so a parked wheel's motor brakes to a stop
// and stays there.
//
// They live under Prefabs/Agents/Vehicles/, the folder RagdollWiring reads as "a machine, never a
// body": a monowheel carries AgentController + HealthComponent, which anywhere else would earn it
// an AgentRagdoll and a limp death.
//
// The builder overwrites every prefab wholesale -- anything added by hand is lost on the next run.
//
// Re-run from: Tools > SpaceGame > Vehicles > Build Strider Monowheels
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public static class StriderMonowheelBuilder
    {
        private const string Folder = "Assets/Game/Prefabs/Agents/Vehicles/Ground/Monowheels";

        /// <summary>The riderless player wheel (a Runner body), for the starter wheel beside the landed ship.</summary>
        public const string PlayerPrefabPath = Folder + "/PlayerMonowheel.prefab";
        /// <summary>Which art the player's wheel wears.</summary>
        public const string PlayerVariant = "Runner";

        public static readonly string[] Singles = { "Runner", "Hauler", "Patched" };
        public static readonly string[] Doubles = { "Double", "DoubleWide" };

        /// <summary>Spec §2.1 speeds; the drive spike (plan Task 1) has not run, so these are the spec's
        /// numbers. A convoy's cruise must stay below every follower's top speed or it falls behind.</summary>
        public const float SingleTopSpeed = 20f, DoubleTopSpeed = 16f, CruiseSpeed = 14f;

        /// <summary>GoalTravelModule.speedMultiplier that makes a wheel of this class ride its goals at <see cref="CruiseSpeed"/>.</summary>
        public static float CruiseMultiplier(bool isDouble) => CruiseSpeed / (isDouble ? DoubleTopSpeed : SingleTopSpeed);

        public static string PrefabPath(string variant) => $"{Folder}/StriderMonowheel_{variant}.prefab";

        public static string ArtPrefabPath(string variant) =>
            $"{MonowheelPresentationBuilder.PrefabFolder}/Monowheel_{variant}.prefab";

        /// <summary>Every prefab this builder writes: the five Strider wheels, then the player's.</summary>
        public static IEnumerable<string> AllPrefabPaths =>
            Singles.Concat(Doubles).Select(PrefabPath).Append(PlayerPrefabPath);

        /// <summary>The nested art instance's name under the physics root.</summary>
        public const string BodyName = "Body";
        /// <summary>Every seat marker's name starts with this; the chassis keeps clear of all of them.</summary>
        public const string SeatPrefix = "Seat_";
        public const string RiderSeatName = SeatPrefix + "Rider";
        public const string PassengerSeatName = SeatPrefix + "Passenger";
        public const string SideSeatPrefix = SeatPrefix + "Side_";
        /// <summary>The kinematic chassis body under Body, posed with the art (MonowheelChassis).</summary>
        public const string ChassisName = "Chassis";
        // The parts a seated body rests on: the seat cushion and the cushion on the backrest behind
        // it. Distinct prefixes: "Mesh_SeatCushion_" is not a prefix of "Mesh_SideSeatCushion".
        public const string RiderCushionPrefix = "Mesh_SeatCushion_";
        public const string RiderBackCushionPrefix = "Mesh_BackCushion_";
        public const string PassengerCushionPrefix = "Mesh_PassengerSeatCushion_";
        public const string PassengerBackCushionPrefix = "Mesh_PassengerBackCushion_";
        public const string SideCushionPrefix = "Mesh_SideSeatCushion";
        private static readonly string[] SeatPartPrefixes =
            { RiderCushionPrefix, RiderBackCushionPrefix, PassengerCushionPrefix, PassengerBackCushionPrefix, SideCushionPrefix };
        private const int SideSeats = 2;
        private const int GunnerSeats = 1 + SideSeats;   // the rear passenger seat and one inside each wheel

        // -- physical body: placeholders until the drive spike (plan Task 1) measures them ----------

        /// <summary>Placeholder until the drive spike: the spike's starting mass. The motor writes the
        /// velocity itself, so mass decides only how hard the wheel shoves and is shoved.</summary>
        private const float BodyMass = 400f;
        /// <summary>Placeholder until the drive spike: none. The motor's coastDrag is the drag; a
        /// Rigidbody drag on top would bleed speed the motor keeps writing back.</summary>
        private const float LinearDamping = 0f;
        /// <summary>Placeholder until the drive spike: soaks up the yaw spin a collision leaves, which the
        /// motor would otherwise fight through MoveRotation.</summary>
        private const float AngularDamping = 5f;
        /// <summary>Placeholder until the drive spike: pitch and roll frozen so the motor owns yaw and
        /// nothing tips the wheel over; the lean the art shows is MonowheelLean's roll on Body.</summary>
        private const RigidbodyConstraints Constraints =
            RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        /// <summary>Placeholder until the drive spike: a 20 m/s body the camera follows judders
        /// without interpolation.</summary>
        private const RigidbodyInterpolation Interpolation = RigidbodyInterpolation.Interpolate;
        /// <summary>Placeholder until the drive spike: how far the chassis boxes float above the lowest
        /// wheel contact at rest. They are posed with the art and never stand on the ground (see
        /// MonowheelChassis), so this only keeps them off the sand under the frame.</summary>
        private const float ChassisGroundClearance = 1f;
        /// <summary>Placeholder until the drive spike: how far (m, along the wheel) the chassis boxes stop
        /// short of the seat cushions, so a seated rider's body is in the open and can be shot.</summary>
        private const float ChassisSeatClearance = 0.8f;
        /// <summary>Placeholder until the drive spike: the sphere the wheel rolls on, at the bottom of each
        /// ring. It must be at least MonowheelPresentation's 0.6 m ground probe, which starts inside it
        /// (a raycast never reports the collider it starts in), or the probe reads the wheel as ground.</summary>
        private const float ContactRadius = 0.7f;
        /// <summary>Placeholder until the drive spike: thin boxes round each ring make it a hollow hoop --
        /// solid where the ring is, empty inside where its riders sit. The hoop is inscribed in the
        /// paddles' circle, so its flat faces sit inside it by radius * (1 - cos(180 / n) degrees):
        /// 16 segments keep that under 4 cm.</summary>
        private const int HoopSegments = 16;
        /// <summary>Placeholder until the drive spike: radial thickness (m) of a hoop segment.</summary>
        private const float HoopThickness = 0.3f;
        /// <summary>Hoop segments within this angle of straight down are left out: the contact sphere
        /// carries the wheel there, and a box corner at the ground would snag on every seam.</summary>
        private const float HoopOpenBelowDegrees = 20f;
        // An .asset: CreateAsset refuses the .physicsMaterial extension.
        private const string RingMaterialPath = Folder + "/MonowheelRing.asset";

        // -- seats -----------------------------------------------------------------------------------

        /// <summary>The body a player sits in; the player's seat offset is measured off it.</summary>
        public const string PlayerBodyPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>
        /// Where a seated rider's origin goes from a seat marker (which is at the seat's back corner),
        /// measured off the rider's own body in the Sit state (SeatedBodyMeasure). The player's origin
        /// is a metre above its soles and a Strider's is at its feet, so one number cannot seat both.
        /// </summary>
        public readonly struct RiderOffsets
        {
            public readonly Vector3 Player, Strider;
            public RiderOffsets(Vector3 player, Vector3 strider) { Player = player; Strider = strider; }
        }

        /// <summary>Sit the player and a Strider nomad down and read their seat offsets off them.</summary>
        public static RiderOffsets MeasureRiderOffsets()
        {
            string striderPath = NomadPrefabBuilder.StriderNomads[0].PrefabPath;
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBodyPath);
            var strider = AssetDatabase.LoadAssetAtPath<GameObject>(striderPath);
            if (player == null || strider == null)
                throw new System.InvalidOperationException(
                    $"[StriderMonowheel] Cannot measure where riders sit: no body at {(player == null ? PlayerBodyPath : striderPath)}. " +
                    "Build the Strider nomads first.");
            return new RiderOffsets(-SeatedBodyMeasure.SitCorner(player), -SeatedBodyMeasure.SitCorner(strider));
        }

        // -- Strider brain ---------------------------------------------------------------------------

        /// <summary>A little over the crab outrider's 180: a steel ring and a chassis.</summary>
        private const int SingleHealth = 200;
        /// <summary>Two rings and four aboard; more to chew through, not twice as much.</summary>
        private const int DoubleHealth = 320;
        /// <summary>A scout keeps its column slot within this (m) -- nothing parks (user 2026-09-24).</summary>
        private const float SlotTolerance = 4f;
        /// <summary>
        /// Beyond this (m) from the LEADER a wheel stops holding its slot and rides straight for the
        /// leader (FormationModule measures from the leader, not the slot). So it must clear the city's
        /// farthest scout slot, ~210 m back, or the rear scouts flip between slot and leader for ever.
        /// ScoutRota also counts a returning scout as home within it. A convoy's slots are far closer.
        /// </summary>
        private static float RegroupDistance => RosterAuthoring.CityRegroupDistance;

        [MenuItem("Tools/SpaceGame/Vehicles/Build Strider Monowheels")]
        public static void Build()
        {
            RiderOffsets offsets = MeasureRiderOffsets();
            var built = new List<string>();
            foreach (string variant in Singles)
                if (BuildStrider(variant, isDouble: false, offsets)) built.Add(PrefabPath(variant));
            foreach (string variant in Doubles)
                if (BuildStrider(variant, isDouble: true, offsets)) built.Add(PrefabPath(variant));
            if (BuildPlayer(offsets)) built.Add(PlayerPrefabPath);

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderMonowheel] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs, " +
                               "or the monowheels have no prefabId and do not survive a reload.");

            // Last write: every scratch root lived in the open scene, which switched this off.
            foreach (string path in built) NetworkObjectDefaults.KeepSceneMigrationSync(path);

            Verify(built);
            Debug.Log($"[StriderMonowheel] Built {built.Count} monowheel prefab(s) in {Folder}.");
        }

        private static bool BuildStrider(string variant, bool isDouble, RiderOffsets offsets)
        {
            string path = PrefabPath(variant);
            GameObject root = BuildVehicle(System.IO.Path.GetFileNameWithoutExtension(path), variant,
                                           isDouble ? DoubleTopSpeed : SingleTopSpeed, offsets.Player,
                                           out Transform body, out Transform riderSeat);
            if (root == null) return false;

            if (!AddStriderCrew(root, body, riderSeat, isDouble ? DoubleHealth : SingleHealth, isDouble, offsets.Strider))
            {
                Object.DestroyImmediate(root);
                return false;
            }

            return Finish(root, body, path, offsets.Strider);
        }

        private static bool BuildPlayer(RiderOffsets offsets)
        {
            GameObject root = BuildVehicle(System.IO.Path.GetFileNameWithoutExtension(PlayerPrefabPath), PlayerVariant,
                                           SingleTopSpeed, offsets.Player, out Transform body, out _);
            return root != null && Finish(root, body, PlayerPrefabPath, offsets.Strider);
        }

        /// <summary>
        /// Everything a monowheel is before anyone rides it: the art, the body, the motor, the saddle.
        /// Null (and an error) when the art prefab or its rider's cushions are missing.
        /// </summary>
        private static GameObject BuildVehicle(string name, string variant, float topSpeed, Vector3 playerSeatOffset,
                                               out Transform body, out Transform riderSeat)
        {
            body = null;
            riderSeat = null;

            var art = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabPath(variant));
            if (art == null)
            {
                Debug.LogError($"[StriderMonowheel] No art prefab at {ArtPrefabPath(variant)}; run the monowheel " +
                               $"presentation builder first. {name} skipped.");
                return null;
            }

            var root = new GameObject(name);
            // Connected, never unpacked: the art (ring spin, sand) stays the other session's to rebuild.
            var nested = (GameObject)PrefabUtility.InstantiatePrefab(art, root.transform);
            nested.name = BodyName;
            nested.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform artRoot = nested.transform;

            Transform seat = BackedSeatMarker(artRoot, RiderSeatName, RiderCushionPrefix, RiderBackCushionPrefix, name);
            if (seat == null)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;
            rigidbody.mass = BodyMass;
            rigidbody.linearDamping = LinearDamping;
            rigidbody.angularDamping = AngularDamping;
            rigidbody.constraints = Constraints;
            rigidbody.interpolation = Interpolation;

            var motor = root.AddComponent<MonowheelMotor>();
            SerializedFields.Edit(motor, so => SerializedFields.SetFloat(so, "settings.topSpeed", topSpeed));
            var lean = root.AddComponent<MonowheelLean>();
            SerializedFields.Edit(lean, so => SerializedFields.Set(so, "body", artRoot));

            var brain = root.AddComponent<AgentController>();
            SerializedFields.Edit(brain, so => SerializedFields.Set(so, "MotorComponent", motor));

            var mount = root.AddComponent<MountModule>();
            SerializedFields.Edit(mount, so =>
            {
                SerializedFields.Set(so, "seatPoint", seat);
                SerializedFields.SetVector3(so, "seatOffset", playerSeatOffset);
                SerializedFields.SetBool(so, "mountableByDirectInteraction", true);
                SerializedFields.SetEnumByName(so, "defaultPerspective", nameof(MountModule.CameraPerspective.ThirdPerson));
                SerializedFields.SetBool(so, "followMountPitch", false);
            });
            var steer = root.AddComponent<SteerModule>();
            SerializedFields.Edit(steer, so =>
            {
                SerializedFields.Set(so, "mountModule", mount);
                // SteerModule.Reset's default, which AddComponent never runs. The brain modules cannot
                // outvote a rider anyway: MountModule switches every module that is not mount-aware off
                // while ridden (allowAISelfMovementWhenMounted is off).
                SerializedFields.SetInt(so, "priority", ModulePriority.Scripted);
                // The motor has no jump or leap (it implements neither interface); say so here.
                SerializedFields.SetBool(so, "jumpEnabled", false);
                SerializedFields.SetBool(so, "leapEnabled", false);
            });
            // The saddle is a seat: whoever takes it, a player through the MountModule or a Strider
            // through VesselSeats, sits down in the humanoid's Sit state instead of standing in it.
            root.AddComponent<ChairPose>();

            var tracked = root.AddComponent<SpaceGame.World.SceneTracked>();
            SerializedFields.Edit(tracked, so =>
            {
                SerializedFields.SetEnumByName(so, "policy",
                    nameof(SpaceGame.World.SceneTracked.UnloadPolicy.Migrate));
                // Like every other player vehicle: a wheel left parked must not keep its corner of the
                // world resident. Riding it keeps the chunks loaded anyway (the rider is a player).
                SerializedFields.SetBool(so, "keepChunksLoaded", false);
            });
            root.AddComponent<SpaceGame.World.Safety.UnderTerrainGuard>();

            body = artRoot;
            riderSeat = seat;
            return root;
        }

        /// <summary>
        /// Per ring: a small contact sphere at the bottom that the wheel rolls on, and a hollow hoop of
        /// thin boxes where the ring is (measured paddle radius, ring width from the Bone_Ring*
        /// renderers), both on the root, never Body, so the lean does not roll them: the wheel is round.
        /// A solid sphere the size of the ring would enclose every rider (nobody could shoot them) and
        /// make the wheel as wide as it is tall. Then chassis boxes fore and aft of the seats, from the
        /// art's renderer bounds, on a kinematic body under Body that the pose tips with the art and
        /// that never stands on the ground (MonowheelChassis).
        /// </summary>
        private static void AddColliders(GameObject root, Transform body)
        {
            PhysicsMaterial frictionless = RingMaterial();
            var presentation = body.GetComponent<MonowheelPresentation>();
            float lowestContact = float.MaxValue, highestHub = float.MinValue;
            for (int w = 0; w < presentation.Wheels.Count; w++)
            {
                MonowheelWheel wheel = presentation.Wheels[w];
                var contact = root.AddComponent<SphereCollider>();
                contact.center = wheel.localContact + Vector3.up * ContactRadius;
                contact.radius = ContactRadius;
                contact.sharedMaterial = frictionless;

                Vector3 axle = root.transform.InverseTransformDirection(wheel.ringBone.TransformDirection(wheel.localAxle)).normalized;
                AddHoop(root.transform, $"Hoop_{w}", wheel.localHub, axle, wheel.paddleRadius,
                        WidthAlong(root.transform, wheel.ringBone, axle), frictionless);

                lowestContact = Mathf.Min(lowestContact, wheel.localContact.y);
                highestHub = Mathf.Max(highestHub, wheel.localHub.y);
            }

            // Where seated bodies are: every seat and back cushion, front edge to back.
            Bounds[] seatParts = body.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => SeatPartPrefixes.Any(f.name.StartsWith))
                .Select(f => MeshBoundsInRoot(root.transform, f))
                .ToArray();
            float seatsFront = seatParts.Max(b => b.max.z), seatsBack = seatParts.Min(b => b.min.z);
            Bounds art = RendererBoundsInRoot(root.transform, body);
            float floor = lowestContact + ChassisGroundClearance;
            Transform chassis = AddChassis(body);
            AddChassisBox(root.transform, chassis, art, floor, highestHub, seatsFront + ChassisSeatClearance, art.max.z);
            AddChassisBox(root.transform, chassis, art, floor, highestHub, art.min.z, seatsBack - ChassisSeatClearance);
        }

        /// <summary>The kinematic chassis body: an added child of Body (the art prefab is untouched), at its origin.</summary>
        private static Transform AddChassis(Transform body)
        {
            var chassis = new GameObject(ChassisName);
            chassis.transform.SetParent(body, false);
            var rigidbody = chassis.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            chassis.AddComponent<MonowheelChassis>();
            return chassis.transform;
        }

        /// <summary>
        /// A ring of boxes round <paramref name="hub"/> in the wheel's own plane (square to its axle), so a
        /// cambered ring's hoop leans with it: an upright hoop round a ring cambered 20 degrees reached
        /// below its real lowest point.
        /// </summary>
        private static void AddHoop(Transform root, string name, Vector3 hub, Vector3 axle, float radius, float width,
                                    PhysicsMaterial material)
        {
            var hoop = new GameObject(name).transform;
            hoop.SetParent(root, false);
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, axle).normalized;
            Vector3 up = Vector3.Cross(axle, forward).normalized;
            if (up.y < 0f) up = -up;
            // Inscribed, so no corner reaches past the paddles: a segment's outer face is a chord of the
            // wheel's circle. Centred on the circle, the boxes' corners stood 13 cm below the contact
            // and the whole vehicle rested on them with its wheel in the air.
            float halfStep = 180f / HoopSegments * Mathf.Deg2Rad;
            float length = 2f * radius * Mathf.Sin(halfStep);
            float centreRadius = radius * Mathf.Cos(halfStep) - HoopThickness * 0.5f;
            for (int i = 0; i < HoopSegments; i++)
            {
                // Segment 0 is straight down; the angle runs round the ring in its own plane.
                float degrees = -90f + i * 360f / HoopSegments;
                if (Mathf.Abs(Mathf.DeltaAngle(-90f, degrees)) < HoopOpenBelowDegrees) continue;

                float theta = degrees * Mathf.Deg2Rad;
                Vector3 radial = Mathf.Sin(theta) * up + Mathf.Cos(theta) * forward;
                Vector3 tangent = Mathf.Cos(theta) * up - Mathf.Sin(theta) * forward;
                var segment = new GameObject($"Segment_{i}");
                segment.transform.SetParent(hoop, false);
                segment.transform.SetLocalPositionAndRotation(hub + centreRadius * radial, Quaternion.LookRotation(tangent, radial));
                var box = segment.AddComponent<BoxCollider>();
                box.size = new Vector3(width, HoopThickness, length);
                box.sharedMaterial = material;
            }
        }

        /// <summary>A chassis box between two points along the wheel (root space); nothing when the span is empty.</summary>
        private static void AddChassisBox(Transform root, Transform chassis, Bounds art, float floor, float top, float fromZ, float toZ)
        {
            if (toZ <= fromZ) return;
            var box = chassis.gameObject.AddComponent<BoxCollider>();
            box.center = chassis.InverseTransformPoint(root.TransformPoint(
                new Vector3(art.center.x, (floor + top) * 0.5f, (fromZ + toZ) * 0.5f)));
            box.size = new Vector3(art.size.x, top - floor, toZ - fromZ);
        }

        /// <summary>Frictionless, so ground friction never fights the motor, which owns grip
        /// (MonowheelDrive's lateralGrip). An asset because a prefab can only reference one; made once
        /// and kept, so its GUID stays stable across rebuilds.</summary>
        private static PhysicsMaterial RingMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(RingMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial(System.IO.Path.GetFileNameWithoutExtension(RingMaterialPath));
                System.IO.Directory.CreateDirectory(Folder);
                AssetDatabase.CreateAsset(material, RingMaterialPath);
            }
            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        /// <summary>How wide the ring's meshes are along its axle (root space). An axis-aligned box would
        /// count a cambered ring's lean as width.</summary>
        private static float WidthAlong(Transform root, Transform ringBone, Vector3 axle)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (MeshFilter part in ringBone.GetComponentsInChildren<MeshFilter>(true))
            foreach (Vector3 vertex in part.sharedMesh.vertices)
            {
                float along = Vector3.Dot(root.InverseTransformPoint(part.transform.TransformPoint(vertex)), axle);
                min = Mathf.Min(min, along);
                max = Mathf.Max(max, along);
            }
            return max - min;
        }

        /// <summary>One part's bounds in root space, from its vertices: tighter than its renderer's, which
        /// boxes the mesh's own bounds again after the model's import rotation.</summary>
        private static Bounds MeshBoundsInRoot(Transform root, MeshFilter part)
        {
            Vector3[] vertices = part.sharedMesh.vertices;
            var bounds = new Bounds(root.InverseTransformPoint(part.transform.TransformPoint(vertices[0])), Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(root.InverseTransformPoint(part.transform.TransformPoint(vertex)));
            return bounds;
        }

        private static Bounds RendererBoundsInRoot(Transform root, Transform under)
        {
            MeshRenderer[] renderers = under.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (MeshRenderer r in renderers)
            {
                bounds.Encapsulate(root.InverseTransformPoint(r.bounds.min));
                bounds.Encapsulate(root.InverseTransformPoint(r.bounds.max));
            }
            return bounds;
        }

        /// <summary>The Strider half: health, side, the column brain, the driver, and a double's gunners.</summary>
        private static bool AddStriderCrew(GameObject root, Transform body, Transform riderSeat, int maxHealth, bool isDouble,
                                           Vector3 seatOffset)
        {
            var health = root.AddComponent<HealthComponent>();
            SerializedFields.Edit(health, so =>
            {
                SerializedFields.SetInt(so, "maxHealth", maxHealth);
                SerializedFields.SetInt(so, "currentHealth", maxHealth);
            });
            root.AddComponent<HealthReactionModule>();
            root.AddComponent<MonowheelWreck>();

            if (!EntityFactionWiring.Ensure(root, root.name))
                Debug.LogError($"[StriderMonowheel] {root.name} got no EntityFaction; it is on nobody's side and invisible to targeting.");

            var formation = root.AddComponent<FormationModule>();
            SerializedFields.Edit(formation, so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
                // Nothing parks (user 2026-09-24): a scout at home keeps its slot beside the column.
                SerializedFields.SetBool(so, "holdSlotAtRest", true);
                SerializedFields.SetFloat(so, "slotTolerance", SlotTolerance);
                SerializedFields.SetFloat(so, "regroupDistance", RegroupDistance);
            });
            var travel = root.AddComponent<GoalTravelModule>();
            SerializedFields.Edit(travel, so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1);
                // A goal is ridden at cruise, not flat out: a convoy's leader and a sweeping scout both
                // travel by goal, and a follower keeps up only with a leader below its own top speed.
                SerializedFields.SetFloat(so, "speedMultiplier", CruiseMultiplier(isDouble));
            });

            GameObject driver = LoadNomad(0, root.name);
            var passenger = root.AddComponent<NpcPassenger>();
            SerializedFields.Edit(passenger, so =>
            {
                SerializedFields.Set(so, "riderPrefab", driver);
                SerializedFields.Set(so, "seatPoint", riderSeat);
                SerializedFields.SetVector3(so, "seatOffset", seatOffset);
                SerializedFields.SetBool(so, "spawnOnStart", true);
                // NpcPassenger's default names the Clanker controller's flag; every Strider wears the
                // humanoid controller, whose only seated flag is Seated. A missing name is skipped silently.
                SerializedFields.SetString(so, "seatedAnimatorBool", SpaceGame.Presentation.HumanoidParams.Seated);
            });

            var gate = root.AddComponent<MonowheelDriverGate>();
            SerializedFields.Edit(gate, so =>
            {
                SerializedFields.Set(so, "passenger", passenger);
                SerializedFields.Set(so, "mount", root.GetComponent<MountModule>());
                MonoBehaviour[] driven = { formation, travel };
                SerializedProperty modules = so.FindProperty("drivenModules");
                modules.arraySize = driven.Length;
                for (int i = 0; i < driven.Length; i++) modules.GetArrayElementAtIndex(i).objectReferenceValue = driven[i];
            });

            return !isDouble || AddGunnerSeats(root, body, seatOffset);
        }

        /// <summary>
        /// Seat 0 on the rear passenger seat, seats 1-2 on the two side cushions inside the wheels.
        /// </summary>
        private static bool AddGunnerSeats(GameObject root, Transform body, Vector3 seatOffset)
        {
            Transform passengerSeat = BackedSeatMarker(body, PassengerSeatName, PassengerCushionPrefix, PassengerBackCushionPrefix, root.name);
            if (passengerSeat == null) return false;

            Bounds[] cushions = body.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name.StartsWith(SideCushionPrefix))
                .Select(f => MeshBoundsInRoot(root.transform, f))
                .OrderBy(b => b.center.x)
                .ToArray();
            if (cushions.Length != SideSeats)
            {
                Debug.LogError($"[StriderMonowheel] {root.name}: expected {SideSeats} '{SideCushionPrefix}*' meshes, found " +
                               $"{cushions.Length}; the art changed. The double is skipped.");
                return false;
            }

            var seats = new Transform[GunnerSeats];
            seats[0] = passengerSeat;
            // No backrest: the back corner is the cushion's rear edge.
            for (int i = 0; i < SideSeats; i++)
            {
                Bounds cushion = cushions[i];
                seats[1 + i] = SeatMarker(body, $"{SideSeatPrefix}{i}",
                                          root.transform.TransformPoint(new Vector3(cushion.center.x, cushion.max.y, cushion.min.z)));
            }

            var chair = root.GetComponent<ChairPose>();   // the saddle's, added with the mount
            var vesselSeats = root.AddComponent<VesselSeats>();
            SerializedFields.Edit(vesselSeats, so =>
            {
                SerializedProperty array = so.FindProperty("seats");
                array.arraySize = seats.Length;
                for (int i = 0; i < seats.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
                SerializedFields.SetVector3(so, "seatOffset", seatOffset);
                SerializedFields.Set(so, "chairPose", chair);
            });

            GameObject gunner = LoadNomad(1, root.name);
            var gunners = root.AddComponent<MountedGunners>();
            SerializedFields.Edit(gunners, so =>
            {
                SerializedFields.Set(so, "gunnerPrefab", gunner);
                SerializedFields.SetBool(so, "spawnOnStart", true);
            });
            return true;
        }

        private static GameObject LoadNomad(int recipe, string forWhom)
        {
            string path = NomadPrefabBuilder.StriderNomads[recipe].PrefabPath;
            var nomad = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (nomad == null)
                Debug.LogError($"[StriderMonowheel] No Strider nomad at {path}; build the Strider nomads first or {forWhom} rides empty.");
            return nomad;
        }

        /// <summary>
        /// The seat marker of a seat with a backrest: at its back corner, where the top of the seat
        /// cushion meets the front of the back cushion -- the corner a seated body's bottom and the back
        /// of its pelvis go into. Measured off the cushions because the art's Socket_* empties are not
        /// on them (the Double's rider socket is 0.16 m ahead of its backrest). Null and an error when
        /// either cushion is missing.
        /// </summary>
        private static Transform BackedSeatMarker(Transform body, string name, string cushionPrefix, string backPrefix, string forWhom)
        {
            MeshFilter cushion = FindSingle(body, cushionPrefix, forWhom)?.GetComponent<MeshFilter>();
            MeshFilter back = FindSingle(body, backPrefix, forWhom)?.GetComponent<MeshFilter>();
            if (cushion == null || back == null) return null;

            Transform root = body.parent;
            Bounds seat = MeshBoundsInRoot(root, cushion);
            float backFront = MeshBoundsInRoot(root, back).max.z;
            return SeatMarker(body, name, root.TransformPoint(new Vector3(seat.center.x, seat.max.y, backFront)));
        }

        /// <summary>
        /// An upright seat marker at a point on the art. Not the art's socket itself: the Blender empties
        /// import pitched -90 degrees, so a rider seated on one lies on their back and the seat offset
        /// pushes them forward instead of down. Parented to Body (an added child on the connected
        /// instance -- the art prefab's own file is untouched) so the seat tips with the chassis pose,
        /// and TiltingSeats carries its rider with it.
        /// </summary>
        private static Transform SeatMarker(Transform body, string name, Vector3 worldPosition)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(body, false);
            marker.SetPositionAndRotation(worldPosition, body.rotation);
            return marker;
        }

        /// <summary>The one transform under <paramref name="body"/> whose name starts with the prefix; null
        /// and an error when there is none or more than one.</summary>
        private static Transform FindSingle(Transform body, string prefix, string forWhom)
        {
            Transform[] found = body.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix)).ToArray();
            if (found.Length == 1) return found[0];

            Debug.LogError($"[StriderMonowheel] {forWhom}: expected one '{prefix}*' in the art, found {found.Length}. Skipped.");
            return null;
        }

        /// <summary>The colliders (last, because they keep clear of every seat), the seats' tilt, the netcode
        /// and save stack, then the save. The temp root is destroyed either way.</summary>
        private static bool Finish(GameObject root, Transform body, string path, Vector3 npcSeatOffset)
        {
            AddColliders(root, body);
            AddTiltingSeats(root, body, npcSeatOffset);

            // A new prefab, so no scene holds an instance: the identity is ours to add, and Ensure (which
            // refuses to add one) fills in the rest of the netcode set around it.
            var netObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            // A player driving it owns it (MountNetworkSync); with this off, their disconnect deletes the wheel.
            netObject.DontDestroyWithOwner = true;
            AgentNetworkWiring.Ensure(root);
            SerializedFields.Edit(root.GetComponent<NetAuthority>(),
                so => SerializedFields.SetBool(so, "freezePhysicsOnRemote", true));
            root.AddComponent<SaveableEntity>();

            System.IO.Directory.CreateDirectory(Folder);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            Object.DestroyImmediate(root);
            if (!saved) Debug.LogError($"[StriderMonowheel] Could not save {path}; the AssetDatabase refused the write.");
            return saved;
        }

        /// <summary>Every rider sits on a seat under Body, which the chassis pose tips: hold them on it.</summary>
        private static void AddTiltingSeats(GameObject root, Transform body, Vector3 npcSeatOffset)
        {
            Transform[] seats = body.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(SeatPrefix)).ToArray();
            var tilting = root.AddComponent<TiltingSeats>();
            SerializedFields.Edit(tilting, so =>
            {
                SerializedProperty array = so.FindProperty("seats");
                array.arraySize = seats.Length;
                for (int i = 0; i < seats.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
                SerializedFields.SetVector3(so, "npcSeatOffset", npcSeatOffset);
                SerializedFields.Set(so, "mount", root.GetComponent<MountModule>());
            });
        }

        private static void Verify(List<string> built)
        {
            foreach (string path in AllPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !built.Contains(path)) { Debug.LogError($"[StriderMonowheel] {path} was not written."); continue; }

                var saveable = prefab.GetComponent<SaveableEntity>();
                if (saveable == null || string.IsNullOrEmpty(saveable.PrefabId))
                    Debug.LogError($"[StriderMonowheel] {prefab.name} has no prefabId; it will not survive a reload.");
                var netObject = prefab.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObject == null || !netObject.SceneMigrationSynchronization)
                    Debug.LogError($"[StriderMonowheel] {prefab.name} lacks a NetworkObject with scene migration sync.");
                if (path != PlayerPrefabPath && prefab.GetComponent<NpcPassenger>() == null)
                    Debug.LogError($"[StriderMonowheel] {prefab.name} has nobody to drive it.");
            }
        }
    }
}
