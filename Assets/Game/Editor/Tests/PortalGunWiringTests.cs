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
        private const string BlobPath   = "Assets/Game/Prefabs/Items/Artifacts/Portals/PortalBlob.prefab";

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
            AssertReference(so, "projectilePrefab");
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
        public void TheBlobCanBeSeenAndCanReportArrival()
        {
            PortalProjectile blob = Load<PortalProjectile>(BlobPath);

            // On the ROOT specifically. The blob's Update is what flies it and then fires the
            // callback that opens the aperture, so a component parked on an inactive child would
            // leave a bead of light hanging at the muzzle forever — which is very close to what the
            // incident above looked like from the player's side.
            Assert.IsTrue(blob.gameObject.activeSelf, "the blob prefab's root is inactive");
            Assert.IsTrue(blob.enabled, "the blob's PortalProjectile is disabled");

            AssertReference(new SerializedObject(blob), "blobRenderer", BlobPath);
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
