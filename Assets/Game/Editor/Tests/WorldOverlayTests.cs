// Tests for the world-anchored label overlay behind damage numbers and player nameplates.
//
// Two properties are worth pinning here, because breaking either shows up as "the feature quietly
// does nothing" rather than as an error.
//
// The first is that the overlay assembles itself: it is created from a RuntimeInitializeOnLoadMethod
// rather than placed in a scene, precisely so world streaming cannot unload it, which means nothing
// in the project holds a reference that would fail loudly if the construction stopped working.
//
// The second is that a label is anchored to the top of whatever it describes. The victims here span
// a crouching player to a six-legged habitat, and an offset measured wrong puts the text inside the
// model — visible in a screenshot, invisible to a compiler.
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    public class WorldOverlayTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject NewObject(string name = "entity")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>
        /// Builds the overlay through the same entry point the runtime bootstrap uses.
        ///
        /// It goes through Create() rather than AddComponent because Unity raises Awake on
        /// AddComponent in play mode and not outside it — which is exactly why construction is an
        /// explicit, idempotent Build() rather than something Awake alone is trusted to do.
        /// </summary>
        private WorldOverlay NewOverlay()
        {
            WorldOverlay overlay = WorldOverlay.Create();
            spawned.Add(overlay.gameObject);
            return overlay;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go == null) continue;

                // Released by hand: the damage listener lives on a STATIC event, and outside play
                // mode Unity raises neither OnDisable nor OnDestroy — so without this every test
                // leaves a listener behind holding labels the next teardown destroys.
                var numbers = go.GetComponent<DamageNumbers>();
                if (numbers != null) numbers.Unbind();

                Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        // ─────────── Construction ───────────

        [Test]
        public void OverlayBuildsItsOwnCanvas()
        {
            WorldOverlay overlay = NewOverlay();

            var canvas = overlay.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "The overlay must supply its own canvas — nothing places one for it.");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.Less(canvas.sortingOrder, 0,
                "World labels belong under PlayerHUD (order 0), so HUD chrome and menus draw over them.");

            Assert.IsNotNull(overlay.GetComponent<CanvasScaler>(),
                "Without a scaler the text is a different size on every display.");
        }

        [Test]
        public void OverlayHasNoRaycasterSoItCannotSwallowClicks()
        {
            WorldOverlay overlay = NewOverlay();

            Assert.IsNull(overlay.GetComponent<GraphicRaycaster>(),
                "A full-screen raycaster here would sit in front of every button in the game.");
        }

        [Test]
        public void OverlayBuildsBothSubsystems()
        {
            WorldOverlay overlay = NewOverlay();

            Assert.IsNotNull(overlay.GetComponent<DamageNumbers>());
            Assert.IsNotNull(overlay.GetComponent<PlayerNameplates>());
        }

        [Test]
        public void LayerIsAChildAndStretchedToTheCanvas()
        {
            WorldOverlay overlay = NewOverlay();

            Assert.IsNotNull(overlay.Layer, "Labels have nothing to parent to without a layer.");
            Assert.AreNotSame(overlay.transform, overlay.Layer,
                "The layer must not be the canvas's own RectTransform — Unity drives that one and " +
                "overwrites anchors written to it.");
            Assert.AreEqual(Vector2.zero, overlay.Layer.anchorMin);
            Assert.AreEqual(Vector2.one, overlay.Layer.anchorMax);
        }

        [Test]
        public void CreateIsIdempotent()
        {
            WorldOverlay first = NewOverlay();
            WorldOverlay second = NewOverlay();

            Assert.AreSame(first, second,
                "A second canvas would draw every nameplate and every damage number twice.");
            Assert.AreSame(first, WorldOverlay.Instance);
        }

        // ─────────── Labels ───────────

        [Test]
        public void CreateLabelProducesNonInteractiveText()
        {
            WorldOverlay overlay = NewOverlay();

            TextMeshProUGUI label = WorldOverlay.CreateLabel(overlay.Layer, "Probe", 28f, 300f);

            Assert.IsNotNull(label);
            Assert.AreSame(overlay.Layer, label.rectTransform.parent);
            Assert.IsFalse(label.raycastTarget,
                "A label that takes raycasts blocks the UI underneath it.");

            label.text = "-25";
            Assert.AreEqual("-25", label.text);
        }

        [Test]
        public void LabelsShareOneMaterialSoAPoolStillBatches()
        {
            WorldOverlay overlay = NewOverlay();

            TextMeshProUGUI a = WorldOverlay.CreateLabel(overlay.Layer, "A", 28f, 300f);
            TextMeshProUGUI b = WorldOverlay.CreateLabel(overlay.Layer, "B", 28f, 300f);

            Assert.AreSame(a.fontSharedMaterial, b.fontSharedMaterial,
                "Each label owning a material copy is what TMP's outlineWidth property does, and it " +
                "is why this builds the outlined material once instead.");
        }

        [Test]
        public void FadingALabelLeavesTheSharedMaterialAlone()
        {
            WorldOverlay overlay = NewOverlay();

            TextMeshProUGUI a = WorldOverlay.CreateLabel(overlay.Layer, "A", 28f, 300f);
            TextMeshProUGUI b = WorldOverlay.CreateLabel(overlay.Layer, "B", 28f, 300f);

            a.alpha = 0f;

            Assert.AreEqual(0f, a.alpha, 0.001f);
            Assert.AreEqual(1f, b.alpha, 0.001f,
                "Fading one number must not fade every other label sharing the material.");
        }

        // ─────────── Anchoring ───────────

        [Test]
        public void HeadOffsetFallsBackWhenThereIsNothingToMeasure()
        {
            Assert.Greater(WorldOverlay.HeadOffset(null), 0f);
            Assert.Greater(WorldOverlay.HeadOffset(NewObject("bare")), 0f,
                "An entity with no collider and no renderer must still get a usable offset.");
        }

        [Test]
        public void HeadOffsetClearsTheTopOfTheCollider()
        {
            GameObject body = NewObject("body");
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 1f, 0f);   // stands on the origin, 2 m tall

            float offset = WorldOverlay.HeadOffset(body);

            Assert.Greater(offset, 2f, "The label must sit above the head, not inside it.");
            Assert.Less(offset, 2.8f, "…but not float far off into the sky.");
        }

        [Test]
        public void HeadOffsetScalesWithTheEntity()
        {
            GameObject small = NewObject("small");
            var smallBox = small.AddComponent<BoxCollider>();
            smallBox.size = new Vector3(1f, 1f, 1f);
            smallBox.center = new Vector3(0f, 0.5f, 0f);

            GameObject large = NewObject("large");
            var largeBox = large.AddComponent<BoxCollider>();
            largeBox.size = new Vector3(4f, 6f, 4f);
            largeBox.center = new Vector3(0f, 3f, 0f);

            Assert.Greater(WorldOverlay.HeadOffset(large), WorldOverlay.HeadOffset(small),
                "A fixed offset would bury the label inside anything bigger than a person.");
        }

        [Test]
        public void HeadOffsetIgnoresTriggerVolumes()
        {
            GameObject body = NewObject("body");
            var solid = body.AddComponent<BoxCollider>();
            solid.size = new Vector3(1f, 2f, 1f);
            solid.center = new Vector3(0f, 1f, 0f);

            float withoutTrigger = WorldOverlay.HeadOffset(body);

            // An aggro range or pickup radius, of the kind entities routinely carry.
            var aggro = body.AddComponent<SphereCollider>();
            aggro.isTrigger = true;
            aggro.radius = 20f;

            Assert.AreEqual(withoutTrigger, WorldOverlay.HeadOffset(body), 0.001f,
                "A 20 m aggro sphere would put the nameplate 20 m above the creature's head.");
        }

        // ─────────── Damage numbers ───────────
        //
        // The bug these exist for: the number was originally announced only by
        // NetworkedHealthComponent, so anything carrying a plain HealthComponent — a crate, a test
        // cube, the two creature prefabs that never got the networked component — took damage in
        // silence. Every assertion below is on a victim with NO networking whatsoever.

        /// <summary>A victim with nothing but a HealthComponent, as most damageable things are.</summary>
        private HealthComponent NewVictim()
        {
            GameObject go = NewObject("victim");
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, 1f);
            box.center = new Vector3(0f, 1f, 0f);
            return go.AddComponent<HealthComponent>();
        }

        /// <summary>
        /// The local player's weapon: a child of a Player-tagged root, which is how an equipped item
        /// actually sits — instantiated onto a socket on the rig.
        /// </summary>
        private GameObject NewPlayerWeapon()
        {
            GameObject player = NewObject("player");
            player.tag = "Player";

            var weapon = new GameObject("laser staff");
            weapon.transform.SetParent(player.transform, false);
            return weapon;
        }

        [Test]
        public void PlainHealthComponentStillRaisesTheDamageSignal()
        {
            HealthComponent victim = NewVictim();
            GameObject weapon = NewPlayerWeapon();

            HealthComponent seen = null;
            int seenAmount = 0;
            void Handler(HealthComponent h, int a) { seen = h; seenAmount = a; }

            HealthComponent.AnyDamaged += Handler;
            try
            {
                NetDamage.Apply(victim.gameObject, 20, weapon.transform);
            }
            finally
            {
                HealthComponent.AnyDamaged -= Handler;
            }

            Assert.AreSame(victim, seen,
                "Damage on an unnetworked victim must still announce itself — this is the whole bug.");
            Assert.AreEqual(20, seenAmount);
            Assert.AreSame(weapon.transform, victim.LastDamageSource,
                "The source has to survive, or nothing can tell whose hit it was.");
        }

        [Test]
        public void AHitFromTheLocalPlayerDrawsANumber()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            int before = overlay.Layer.childCount;

            NetDamage.Apply(NewVictim().gameObject, 25, NewPlayerWeapon().transform);

            Assert.Greater(overlay.Layer.childCount, before,
                "Shooting something with a plain HealthComponent must put a number on screen.");
        }

        [Test]
        public void DamageWithNoSourceDrawsNothing()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            int before = overlay.Layer.childCount;

            // A fall, or a cactus: real damage, but nobody to credit for it.
            NetDamage.Apply(NewVictim().gameObject, 15, null);

            Assert.AreEqual(before, overlay.Layer.childCount,
                "Environmental damage is not something the player did, so it gets no number.");
        }

        [Test]
        public void AHitFromSomebodyElseDrawsNothing()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            int before = overlay.Layer.childCount;

            // An untagged attacker stands in for another creature: it is not this machine's player.
            GameObject other = NewObject("wild animal");
            NetDamage.Apply(NewVictim().gameObject, 15, other.transform);

            Assert.AreEqual(before, overlay.Layer.childCount,
                "Only damage the local player dealt is drawn.");
        }

        [Test]
        public void RepeatedHitsReuseThePool()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            HealthComponent victim = NewVictim();
            victim.RestoreHealth(100000);
            GameObject weapon = NewPlayerWeapon();

            int before = overlay.Layer.childCount;

            for (int i = 0; i < 40; i++)
                NetDamage.Apply(victim.gameObject, 1, weapon.transform);

            Assert.LessOrEqual(overlay.Layer.childCount - before, 24,
                "The pool is capped; a burst weapon must not create a label per bullet forever.");
        }

        // ─────────── Projection ───────────

        /// <summary>
        /// A camera at the origin looking down +Z, pinned as the overlay's eye.
        ///
        /// Pinned rather than tagged MainCamera because these tests run against whatever scene the
        /// editor happens to have open, and Camera.main would just as happily answer with that
        /// scene's camera, wherever in the world it is pointing.
        /// </summary>
        private Camera GiveEye(WorldOverlay overlay)
        {
            GameObject camGo = NewObject("eye");
            Camera cam = camGo.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;

            overlay.EyeOverride = cam;
            return cam;
        }

        [Test]
        public void PointsBehindTheCameraDoNotProject()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            Assert.IsFalse(overlay.Project(new Vector3(0f, 0f, -10f), out _),
                "Behind the camera the projection folds back and would place the label on the " +
                "opposite side of the screen from the thing it describes.");
        }

        [Test]
        public void PointsInFrontOfTheCameraProject()
        {
            WorldOverlay overlay = NewOverlay();
            GiveEye(overlay);

            Assert.IsTrue(overlay.Project(new Vector3(0f, 0f, 10f), out Vector2 point));
            Assert.IsTrue(overlay.IsOnScreen(point),
                "A point straight ahead lands in the middle of the screen.");
        }

        [Test]
        public void ProjectionTracksTheCameraTurning()
        {
            WorldOverlay overlay = NewOverlay();
            Camera cam = GiveEye(overlay);

            Assert.IsTrue(overlay.Project(new Vector3(0f, 0f, 10f), out Vector2 ahead));

            // Turn away: what was straight ahead is now behind.
            cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            Assert.IsFalse(overlay.Project(new Vector3(0f, 0f, 10f), out _),
                "A label must not stay pinned where it was when the player turns around.");
            Assert.IsTrue(overlay.IsOnScreen(ahead));
        }
    }
}
