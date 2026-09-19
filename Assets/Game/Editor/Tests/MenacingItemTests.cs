// Which items an NPC reads as a weapon, read off the assets.
//
// `InventoryItem.menacing` is authored per item and there is no way to derive it: "weapon" is not a
// C# class in this project. Only two of the seven guns an NPC can roll — BasicGun and
// BallLightningWeapon — are `Weapon` subclasses; the rest are ordinary `UsableItem` artifacts, so
// `held is Weapon` would call a bazooka harmless. A flag is the honest answer, and a flag needs a
// list somebody can review, which is this file.
//
// The rule, so the next person can apply it to a new item: tick it if the item is unmistakably a
// weapon when it is pointed at you. Tools, placeables, ship parts, potions and supplies are not,
// and **a gauntlet is never menacing whatever it does** — it is gear you are wearing rather than
// something you have drawn, so a wrist blade reads no differently from a torch.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class MenacingItemTests
    {
        /// <summary>
        /// Every item that counts as brandished. Listed here with the reason, the way
        /// `PackSizeTests` lists the items whose pack size diverges: an authored flag with no
        /// review surface drifts one prefab at a time.
        /// </summary>
        private static readonly Dictionary<string, string> Menacing = new()
        {
            ["basicgun"]            = "the starter sidearm",
            ["GravelBlaster"]       = "a shotgun in all but name",
            ["NetGun"]              = "fires a capture net at people",
            ["LaserStaff"]          = "a beam weapon held two-handed",
            ["BallLightningWeapon"] = "the Cixin gun; charges an orb at you",
            ["LightningSpell"]      = "throws lightning",
            ["DragonBazooka"]       = "a rocket launcher",
            ["Flamethrower"]        = "the hand-held one; the gauntlet version is gear and is not listed",
            ["CryoSprayer"]         = "freezes whatever it is pointed at",
            ["ConjurerStaff"]       = "calls down a bolt where it is aimed",
            ["BottledSingularity"]  = "thrown, and it takes whoever is nearby with it",
        };

        private static IEnumerable<(string Name, InventoryItem Item, SerializedObject So)> AllItems() =>
            AssetDatabase.FindAssets("t:InventoryItem")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => (Name: System.IO.Path.GetFileNameWithoutExtension(p),
                              Item: AssetDatabase.LoadAssetAtPath<InventoryItem>(p),
                              Path: p))
                .Where(t => t.Item != null)
                .Select(t => (t.Name, t.Item, new SerializedObject(t.Item)));

        [Test]
        public void EveryListedWeaponIsMarkedMenacing()
        {
            var seen = new HashSet<string>();

            foreach ((string name, InventoryItem item, SerializedObject _) in AllItems())
            {
                if (!Menacing.ContainsKey(name)) continue;

                seen.Add(name);
                Assert.IsTrue(item.menacing,
                    $"{name} is a weapon ({Menacing[name]}) but is not marked menacing, so " +
                    "brandishing it at somebody does nothing.");
            }

            CollectionAssert.IsEmpty(Menacing.Keys.Except(seen).ToList(),
                "these are listed here but no such InventoryItem exists — renamed or deleted");
        }

        [Test]
        public void NothingElseIsMarkedMenacing()
        {
            foreach ((string name, InventoryItem item, SerializedObject _) in AllItems())
            {
                if (Menacing.ContainsKey(name)) continue;

                Assert.IsFalse(item.menacing,
                    $"{name} is marked menacing but is not in the list in this file. Add it with " +
                    "the reason, or clear the flag — an NPC turning hostile because you were " +
                    "holding a lantern is the failure this list exists to prevent.");
            }
        }

        /// <summary>
        /// The rule the user set, and the one most likely to be broken by someone adding a weapon
        /// gauntlet: a gauntlet is worn gear, not a drawn weapon. The FlameGauntlet and the
        /// WristBlade are both plainly dangerous and both must stay unmarked.
        /// </summary>
        [Test]
        public void NoGauntletIsEverMenacing()
        {
            int checkedCount = 0;

            foreach ((string name, InventoryItem item, SerializedObject so) in AllItems())
            {
                SerializedProperty kind = so.FindProperty("equipKind");
                if (kind == null || kind.enumNames[kind.enumValueIndex] != "Gauntlet") continue;

                checkedCount++;
                Assert.IsFalse(item.menacing,
                    $"{name} is a gauntlet. Gauntlets are gear you are wearing rather than " +
                    "something you have drawn, so they never read as a threat — however sharp.");
            }

            Assert.Greater(checkedCount, 0, "no gauntlets found; has equipKind been renamed?");
        }

        /// <summary>
        /// Worn kinds cannot be brandished either — nothing is in the hand — so the flag would be
        /// inert rather than wrong. Asserted anyway so it never becomes load-bearing by accident.
        /// </summary>
        [Test]
        public void NoWornItemIsMenacing()
        {
            foreach ((string name, InventoryItem item, SerializedObject so) in AllItems())
            {
                SerializedProperty kind = so.FindProperty("equipKind");
                if (kind == null || kind.enumNames[kind.enumValueIndex] == "Hand") continue;

                Assert.IsFalse(item.menacing, $"{name} is worn, not held");
            }
        }
    }
}
