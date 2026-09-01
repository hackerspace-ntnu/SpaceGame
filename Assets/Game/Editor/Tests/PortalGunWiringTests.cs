// The portal gun's serialized parts actually resolve.
//
// This exists because of a failure that no amount of reading the code or the YAML could find, and
// that every other portal test passed straight through.
//
// THE INCIDENT. The gun fired, the blob flew to the wall, and no aperture ever opened — a hundred
// times in a row. The YAML on disk was correct: `portalPrefab: {fileID: 4199670190322545128, guid:
// d6fc2fe8…}`, and an object with exactly that guid and local id existed in PortalOrange.prefab.
// But the reference resolved to NULL at runtime, so PortalGunItem.OpenPortal returned at its own
// null guard, one line before it would have opened anything. Unity's cached import artifact for
// PortalGun.prefab had gone stale and disagreed with the file on disk — a ForceUpdate reimport
// fixed it and reported "Importer(PrefabImporter) generated inconsistent result" as it did so.
//
// WHY IT NEEDS A TEST RATHER THAN A FIX. There is nothing to fix in the repository: no file
// changed, and `git status` was empty afterwards. The bug lived entirely in one machine's Library
// cache, which means it can happen again to anyone whose editor imports a prefab while it is being
// rewritten underneath — which, in a project where prefab YAML is edited by tooling, is routine.
// A dangling reference is also what deleting a referenced prefab leaves behind, silently and with
// no error, so this guards two failure modes at once.
//
// The whole value is that it fails in the SUITE instead of in playtesting, where a null serialized
// reference is indistinguishable from "the feature is broken".
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools
{
    public class PortalGunWiringTests
    {
        private const string GunPath    = "Assets/Game/Prefabs/Items/Artifacts/Portals/PortalGun.prefab";
        private const string OrangePath = "Assets/Game/Prefabs/Items/Artifacts/Portals/PortalOrange.prefab";
        private const string BluePath   = "Assets/Game/Prefabs/Items/Artifacts/Portals/PortalBlue.prefab";
        private const string ItemAssetPath = "Assets/Game/Resources/Items/Artifacts/PortalGun.asset";
        private const string RigPath    = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private static T Load<T>(string path) where T : Component
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, $"prefab missing from the project: {path}");

            T component = go.GetComponent<T>();
            Assert.IsNotNull(component, $"{path} has no {typeof(T).Name} on its root");
            return component;
        }

        /// <summary>
        /// The one that would have caught the incident above.
        ///
        /// Read through the public property rather than the serialized field, because that is the
        /// route the save system uses to re-open a player's portals on load — so a null here breaks
        /// restoring a world as well as firing the gun.
        /// </summary>
        [Test]
        public void TheGunCanReachTheApertureItOpens()
        {
            PortalGunItem gun = Load<PortalGunItem>(GunPath);

            Assert.IsNotNull(gun.PortalPrefab,
                "the gun's aperture prefab reference is null. Nothing in the repository is wrong — " +
                "check for a stale import artifact and reimport " + GunPath + " with ForceUpdate. " +
                "Left null, every shot flies and silently opens nothing.");
        }

        [Test]
        public void TheGunsOwnPartsAreWired()
        {
            PortalGunItem gun = Load<PortalGunItem>(GunPath);
            var so = new SerializedObject(gun);

            // Only the ones whose absence is a defect. `sizeReference` on ItemGrip is genuinely
            // optional and is deliberately not asserted — a test that demands every slot be filled
            // gets switched off the first time somebody authors a legitimate blank.
            AssertReference(so, "muzzle");
            AssertReference(so, "jet");
            AssertReference(so, "bodyRenderer");
        }

        [Test]
        public void BothAperturePrefabsCanDrawThemselves()
        {
            foreach (string path in new[] { OrangePath, BluePath })
            {
                Portal portal = Load<Portal>(path);
                var so = new SerializedObject(portal);

                // Without the surface renderer there is no quad to put the view on, and the
                // aperture is invisible however correctly everything else behaves.
                AssertReference(so, "surfaceRenderer", path);
                AssertReference(so, "rimRenderer", path);

                // The door, not the window: the sweep measures this volume.
                AssertReference(so, "travellerVolume", path);
            }
        }

        [Test]
        public void TheJetIsBuiltButNotRunning()
        {
            PortalGunItem gun = Load<PortalGunItem>(GunPath);
            var so = new SerializedObject(gun);

            var jet = so.FindProperty("jet").objectReferenceValue as ParticleSystem;
            Assert.IsNotNull(jet, "the gun has no jet, so spraying is invisible");

            // Play On Awake is the defect worth a test. PortalGunItem.SetJet is what starts and
            // stops the jet, and a system that begins emitting the moment the gun is equipped
            // paints the floor at the player's feet with no trigger pull at all.
            Assert.IsFalse(jet.main.playOnAwake, "the jet emits before the trigger is pulled");
            Assert.IsTrue(jet.main.loop, "the jet stops after one burst instead of while held");
        }

        /// <summary>The hotbar instantiates the InventoryItem's prefab, not the one tests load.</summary>
        [Test]
        public void TheInventoryItemPointsAtTheGun()
        {
            string[] found = AssetDatabase.FindAssets("PortalGun t:ScriptableObject");
            Assert.IsNotEmpty(found, "no PortalGun InventoryItem asset in the project");

            bool reached = false;
            foreach (string guid in found)
            {
                var item = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (item == null) continue;

                SerializedProperty prefab = new SerializedObject(item).FindProperty("itemPrefab");
                if (prefab == null || prefab.objectReferenceValue == null) continue;

                var go = prefab.objectReferenceValue as GameObject;
                if (go != null && go.GetComponent<PortalGunItem>() != null) reached = true;
            }

            Assert.IsTrue(reached,
                "no PortalGun InventoryItem resolves to a prefab carrying PortalGunItem");
        }

        /// <summary>
        /// The gun stays UPRIGHT on the backpack, and this is the test that says so out loud.
        ///
        /// <para>
        /// It is a fire extinguisher, so the whole-roster orientation audit flags it — its longest
        /// axis is its own up, which is the shape of an item balanced on its end. It is not
        /// balanced on anything: the mesh carries 0.0130 m² of down-facing geometry in its bottom
        /// 5 mm, an annulus from r 0.018 to r 0.0505 m against a bottle radius of 0.0589, and
        /// <c>portal_gun.py</c> puts the origin at the centre of that ring expressly so the bottle
        /// "stands on a surface without a Z nudge". A foot is the exception Backpack.md carves out
        /// of "put the smallest axis up", and PortalContentBuilder carries the full reasoning.
        /// </para>
        /// <para>
        /// The bug this catches is a capacity one and it is silent. Laid on its side the gun
        /// measures 14 x 9 cells, which no face on the rig takes; <see cref="PackOverhang"/> then
        /// lashes it to the rack ski-fashion, occupying every one of that face's 81 cells — a
        /// third of the whole rig, for one item, where standing it costs 36 and fits strictly.
        /// Nothing warns, because overhang is a legitimate answer; the pack simply gets smaller.
        /// </para>
        /// </summary>
        [Test]
        public void TheGunStandsOnItsBaseRing()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            Assert.IsNotNull(asset, $"no InventoryItem at {ItemAssetPath}");

            ItemFootprint.ClearCache();
            Vector3 size = ItemFootprint.SizeOf(asset.itemPrefab);
            PackShape shape = PackShapes.For(asset, null);

            Assert.AreEqual(1, ItemFootprint.MaxAxis(size),
                $"the portal gun measures {size.ToString("F3")} and no longer stands on its base " +
                "ring. If that was deliberate, read PortalContentBuilder's note on why it is not " +
                "laid down before changing this test.");

            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.IsNotNull(rig, $"no rig at {RigPath}");

            PackSurface rack = rig.GetComponentsInChildren<PackSurface>(true)
                                  .FirstOrDefault(s => s.Id == PackSurfaceId.Rack);
            Assert.IsNotNull(rack, $"{RigPath} has lost its Rack face");

            // Strictly, not by overhang: the point of standing it up is that it fits a face the
            // way an item is supposed to, so the assertion is that the shape is inside the grid
            // BEFORE PackOverhang gets to rescue it.
            Vector2Int cells = rack.Cells;
            Assert.IsTrue(shape.Width <= cells.x && shape.Height <= cells.y,
                $"the portal gun is {shape.Width}x{shape.Height} cells and no longer fits the " +
                $"{cells.x}x{cells.y} rack without overhang — it now costs the whole face");
        }

        /// <summary>
        /// The gun is sized for the MAT, not for the hand — and the trap this pins is the
        /// fallback, not the number.
        ///
        /// <para>
        /// <c>ItemGrip.packSize</c> of <c>0</c> does not mean "unset". It means "follow
        /// <c>holdSize</c>", which is the HAND's number off <c>ItemScaleLadder</c>'s bracket
        /// ladder — a ladder that is deliberately not life size, because the astronaut's hand is
        /// about 1.7x a human's. This gun is a 0.4445 m fire extinguisher on the Gun bracket at
        /// 1.25 m, so through that fallback the pack drew it 1.875 m tall on a 1.08 m leaf and
        /// charged 36 of the rig's 255 cells for it.
        /// </para>
        /// <para>
        /// The bug that comes BACK is a bracket edit. Nudge <c>holdSize</c> for feel — which is
        /// exactly what the ladder exists to do, and what it has already done to eight prefabs —
        /// and with <c>packSize</c> at 0 the item's share of the pack moves with it, silently and
        /// with nothing in the diff to say the mat was involved. Authoring <c>packSize</c> cuts
        /// that link; this test is what notices if it is ever cut back.
        /// </para>
        /// </summary>
        [Test]
        public void TheGunIsSizedForTheMatNotTheHand()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunPath);
            Assert.IsNotNull(prefab, $"prefab missing from the project: {GunPath}");

            var grip = prefab.GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{GunPath} has no ItemGrip");

            Assert.Greater(grip.PackSize, 0f,
                "the portal gun's packSize is 0, so its size on the mat is wired to holdSize and " +
                "moves with the hand's bracket ladder. PortalContentBuilder.PackSize owns the " +
                "number — run SpaceGame ▸ Portals ▸ Build Portal Gun Content.");

            // Not merely non-zero: a mat size that has drifted back up toward the hand's is the
            // same defect wearing a different number.
            Assert.AreEqual(0.54f, grip.PackSize, 1e-3f,
                "the portal gun's mat size is no longer the 0.54 m PortalContentBuilder derives " +
                "from its true 0.4445 m model. Read that constant's note before changing this.");

            Assert.AreEqual(1.25f, grip.HoldSize, 1e-3f,
                "holdSize moved. It is the ItemScaleLadder Gun bracket and the hand's number " +
                "only — sizing the gun for the mat must not touch how it is held.");

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            Assert.IsNotNull(asset, $"no InventoryItem at {ItemAssetPath}");

            ItemFootprint.ClearCache();
            PackShape shape = PackShapes.For(asset, null);

            // 8 cells of 255, and every face on the rig but the one-cell lash line takes it
            // strictly. At the hand's size it was 36 cells and only the rack would have it.
            Assert.AreEqual(2, shape.Width, $"the gun is {shape.Width}x{shape.Height} cells, not 2x4");
            Assert.AreEqual(4, shape.Height, $"the gun is {shape.Width}x{shape.Height} cells, not 2x4");
        }

        private static void AssertReference(SerializedObject so, string field, string what = GunPath)
        {
            SerializedProperty property = so.FindProperty(field);
            Assert.IsNotNull(property, $"{what} has no serialized field '{field}'");
            Assert.IsNotNull(property.objectReferenceValue,
                $"{what}: '{field}' is null. If the YAML names a real object, the import artifact " +
                "is stale — reimport with ForceUpdate.");
        }
    }
}
