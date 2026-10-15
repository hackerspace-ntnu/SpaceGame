// A chore that carries props, run through the real ErrandRunner on a tiny settlement: the round fetches the basket
// from the source spot's rest, the hand shows the basket's item on the way, it is set down at the target's free
// rest, and a round cut short with the basket in hand lands it where it was going. No NavMesh: dwells are zero
// and "arrived" is told, so only the runner's own decisions are measured.
using NUnit.Framework;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class PropErrandTests
    {
        private GameObject root;
        private Resident hauler;
        private SettlementProp basket;
        private PropRest home, shelf;
        private ChoreDefinition chore;
        private SpotUse pile, store;
        private ResidentArchetype archetype;
        private SettlementCulture culture;
        private SettlementConfig config;
        private InventoryItem wicker;

        [SetUp]
        public void SetUp()
        {
            wicker = AssetDatabase.LoadAssetAtPath<InventoryItem>("Assets/Game/Resources/Items/Tools/Carry_Basket_Wicker.asset");
            Assume.That(ResidentTuning.Instance.PropIndexOf(wicker), Is.GreaterThan(0), "the tuning carries the wicker basket");

            pile = Use("Pile");
            store = Use("Store");
            chore = ScriptableObject.CreateInstance<ChoreDefinition>();
            (chore.source, chore.target, chore.carried, chore.carriesProps) = (pile, store, wicker, true);
            (chore.sourceSeconds, chore.targetSeconds, chore.targetsPerRound) = (Vector2.zero, Vector2.zero, Vector2Int.one);
            archetype = ScriptableObject.CreateInstance<ResidentArchetype>();
            archetype.chore = chore;
            culture = ScriptableObject.CreateInstance<SettlementCulture>();
            config = ScriptableObject.CreateInstance<SettlementConfig>();
            config.culture = culture;

            root = new GameObject("Settlement");
            var settlement = root.AddComponent<Settlement>();
            var so = new SerializedObject(settlement);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();

            Transform generated = Child("Generated", root.transform, Vector3.zero);
            Transform building = Child("Building", generated, Vector3.zero);
            SettlementSpot pileSpot = Spot("PileSpot", building, new Vector3(0f, 0f, 2f), pile);
            SettlementSpot storeSpot = Spot("StoreSpot", building, new Vector3(10f, 0f, 2f), store);
            home = Rest("Home", building, Vector3.zero, pileSpot);
            shelf = Rest("Shelf", building, new Vector3(10f, 0f, 0f), storeSpot);
            basket = Child("Basket", building, Vector3.zero).gameObject.AddComponent<SettlementProp>();
            Set(basket, "home", home);
            Set(basket, "item", wicker);
            building.gameObject.AddComponent<SettlementPropSync>();

            hauler = Child("Hauler", root.transform, new Vector3(1f, 0f, 3f)).gameObject.AddComponent<Resident>();
            (hauler.settlement, hauler.archetype, hauler.seed) = (settlement, archetype, 7);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (Object o in new Object[] { chore, pile, store, archetype, culture, config }) Object.DestroyImmediate(o);
        }

        [Test]
        public void ARound_FetchesTheBasket_CarriesItsItem_AndSetsItDownAtTheTargetRest()
        {
            var runner = new ErrandRunner();
            SettlementSociety society = hauler.Society;
            SettlementProps props = root.GetComponent<Settlement>().Props;
            var segment = new PlanSegment { activity = Activity.Chore, place = society.PlaceOf(home.Spot), arrive = 0f, leave = 600f };

            ErrandStop first = runner.Step(hauler, society, segment, arrived: false);
            Assert.AreSame(basket, first.pick, "the round starts at the basket");
            Assert.AreEqual(0, first.prop, "nothing in hand on the way there");

            ErrandStop second = runner.Step(hauler, society, segment, arrived: true);
            Assert.AreEqual(SettlementPropSync.Carried, props.StateOf(basket), "picked up when the dwell at the source ends");
            Assert.AreSame(basket, second.drop);
            Assert.AreEqual(ResidentTuning.Instance.PropIndexOf(wicker), second.prop, "the hand shows the basket's item");

            runner.Step(hauler, society, segment, arrived: true);
            Assert.AreEqual(props.IndexOf(shelf), props.StateOf(basket), "set down at the target's free rest");
            Assert.That(Vector3.Distance(basket.transform.position, shelf.Position), Is.LessThan(1e-4f));
        }

        [Test]
        public void ARoundCutShort_WithTheBasketInHand_LandsItWhereItWasGoing()
        {
            var runner = new ErrandRunner();
            SettlementSociety society = hauler.Society;
            SettlementProps props = root.GetComponent<Settlement>().Props;
            var segment = new PlanSegment { activity = Activity.Chore, place = society.PlaceOf(home.Spot), arrive = 0f, leave = 600f };

            runner.Step(hauler, society, segment, arrived: false);
            runner.Step(hauler, society, segment, arrived: true);
            Assume.That(props.StateOf(basket), Is.EqualTo(SettlementPropSync.Carried));

            runner.Reset();
            Assert.AreEqual(props.IndexOf(shelf), props.StateOf(basket));
            Assert.IsTrue(props.IsFree(props.IndexOf(home)), "and no promise is left behind");
            Assert.AreSame(basket, props.FreePropAt(props.IndexOf(shelf)), "nobody holds a promise on the basket any more");
        }

        private static SpotUse Use(string name)
        {
            var use = ScriptableObject.CreateInstance<SpotUse>();
            use.name = name;
            use.role = SpotRole.Errand;
            return use;
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        private static SettlementSpot Spot(string name, Transform parent, Vector3 position, SpotUse use)
        {
            SettlementSpot spot = Child(name, parent, position).gameObject.AddComponent<SettlementSpot>();
            Set(spot, "use", use);
            return spot;
        }

        private static PropRest Rest(string name, Transform parent, Vector3 position, SettlementSpot spot)
        {
            PropRest rest = Child(name, parent, position).gameObject.AddComponent<PropRest>();
            Set(rest, "spot", spot);
            return rest;
        }

        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
