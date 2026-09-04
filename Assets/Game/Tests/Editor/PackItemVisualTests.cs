using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// A display copy is scenery for the eye and for the cursor ray, and it must be nothing else.
    ///
    /// <para>
    /// <see cref="DisplayCopy"/> strips every collider and every Rigidbody off a copy and
    /// <see cref="BackpackItemVisual"/> then adds exactly one BoxCollider back, so the copy has no
    /// body of its own and its collider joins whatever Rigidbody it is parented under. On a worn
    /// pack that is the PLAYER, and on the ship's gear wall it is the SHIP — which is
    /// <see cref="BackpackObject"/>'s "bolt a box onto the player's own capsule and wedge them in
    /// every doorway", once per stowed item, at the size the item is drawn. The gear wall made it
    /// visible because a wall stands in a room people walk through and draws its items 59% over
    /// life size, but the worn pack had it first.
    /// </para>
    /// <para>
    /// So three things are pinned here, and the order of them is the point. The copy <b>does not
    /// push the body it hangs on</b> — stepped through real physics, because that is the fault
    /// itself and everything else is a proxy for it. It <b>is excluded from every collision
    /// layer</b>, which is the mechanism that currently makes the first one true and would fail
    /// silently if the mechanism were swapped for one that does not work. And it <b>is still hit by
    /// a ray</b>, because a copy that stopped colliding by losing its collider would pass both of
    /// the others and break focus mode's cursor with nothing to say so.
    /// </para>
    /// <para>
    /// <b>The property assertion is not the guard, and on its own it never was.</b>
    /// <c>excludeLayers</c> on a collider that belongs to somebody else's Rigidbody composes with
    /// that body's own masks by rules this project does not control and Unity has changed before, so
    /// "the field holds the value we wrote" is a test of the edit, not of the outcome. Hence
    /// <see cref="ADisplayCopyDoesNotShoveTheBodyItHangsOn"/> and, beside it, the control that
    /// proves this fixture can still SEE a shove: a guard that would pass with the bug reinstated
    /// is not a guard.
    /// </para>
    /// </summary>
    public class PackItemVisualTests
    {
        /// <summary>The face the copy is laid on, big enough that the item is nowhere near an edge.</summary>
        private static readonly Vector2 FaceSize = new(2f, 2f);

        /// <summary>
        /// Where the source object sits. Far from the surface at the origin so that its own
        /// geometry can never be what a ray in these tests hits.
        /// </summary>
        private static readonly Vector3 FarAway = new(1000f, 0f, 0f);

        /// <summary>
        /// Metres between the body's own collider and the surface its gear is laid on. Only has to
        /// be more than the two of them put together: the bulkhead below is stood over the display
        /// copy alone, so a body that moves at all can only have been moved through the copy.
        /// </summary>
        private const float SurfaceStandoff = 5f;

        /// <summary>One physics step, and how many of them a shove is given to show itself.</summary>
        private const float Step = 0.02f;

        private const int Steps = 25;

        /// <summary>
        /// Metres of movement that still count as none. The body carries no gravity and nothing
        /// else touches it, so an untouched one does not drift at all and this is only slack
        /// against solver noise.
        /// </summary>
        private const float Still = 1e-3f;

        private GameObject surfaceHost;
        private GameObject source;
        private GameObject copy;
        private GameObject wearerGo;
        private GameObject bulkhead;
        private SimulationMode originalSimulationMode;

        /// <summary>
        /// Physics is stepped by hand, the way <c>WalkerPlatformCarrierTests</c> steps it: a shove
        /// is something a body does over a step, and there is no other way to ask whether one
        /// happened.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = originalSimulationMode;

            if (copy != null) Object.DestroyImmediate(copy);
            if (source != null) Object.DestroyImmediate(source);
            if (surfaceHost != null) Object.DestroyImmediate(surfaceHost);
            if (wearerGo != null) Object.DestroyImmediate(wearerGo);
            if (bulkhead != null) Object.DestroyImmediate(bulkhead);

            copy = null;
            source = null;
            surfaceHost = null;
            wearerGo = null;
            bulkhead = null;
        }

        /// <summary>
        /// A surface, unrotated and unscaled — authored the way <c>PackSurfaceTests</c> authors
        /// one, since the id and the size are inspector fields on the real rig and neither has a
        /// setter.
        /// </summary>
        /// <param name="parent">The body to hang it off, or null to leave it standing at the
        /// origin on its own. Given one it is a worn pack or a gear wall: the copy's collider then
        /// joins that body, which is the whole condition the shove tests are about.</param>
        private PackSurface Surface(Transform parent = null)
        {
            surfaceHost = new GameObject("SURF_Test");
            if (parent != null)
            {
                surfaceHost.transform.SetParent(parent, false);
                surfaceHost.transform.localPosition = Vector3.right * SurfaceStandoff;
            }

            var surface = surfaceHost.AddComponent<PackSurface>();

            var so = new UnityEditor.SerializedObject(surface);
            so.FindProperty("id").enumValueIndex = (int)PackSurfaceId.BackPanelLeft;
            so.FindProperty("size").vector2Value = FaceSize;
            so.ApplyModifiedPropertiesWithoutUndo();

            return surface;
        }

        /// <summary>
        /// Stands in for an item prefab: one visible box, no collider of its own. The collider
        /// goes because the copy is measured off renderers and the SOURCE must not be a candidate
        /// for the ray — <see cref="DisplayCopy"/> would have stripped it off the copy anyway.
        /// </summary>
        private GameObject SourceItem()
        {
            source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "TestItem";
            source.transform.position = FarAway;
            Object.DestroyImmediate(source.GetComponent<Collider>());

            return source;
        }

        private BoxCollider BuiltCopy(Transform parent = null)
        {
            copy = BackpackItemVisual.Build(SourceItem(), Surface(parent), FaceSize * 0.5f, 0f);
            Assert.IsNotNull(copy, "the copy itself");

            var box = copy.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "focus mode's cursor ray has nothing to hit without this");

            return box;
        }

        /// <summary>
        /// The thing a display copy hangs off: a body with a collider of its own, standing well
        /// clear of where the gear goes. The player, in miniature.
        ///
        /// <para>
        /// Gravity is off and rotation frozen so that the contact under test is the only thing in
        /// this scene that can move it — an untouched body here does not drift by a millimetre, and
        /// any reading at all is the shove. Its own collider is what makes it a compound in the
        /// first place: a Rigidbody with nothing on it would take the copy's box as its ONLY shape
        /// and the test would be about a different object.
        /// </para>
        /// </summary>
        private Rigidbody Wearer()
        {
            wearerGo = new GameObject("Wearer");

            var own = wearerGo.AddComponent<BoxCollider>();
            own.size = Vector3.one * 0.5f;

            var body = wearerGo.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            return body;
        }

        /// <summary>
        /// Something solid standing in the display copy and nowhere near the body carrying it — the
        /// ship's bulkhead, the doorway, the deck.
        ///
        /// <para>
        /// Overlapped by half its width along one axis rather than laid exactly over the copy: two
        /// boxes at the same pose share a centre, and the direction PhysX pushes a pair apart from
        /// there is not defined. Half an overlap has one shallowest way out, so a shove — if there
        /// is one — has a sign and a size worth reading.
        /// </para>
        /// <para>
        /// Static, with no Rigidbody: it cannot itself be the thing that moves, so the only body in
        /// the scene that can report anything is the one under test.
        /// </para>
        /// </summary>
        private void BulkheadHalfInside(BoxCollider box)
        {
            // Nothing has stepped physics yet and this project does not sync transforms
            // automatically, so the collider is still at its authored pose until this runs.
            Physics.SyncTransforms();
            Bounds bounds = box.bounds;

            bulkhead = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bulkhead.name = "Bulkhead";
            Object.DestroyImmediate(bulkhead.GetComponent<MeshRenderer>());
            bulkhead.transform.localScale = bounds.size;
            bulkhead.transform.position = bounds.center + Vector3.right * bounds.extents.x;

            Physics.SyncTransforms();
        }

        private static void StepPhysics()
        {
            for (int i = 0; i < Steps; i++) Physics.Simulate(Step);
        }

        /// <summary>
        /// The test this file exists for, asked of physics rather than of a field.
        ///
        /// <para>
        /// The copy's collider is checked to have actually joined the wearer's body FIRST, because
        /// the alternative is a green test that proves nothing: a fixture where the copy ended up
        /// hanging off nothing would sail through the shove that follows for entirely the wrong
        /// reason. A guard must not share its input with the thing it is guarding.
        /// </para>
        /// </summary>
        [Test]
        public void ADisplayCopyDoesNotShoveTheBodyItHangsOn()
        {
            Rigidbody wearer = Wearer();
            BoxCollider box = BuiltCopy(wearer.transform);

            Assert.AreSame(wearer, box.attachedRigidbody,
                           "this fixture is not reproducing the fault — the copy's collider has to " +
                           "have joined the wearer's Rigidbody, which is what makes it part of that " +
                           "body's shape in the first place");

            BulkheadHalfInside(box);

            Vector3 before = wearer.position;
            StepPhysics();

            Assert.AreEqual(0f, Vector3.Distance(wearer.position, before), Still,
                            $"the wearer was moved {Vector3.Distance(wearer.position, before):F3} m " +
                            "by a bulkhead standing in a STOWED ITEM. Nothing touched the wearer's " +
                            "own collider, so the copy is back in the simulation: on a worn pack " +
                            "that is a box bolted to the player's capsule, and on the ship's gear " +
                            "wall it is one bolted to the hull");
        }

        /// <summary>
        /// The control, and it is not optional. Everything above passes just as well on a fixture
        /// where the two boxes never met — a mis-seated copy, a bulkhead built in the wrong place, a
        /// simulation mode that quietly did not step. Putting the copy back in the simulation has to
        /// produce the shove, or the guard beside it is measuring nothing.
        /// </summary>
        [Test]
        public void TheControl_ACopyLeftInTheSimulationDoesShove()
        {
            Rigidbody wearer = Wearer();
            BoxCollider box = BuiltCopy(wearer.transform);

            box.excludeLayers = 0;

            BulkheadHalfInside(box);

            Vector3 before = wearer.position;
            StepPhysics();

            Assert.Greater(Vector3.Distance(wearer.position, before), Still,
                           "a copy left in the simulation did not shove its wearer, so this fixture " +
                           "cannot see the bug the test beside it exists to catch. Check that the " +
                           "bulkhead really overlaps the copy and that physics is being stepped");
        }

        /// <summary>
        /// The mechanism that currently makes the shove test above pass. Pinned separately because a
        /// change that swapped it for something that does not work — a trigger, which still reports
        /// contacts and still belongs to the compound body; a layer-matrix row, which would miss the
        /// copies that fall back to the rig's own layer — would read as a tidy-up in review.
        ///
        /// <para>
        /// On its own it is worth little: it asserts the edit, not the outcome. The shove test is
        /// the one that fails if Unity ever composes these masks differently.
        /// </para>
        /// </summary>
        [Test]
        public void ADisplayCopyIsExcludedFromEveryCollisionLayer()
        {
            BoxCollider box = BuiltCopy();

            Assert.AreEqual(Physics.AllLayers, box.excludeLayers.value,
                            "a stowed item is not an obstacle: on a worn pack this collider is " +
                            "part of the player's own body, and on the gear wall part of the ship's");
        }

        /// <summary>
        /// The other half: excluding the copy from collision must not have excluded it from
        /// queries, which is the whole reason the collider is there.
        /// </summary>
        [Test]
        public void ADisplayCopyIsStillHitByACursorRay()
        {
            BoxCollider box = BuiltCopy();

            // Transforms are not synced automatically in this project, so the collider is still at
            // its old pose as far as the query is concerned until this runs.
            Physics.SyncTransforms();

            Bounds bounds = box.bounds;
            float standoff = bounds.extents.y + 1f;
            Vector3 from = bounds.center + Vector3.up * standoff;

            bool hit = Physics.Raycast(from, Vector3.down, out RaycastHit info, standoff * 2f,
                                       Physics.AllLayers, QueryTriggerInteraction.Collide);

            Assert.IsTrue(hit, "the cursor ray found nothing where the copy is");
            Assert.AreSame(box, info.collider, "the ray hit something other than the copy");
        }
    }
}
