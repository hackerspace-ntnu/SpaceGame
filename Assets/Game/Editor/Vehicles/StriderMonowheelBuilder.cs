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
        private const string RiderSocketPrefix = "Socket_Rider_";
        private const string PassengerSocketPrefix = "Socket_Passenger_";
        private const string SideCushionPrefix = "Mesh_SideSeatCushion";
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
        /// wheel contact. Pitch is frozen, so a box's nose (~4 m ahead of the ring) must clear rising
        /// ground -- 1 m clears about 13 degrees. It must also stay above MonowheelPresentation's
        /// 0.6 m ground probe, which would otherwise read the box as ground.</summary>
        private const float ChassisGroundClearance = 1f;
        /// <summary>Placeholder until the drive spike: how far (m, along the wheel) the chassis boxes stop
        /// short of the nearest seat, so a seated rider's body is in the open and can be shot.</summary>
        private const float ChassisSeatClearance = 0.8f;
        /// <summary>Placeholder until the drive spike: the sphere the wheel rolls on, at the bottom of each
        /// ring. It must be at least MonowheelPresentation's 0.6 m ground probe, which starts inside it
        /// (a raycast never reports the collider it starts in), or the probe reads the wheel as ground.</summary>
        private const float ContactRadius = 0.7f;
        /// <summary>Placeholder until the drive spike: thin boxes round each ring make it a hollow hoop --
        /// solid where the ring is, empty inside where its riders sit.</summary>
        private const int HoopSegments = 12;
        /// <summary>Placeholder until the drive spike: radial thickness (m) of a hoop segment.</summary>
        private const float HoopThickness = 0.3f;
        /// <summary>Hoop segments within this angle of straight down are left out: the contact sphere
        /// carries the wheel there, and a box corner at the ground would snag on every seam.</summary>
        private const float HoopOpenBelowDegrees = 20f;
        // An .asset: CreateAsset refuses the .physicsMaterial extension.
        private const string RingMaterialPath = Folder + "/MonowheelRing.asset";

        // -- seats -----------------------------------------------------------------------------------

        /// <summary>How far below a seat marker a seated rider's origin (its feet) goes: the robot horse's
        /// Clanker drop, NOT measured for the 3 m Strider nomad. Placeholder until seen in play.</summary>
        private const float RiderSeatDrop = 0.95f;
        private static Vector3 SeatOffset => Vector3.down * RiderSeatDrop;

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
        private static float RegroupDistance => RosterAuthoring.CityFarthestSlot + RegroupMargin;
        /// <summary>Room to fall behind the farthest slot (a corner, a dune) before the shape is given up.</summary>
        private const float RegroupMargin = 30f;

        [MenuItem("Tools/SpaceGame/Vehicles/Build Strider Monowheels")]
        public static void Build()
        {
            var built = new List<string>();
            foreach (string variant in Singles)
                if (BuildStrider(variant, isDouble: false)) built.Add(PrefabPath(variant));
            foreach (string variant in Doubles)
                if (BuildStrider(variant, isDouble: true)) built.Add(PrefabPath(variant));
            if (BuildPlayer()) built.Add(PlayerPrefabPath);

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderMonowheel] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs, " +
                               "or the monowheels have no prefabId and do not survive a reload.");

            // Last write: every scratch root lived in the open scene, which switched this off.
            foreach (string path in built) NetworkObjectDefaults.KeepSceneMigrationSync(path);

            Verify(built);
            Debug.Log($"[StriderMonowheel] Built {built.Count} monowheel prefab(s) in {Folder}.");
        }

        private static bool BuildStrider(string variant, bool isDouble)
        {
            string path = PrefabPath(variant);
            GameObject root = BuildVehicle(System.IO.Path.GetFileNameWithoutExtension(path), variant,
                                           isDouble ? DoubleTopSpeed : SingleTopSpeed, out Transform body, out Transform riderSeat);
            if (root == null) return false;

            if (!AddStriderCrew(root, body, riderSeat, isDouble ? DoubleHealth : SingleHealth, isDouble))
            {
                Object.DestroyImmediate(root);
                return false;
            }

            return Finish(root, body, path);
        }

        private static bool BuildPlayer()
        {
            GameObject root = BuildVehicle(System.IO.Path.GetFileNameWithoutExtension(PlayerPrefabPath), PlayerVariant,
                                           SingleTopSpeed, out Transform body, out _);
            return root != null && Finish(root, body, PlayerPrefabPath);
        }

        /// <summary>
        /// Everything a monowheel is before anyone rides it: the art, the body, the motor, the saddle.
        /// Null (and an error) when the art prefab or its rider socket is missing.
        /// </summary>
        private static GameObject BuildVehicle(string name, string variant, float topSpeed,
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

            Transform socket = FindSingle(artRoot, RiderSocketPrefix, name);
            if (socket == null)
            {
                Object.DestroyImmediate(root);
                return null;
            }
            Transform seat = SeatMarker(artRoot, RiderSeatName, socket.position);

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
                SerializedFields.SetVector3(so, "seatOffset", SeatOffset);
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
        /// renderers). Then chassis boxes fore and aft of the seats, from the art's renderer bounds. A
        /// solid sphere the size of the ring would enclose every rider (nobody could shoot them) and make
        /// the wheel as wide as it is tall. All of it hangs off the root, never Body, so the lean does
        /// not roll it.
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

                float ringWidth = RendererBoundsInRoot(root.transform, wheel.ringBone).size.x;
                AddHoop(root.transform, $"Hoop_{w}", wheel.localHub, wheel.paddleRadius, ringWidth, frictionless);

                lowestContact = Mathf.Min(lowestContact, wheel.localContact.y);
                highestHub = Mathf.Max(highestHub, wheel.localHub.y);
            }

            float[] seatZ = body.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(SeatPrefix))
                .Select(t => root.transform.InverseTransformPoint(t.position).z)
                .ToArray();
            Bounds art = RendererBoundsInRoot(root.transform, body);
            float floor = lowestContact + ChassisGroundClearance;
            AddChassisBox(root, art, floor, highestHub, seatZ.Max() + ChassisSeatClearance, art.max.z);
            AddChassisBox(root, art, floor, highestHub, art.min.z, seatZ.Min() - ChassisSeatClearance);
        }

        private static void AddHoop(Transform root, string name, Vector3 hub, float radius, float width, PhysicsMaterial material)
        {
            var hoop = new GameObject(name).transform;
            hoop.SetParent(root, false);
            float step = 360f / HoopSegments;
            float length = 2f * radius * Mathf.Tan(step * 0.5f * Mathf.Deg2Rad);
            for (int i = 0; i < HoopSegments; i++)
            {
                // Segment 0 is straight down; the angle runs round the ring in the root's YZ plane.
                float degrees = -90f + i * step;
                if (Mathf.Abs(Mathf.DeltaAngle(-90f, degrees)) < HoopOpenBelowDegrees) continue;

                float theta = degrees * Mathf.Deg2Rad;
                var radial = new Vector3(0f, Mathf.Sin(theta), Mathf.Cos(theta));
                var tangent = new Vector3(0f, Mathf.Cos(theta), -Mathf.Sin(theta));
                var segment = new GameObject($"Segment_{i}");
                segment.transform.SetParent(hoop, false);
                segment.transform.SetLocalPositionAndRotation(hub + radius * radial, Quaternion.LookRotation(tangent, radial));
                var box = segment.AddComponent<BoxCollider>();
                box.size = new Vector3(width, HoopThickness, length);
                box.sharedMaterial = material;
            }
        }

        /// <summary>A chassis box between two points along the wheel; nothing when the span is empty.</summary>
        private static void AddChassisBox(GameObject root, Bounds art, float floor, float top, float fromZ, float toZ)
        {
            if (toZ <= fromZ) return;
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(art.center.x, (floor + top) * 0.5f, (fromZ + toZ) * 0.5f);
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
        private static bool AddStriderCrew(GameObject root, Transform body, Transform riderSeat, int maxHealth, bool isDouble)
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
                SerializedFields.SetVector3(so, "seatOffset", SeatOffset);
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

            return !isDouble || AddGunnerSeats(root, body);
        }

        /// <summary>
        /// Seat 0 on the rear passenger socket, seats 1-2 on top of the two side cushions inside the wheels.
        /// </summary>
        private static bool AddGunnerSeats(GameObject root, Transform body)
        {
            Transform passengerSocket = FindSingle(body, PassengerSocketPrefix, root.name);
            if (passengerSocket == null) return false;

            MeshRenderer[] cushions = body.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith(SideCushionPrefix))
                .OrderBy(r => root.transform.InverseTransformPoint(r.bounds.center).x)
                .ToArray();
            if (cushions.Length != SideSeats)
            {
                Debug.LogError($"[StriderMonowheel] {root.name}: expected {SideSeats} '{SideCushionPrefix}*' meshes, found " +
                               $"{cushions.Length}; the art changed. The double is skipped.");
                return false;
            }

            var seats = new Transform[GunnerSeats];
            seats[0] = SeatMarker(body, PassengerSeatName, passengerSocket.position);
            for (int i = 0; i < SideSeats; i++)
            {
                Bounds cushion = cushions[i].bounds;
                seats[1 + i] = SeatMarker(body, $"{SideSeatPrefix}{i}", new Vector3(cushion.center.x, cushion.max.y, cushion.center.z));
            }

            var chair = root.GetComponent<ChairPose>();   // the saddle's, added with the mount
            var vesselSeats = root.AddComponent<VesselSeats>();
            SerializedFields.Edit(vesselSeats, so =>
            {
                SerializedProperty array = so.FindProperty("seats");
                array.arraySize = seats.Length;
                for (int i = 0; i < seats.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
                SerializedFields.SetVector3(so, "seatOffset", SeatOffset);
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
        /// An upright seat marker at a point on the art. Not the art's socket itself: the Blender empties
        /// import pitched -90 degrees, so a rider seated on one lies on their back and the seat offset
        /// pushes them forward instead of down. Parented to Body (an added child on the connected
        /// instance -- the art prefab's own file is untouched) so a player's seat rolls with the lean.
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

        /// <summary>The colliders (last, because they keep clear of every seat), the netcode and save stack,
        /// then the save. The temp root is destroyed either way.</summary>
        private static bool Finish(GameObject root, Transform body, string path)
        {
            AddColliders(root, body);

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
