// What the grappling hook tells the other machines.
//
// The trap these pin is the one the hook shipped with: it replicated through a GrappleNetworkSync
// beside it, whose rope LineRenderer was unassigned on both player prefabs — so a remote grapple
// was invisible, and had been for as long as the component existed. Nothing failed; peers simply
// watched a player fly with no rope.
//
// The fix moves it onto the Use/Present split every other artifact already uses, which is what
// these tests hold in place: the aim is resolved once, on the machine that owns the camera, and
// travels in the message (OnRequestUse), and the rope is drawn from that message on EVERY machine
// (Present). An artifact that stops overriding either half goes back to being invisible to peers.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class GrappleUseFlowTests
    {
        private const int Release = 0;
        private const int Attach = 1;

        private GameObject player;
        private GameObject hook;
        private GameObject target;
        private GrapplingHookArtifact artifact;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("player", typeof(Rigidbody), typeof(AimProvider));

            var cam = new GameObject("cam", typeof(Camera));
            cam.transform.SetParent(player.transform, false);
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            AimProvider aim = player.GetComponent<AimProvider>();
            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(aim, cam.GetComponent<Camera>());

            // The item lives on its own object, as the equipped prefab does.
            hook = new GameObject("grapple", typeof(LineRenderer));
            artifact = hook.AddComponent<GrapplingHookArtifact>();

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (target != null) Object.DestroyImmediate(target);
            if (hook != null) Object.DestroyImmediate(hook);
            if (player != null) Object.DestroyImmediate(player);
        }

        /// <summary>Puts a 1×1×1 collider straight ahead, near face at z = 9.5.</summary>
        private void PlaceTargetAhead()
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = new Vector3(0f, 0f, 10f);
            Physics.SyncTransforms();
        }

        private NetArg Press()
        {
            var arg = new NetArg();
            artifact.OnRequestUse(ref arg);
            return arg;
        }

        private static object FieldOf(object instance, string name) =>
            instance.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                ?.GetValue(instance);

        // ─────────── The owner is known before the first press ───────────

        [Test]
        public void EquippingSetsTheOwnerBeforeTheFirstUse()
        {
            artifact.OnEquipped(player);

            Assert.AreSame(player, FieldOf(artifact, "owner"),
                "OnRequestUse runs before TryUse/PlayUse on the first press after an equip. With no " +
                "owner set at equip time the aim provider is null there, and the aim silently goes " +
                "unreported for exactly one use per equip.");
        }

        // ─────────── The aim travels in the message ───────────

        [Test]
        public void AimedPressReportsTheHookPointSoPeersCanHangTheRopeOnIt()
        {
            PlaceTargetAhead();
            artifact.OnEquipped(player);

            NetArg arg = Press();

            Assert.AreEqual(Attach, arg.B, "A press that hit something must report an attach.");
            Assert.AreEqual(9.5f, arg.P.z, 0.01f,
                "The hook point must be the point the OWNER's ray hit. A peer has neither this " +
                "camera nor this frame and cannot recompute it — if it is not in the message, the " +
                "rope has nothing to hang on.");
            Assert.AreEqual(0f, arg.P.x, 0.01f);
            Assert.AreEqual(0f, arg.P.y, 0.01f);
        }

        [Test]
        public void PressWithNothingUnderTheAimReportsARelease()
        {
            artifact.OnEquipped(player);

            NetArg arg = Press();

            Assert.AreEqual(Release, arg.B,
                "A miss must not present as an attach, or every peer draws a rope to the origin.");
        }

        [Test]
        public void PressWhileTheRopeIsOutReportsARelease()
        {
            PlaceTargetAhead();
            artifact.OnEquipped(player);

            typeof(GrapplingHookArtifact)
                .GetField("_isGrappling", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(artifact, true);

            NetArg arg = Press();

            Assert.AreEqual(Release, arg.B,
                "The second press is the one that lets go. Reporting an attach there would leave " +
                "every peer's rope out for good — nothing else tells them it was dropped.");
        }

        [Test]
        public void HookableLayerMaskIsStillHonouredBeforeAnythingIsSent()
        {
            PlaceTargetAhead();
            artifact.OnEquipped(player);

            typeof(GrapplingHookArtifact)
                .GetField("hookableLayers", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(artifact, (LayerMask)0);

            NetArg arg = Press();

            Assert.AreEqual(Release, arg.B,
                "The mask has to be applied where the aim is resolved. Applied later it would be " +
                "applied on peers too, each against their own copy of the layer setup.");
        }

        // ─────────── Both halves still exist ───────────

        [Test]
        public void TheHookDescribesItsUseAndPresentsItOnEveryMachine()
        {
            MethodInfo request = typeof(GrapplingHookArtifact).GetMethod(
                "OnRequestUse", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo present = typeof(GrapplingHookArtifact).GetMethod(
                "Present", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.AreEqual(typeof(GrapplingHookArtifact), request.DeclaringType,
                "Without its own OnRequestUse the hook point never leaves the owner.");
            Assert.AreEqual(typeof(GrapplingHookArtifact), present.DeclaringType,
                "Without its own Present a peer runs no half of this item at all — which is exactly " +
                "how remote grapples came to be invisible.");
        }

        [Test]
        public void TheSwingStaysWithTheMachineThatOwnsTheBody()
        {
            Assert.AreEqual(UseAuthority.Owner, artifact.Authority,
                "Server authority would put a round trip inside every swing, and the swing is the " +
                "item. The body is owner-authoritative, so the result replicates on its own.");
        }
    }
}
