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

        // ─────────── The harpoon modelled in the launcher ───────────
        //
        // The bracer carries a harpoon sitting in its launch tube, and the head that flies is a
        // separate instance of the same model. The two must never be on screen at once. These run
        // against SpawnHead/DestroyHead directly because those are the only two places that
        // decide it, and both of them run on EVERY machine — which is what makes a peer see the
        // tube empty out rather than watching a hook leave an arm that still has one in it.

        /// <summary>A stand-in for the harpoon child of the bracer model.</summary>
        private GameObject SeatAHarpoon()
        {
            var seated = new GameObject("seated harpoon");
            seated.transform.SetParent(hook.transform, false);
            typeof(GrapplingHookArtifact)
                .GetField("seatedHook", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(artifact, seated);
            return seated;
        }

        private void Invoke(string method, params object[] args) =>
            typeof(GrapplingHookArtifact)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(artifact, args);

        [Test]
        public void FiringEmptiesTheLaunchTube()
        {
            GameObject seated = SeatAHarpoon();
            var headPrefab = new GameObject("head prefab");

            try
            {
                typeof(GrapplingHookArtifact)
                    .GetField("hookHeadPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(artifact, headPrefab);

                Invoke("SpawnHead", Vector3.zero, Vector3.forward);

                Assert.IsFalse(seated.activeSelf,
                    "The head that flies is a separate instance. Leaving the modelled one in the " +
                    "tube puts two harpoons on screen at once — one in flight and one still in the " +
                    "arm that threw it.");
            }
            finally
            {
                // DestroyHead uses Destroy, which is a no-op in edit mode.
                var spawned = (Transform)FieldOf(artifact, "_head");
                if (spawned != null) Object.DestroyImmediate(spawned.gameObject);
                Object.DestroyImmediate(headPrefab);
            }
        }

        [Test]
        public void DroppingTheRopePutsTheHarpoonBack()
        {
            GameObject seated = SeatAHarpoon();
            seated.SetActive(false);

            Invoke("DestroyHead");

            Assert.IsTrue(seated.activeSelf,
                "A launcher that empties once and never refills is a one-shot. The item is rebuilt " +
                "from the prefab on every equip, so nothing else would ever put it back.");
        }

        [Test]
        public void TheHarpoonComesBackEvenWhenNoHeadWasEverSpawned()
        {
            GameObject seated = SeatAHarpoon();
            seated.SetActive(false);

            // _head is null here: this is the unequip path, and the path a shot with no
            // hookHeadPrefab takes.
            Invoke("DestroyHead");

            Assert.IsTrue(seated.activeSelf,
                "Restoring the model behind DestroyHead's null guard leaves the tube empty for " +
                "good after any drop that never had a head to destroy.");
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
