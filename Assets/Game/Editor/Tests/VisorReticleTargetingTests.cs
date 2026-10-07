// The bracket and its info box must land on what the crosshair is on.
//
// VisorReticle used to answer "where is the target" by taking the union of every Renderer under
// the IInteractable COMPONENT. That is a different question from the one the Interactor already
// answered, and it went wrong three ways at once: an interactable resolved off a parent
// (GetComponentInParent, which is how a hull collider answers) bracketed the whole hierarchy at
// its centroid; an interactable whose collider is a bare trigger standing proud of the machine —
// which is the ONLY arrangement that lets a receptacle inside a solid fixture be aimed at — had no
// renderers under it at all and fell back to the trigger's own origin, marking the empty air the
// trigger pads out into; and a component with children spread wide centred between them.
//
// The Interactor knows exactly which collider the ray met and where. These two decisions are what
// turns that into something to draw.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class VisorReticleTargetingTests
    {
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        /// <summary>A GameObject with a real Renderer on it, parented where asked.</summary>
        private Transform Drawn(string name, Transform parent = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            else spawned.Add(go);
            return go.transform;
        }

        /// <summary>A GameObject with no Renderer — a bare trigger volume or a pivot.</summary>
        private Transform Invisible(string name, Transform parent = null)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else spawned.Add(go);
            return go.transform;
        }

        [Test]
        public void FramedSubject_TakesWhatTheRayActuallyHit()
        {
            Transform fixture = Drawn("Fixture");
            Transform panel = Drawn("Panel", fixture);

            Assert.AreSame(panel, VisorReticle.FramedSubject(panel, fixture));
        }

        [Test]
        public void FramedSubject_ClimbsPastABareTriggerToTheThingItGuards()
        {
            Transform fixture = Drawn("Fixture");
            Transform dockVolume = Invisible("DockVolume", fixture);

            Assert.AreSame(fixture, VisorReticle.FramedSubject(dockVolume, fixture));
        }

        [Test]
        public void FramedSubject_NeverClimbsPastTheInteractable()
        {
            // The hull is drawn; the interactable between it and the hit is not. Climbing one step
            // further would bracket the whole ship — the failure this bound exists to prevent.
            Transform hull = Drawn("Hull");
            Transform station = Invisible("Station", hull);
            Transform trigger = Invisible("Trigger", station);

            Assert.IsNull(VisorReticle.FramedSubject(trigger, station));
        }

        [Test]
        public void FramedSubject_MarksNothingWhenNothingUnderTheAimIsDrawn()
        {
            Transform station = Invisible("Station");
            Transform trigger = Invisible("Trigger", station);

            Assert.IsNull(VisorReticle.FramedSubject(trigger, station));
        }

        [Test]
        public void FramedSubject_FallsBackToTheInteractableWhenTheHitIsSomewhereElse()
        {
            // InteractableProxy redirects a press to a component on another GameObject, so the hit
            // and the interactable need not share a hierarchy at all.
            Transform plate = Invisible("PressPlate");
            Transform elsewhere = Drawn("Machine");

            Assert.AreSame(elsewhere, VisorReticle.FramedSubject(plate, elsewhere));
        }

        [Test]
        public void FramedSubject_SurvivesAHitWithNoColliderTransform()
        {
            Transform fixture = Drawn("Fixture");

            Assert.AreSame(fixture, VisorReticle.FramedSubject(null, fixture));
            Assert.IsNull(VisorReticle.FramedSubject(null, null));
        }

        [Test]
        public void MarksHitPoint_WhenTheSubjectIsTooBigToFrame()
        {
            Assert.IsTrue(VisorReticle.MarksHitPoint(projectedSize: 900f, maxSize: 420f));
        }

        [Test]
        public void MarksHitPoint_LeavesSomethingThatFitsAlone()
        {
            Assert.IsFalse(VisorReticle.MarksHitPoint(projectedSize: 120f, maxSize: 420f));
            Assert.IsFalse(VisorReticle.MarksHitPoint(projectedSize: 420f, maxSize: 420f));
        }
    }
}
