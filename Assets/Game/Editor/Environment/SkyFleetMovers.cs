// What makes a Sky fleet hull a moving vehicle rather than scenery, shared by the flagship and every
// escort so the two can never be wired differently:
//
//   NetworkObject, NetRelay, NetAuthority, server-authority NetworkTransform   (SkyVesselBuilder's stack)
//   kinematic Rigidbody                                                        (SkyVesselBuilder's body)
//   FlyingRigidbodyMotor in kinematic-hull mode, driven by an AgentController   (the agent stack)
//   WalkerPlatformCarrier over a DeckVolume trigger                             (players ride the decks)
//   EngineSmoke over one DustCloudRecipe cloud per duct                         (black smoke astern)
//
// The brain — DriftRouteModule on the flagship, FleetEscortModule on an escort — is the caller's.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class SkyFleetMovers
    {
        public const string SmokeMaterialPath = "Assets/Game/Art/Materials/Vehicles/SkyEngineSmoke.mat";
        public const string DeckVolumeName = "DeckVolume";
        public const string SmokePrefix = "EngineSmoke_";

        // Sooty mid-grey, a touch warm: smoke against the sky rather than ink (coal-black read as a
        // hole in it), still well darker than the sand dust (0.78, 0.66, 0.47).
        public static readonly Color SmokeTint = new(0.30f, 0.29f, 0.27f, 1f);

        // Thinner than the recipe's 0.6, so overlapping puffs build to grey rather than solid.
        private const float SmokeAlpha = 0.45f;

        // The exhaust is a JET, not thrown dust: a hull moves at only 2-6 m/s, so the recipe's
        // 4-7.5 m/s puffs braked by drag 2.5 stopped within a metre or two and piled up as a ball at
        // the nozzle. Engine puffs leave at their own astern speed (scaled with √ of the duct size) in a
        // narrow cone and brake steadily, so the trail streams out tens of metres behind before it hangs.
        private const float JetSpeedMin = 12f, JetSpeedMax = 18f;
        // Speed-independent drag is a constant deceleration (m/s²): a 15 m/s puff coasts
        // 15² / (2 × 4.5) ≈ 25 m before it hangs; scaled with the jet, the city's travel ~50 m.
        private const float JetDrag = 4.5f;
        private const float JetConeAngle = 10f;

        // Peak puffs per second per duct (EngineSmoke.fullRate); the cap that keeps such a cloud whole.
        public const float SmokePeakRate = 4f;

        // The deck volume reaches this far (m) past the drawn hull, so a player at the rail is aboard.
        private const float DeckVolumeMargin = 2f;

        public struct Flight
        {
            public float MaxSpeed;
            public float Acceleration;
            public float Deceleration;
            public float FaceRotateSpeed;
        }

        /// <summary>
        /// An engine duct, in the model's own metres: its centre, and its depth along the hull's axis.
        /// Smoke is born at the duct's after face and thrown astern.
        /// </summary>
        public struct Duct
        {
            public Vector3 Centre;
            public float Depth;
        }

        /// <summary>
        /// Everything above but the brain and the smoke. <paramref name="root"/> must be unscaled and
        /// fresh (no NetworkObject yet), and carry no particle systems yet: the deck volume is sized
        /// from everything drawn under it.
        /// </summary>
        public static void AddMover(GameObject root, Flight flight)
        {
            // NetworkObject first: the NetworkBehaviours below look for it when added.
            root.AddComponent<NetworkObject>();
            SkyVesselBuilder.AddNetworking(root);
            SkyVesselBuilder.AddBody(root);
            var body = root.GetComponent<Rigidbody>();

            var motor = root.AddComponent<FlyingRigidbodyMotor>();
            SerializedFields.Edit(motor, so =>
            {
                SerializedFields.Set(so, "body", body);
                SerializedFields.SetBool(so, "kinematicHull", true);
                // A hull holds whatever altitude its brain flies it at, and never falls.
                SerializedFields.SetBool(so, "altitudeHold", false);
                SerializedFields.SetBool(so, "gravityWhenIdle", false);
                SerializedFields.SetFloat(so, "maxSpeed", flight.MaxSpeed);
                SerializedFields.SetFloat(so, "acceleration", flight.Acceleration);
                SerializedFields.SetFloat(so, "deceleration", flight.Deceleration);
                SerializedFields.SetFloat(so, "faceRotateSpeed", flight.FaceRotateSpeed);
            });

            var controller = root.AddComponent<AgentController>();
            SerializedFields.Edit(controller, so =>
            {
                SerializedFields.Set(so, "MotorComponent", motor);
                // A drifting hull's speed is its brain's decision; the crowd-staggering drift every
                // walker gets would only make a city surge and sag.
                SerializedFields.SetFloat(so, "speedVariationAmount", 0f);
            });

            BoxCollider volume = AddDeckVolume(root);
            var carrier = root.AddComponent<WalkerPlatformCarrier>();
            carrier.BindCarryVolume(volume);
        }

        /// <summary>A trigger box over everything drawn under <paramref name="root"/>, in its own space.</summary>
        private static BoxCollider AddDeckVolume(GameObject root)
        {
            Bounds drawn = SkyFleetBuilder.RendererBounds(root);
            var go = new GameObject(DeckVolumeName);
            go.transform.SetParent(root.transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = root.transform.InverseTransformPoint(drawn.center);
            box.size = drawn.size + Vector3.one * (2f * DeckVolumeMargin);
            return box;
        }

        /// <summary>
        /// One cloud per duct, at its after face, blasted astern as a jet (the recipe's speed, cone,
        /// drag and colour overridden here — the recipe's defaults suit thrown sand and stay as they
        /// are for the monowheel), and an EngineSmoke driving them.
        /// <paramref name="model"/> is the transform the ducts are measured in (it may be scaled);
        /// <paramref name="scale"/> sizes the puffs to the duct (1 = an escort's 3 m fans).
        /// </summary>
        public static EngineSmoke AddSmoke(GameObject root, Transform model, IReadOnlyList<Duct> ducts,
                                           float scale, float cruiseSpeed)
        {
            Material material = DustCloudRecipe.Material(SmokeMaterialPath, SmokeTint, softFade: 0f);
            int cap = DustCloudRecipe.CapFor(SmokePeakRate);
            Quaternion astern = Quaternion.LookRotation(Vector3.back);

            var clouds = new ParticleSystem[ducts.Count];
            for (int i = 0; i < ducts.Count; i++)
            {
                Vector3 afterFace = ducts[i].Centre + Vector3.back * (ducts[i].Depth * 0.5f);
                Vector3 local = root.transform.InverseTransformPoint(model.TransformPoint(afterFace));
                ParticleSystem cloud = DustCloudRecipe.Cloud(root.transform, SmokePrefix + i, local, astern,
                                                             material, SmokeTint, cap, Vector3.zero);
                ParticleSystem.MainModule main = cloud.main;
                ParticleSystem.MinMaxCurve size = main.startSize;
                size.constantMin *= scale;
                size.constantMax *= scale;
                main.startSize = size;
                float jet = Mathf.Sqrt(scale);
                main.startSpeed = new ParticleSystem.MinMaxCurve(JetSpeedMin * jet, JetSpeedMax * jet);
                // White: the JetSmoke shader multiplies its _Color (the tint) by the particle colour,
                // so tinting both squares it — a 0.30 grey came out as 0.09, near-black again.
                main.startColor = new Color(1f, 1f, 1f, SmokeAlpha);
                ParticleSystem.ShapeModule shape = cloud.shape;
                shape.radius *= scale;
                shape.angle = JetConeAngle;
                ParticleSystem.LimitVelocityOverLifetimeModule drag = cloud.limitVelocityOverLifetime;
                drag.drag = JetDrag * jet;
                // Not the default ∝ speed: that killed a 15 m/s jet within ~8 m.
                drag.multiplyDragByParticleVelocity = false;
                clouds[i] = cloud;
            }

            var smoke = root.AddComponent<EngineSmoke>();
            smoke.Configure(clouds, cruiseSpeed);
            return smoke;
        }

        /// <summary>
        /// Each duct must sit inside a drawn engine assembly of <paramref name="model"/>, or the model
        /// changed under the measured points. Returns the ducts that do not, empty when all are fine.
        /// </summary>
        public static List<int> DuctsOffTheEngines(Transform model, IReadOnlyList<Duct> ducts, string engineMeshPrefix)
        {
            var engines = new List<Bounds>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith(engineMeshPrefix)) engines.Add(r.bounds);

            var off = new List<int>();
            for (int i = 0; i < ducts.Count; i++)
            {
                Vector3 world = model.TransformPoint(ducts[i].Centre);
                if (!engines.Exists(b => b.Contains(world))) off.Add(i);
            }
            return off;
        }
    }
}
