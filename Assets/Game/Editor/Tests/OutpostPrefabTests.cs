// The outposts: the layout names only pieces that exist, every outpost prefab is built from it, and what makes one usable by residents is
// there -- a sit spot for every seat, a ladder that ends on its deck, a rest and a stop for every prop. Reads the built prefabs; nothing is baked.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Residents;
using SpaceGame.EditorTools.Outposts;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class OutpostPrefabTests
    {
        private const float LadderTopTolerance = 0.05f;

        private OutpostLayoutFile layouts;
        private readonly Dictionary<string, GameObject> contents = new();

        [OneTimeSetUp]
        public void LoadLayoutsAndPrefabs()
        {
            layouts = OutpostLayoutFile.Parse(File.ReadAllText(OutpostPrefabBuilder.LayoutPath));
            foreach (OutpostLayout layout in layouts.outposts)
            {
                string path = $"{OutpostPrefabBuilder.OutputDir}/{layout.name}.prefab";
                Assert.IsTrue(File.Exists(path), $"{layout.name} has no prefab: run Tools/SpaceGame/Outposts/Build Missing Outpost Prefabs");
                contents[layout.name] = PrefabUtility.LoadPrefabContents(path);
            }
        }

        [OneTimeTearDown]
        public void UnloadPrefabs()
        {
            foreach (GameObject root in contents.Values) PrefabUtility.UnloadPrefabContents(root);
            contents.Clear();
        }

        [Test]
        public void TheLayoutHoldsOutpostsOfRealPieces()
        {
            Assert.IsNotEmpty(layouts.outposts);
            foreach (OutpostLayout layout in layouts.outposts)
            {
                Assert.GreaterOrEqual(layout.pieces.Length, 4, layout.name);
                foreach (OutpostPiece piece in layout.pieces)
                {
                    Assert.AreEqual(1f, Quaternion.Dot(piece.Rotation, piece.Rotation), 1e-3f, $"{layout.name}/{piece.name} rotation is not a rotation");
                    Assert.Greater(piece.Scale.x * piece.Scale.y * piece.Scale.z, 0f, $"{layout.name}/{piece.name} scale");
                }
            }
        }

        [Test]
        public void EveryPieceKindIsADecorationPrefabOrAnItemWithAModel()
        {
            string[] decorations = AssetDatabase.FindAssets("t:Prefab Deco_", new[] { "Assets/Game/Prefabs/Environment/Decorations" })
                .Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))).ToArray();

            foreach (string kind in layouts.outposts.SelectMany(o => o.pieces).Select(p => p.kind).Distinct())
            {
                if (OutpostItemModels.IsItem(kind))
                {
                    InventoryItem item = OutpostItemModels.LoadItem(kind);
                    Assert.IsTrue(item != null && item.itemPrefab != null, $"{kind} is an item with no asset or no prefab");
                }
                else Assert.Contains($"Deco_{kind}", decorations, $"no Deco_{kind} prefab");
            }
        }

        [Test]
        public void EveryOutpostPrefabHoldsEveryPiece()
        {
            foreach (OutpostLayout layout in layouts.outposts)
            {
                int props = layout.pieces.Count(p => OutpostProps.IsProp(p.kind));
                int pieces = contents[layout.name].transform.Find("Pieces").childCount;
                Assert.AreEqual(layout.pieces.Length - props, pieces, $"{layout.name}: pieces in the prefab");
            }
        }

        [Test]
        public void EverySitSpotHasASeatAndEverySeatPieceHasASpot()
        {
            float reach = ResidentTuning.Instance.seatReach;
            foreach ((string name, GameObject root) in contents)
            {
                SettlementSpot[] sitSpots = root.GetComponentsInChildren<SettlementSpot>(true).Where(s => s.Use != null && s.Use.seated && s.gameObject.activeInHierarchy).ToArray();
                Seat[] seats = root.GetComponentsInChildren<Seat>(true);

                List<int> seatless = SettlementPlaces.SeatlessSpots(sitSpots.Select(s => s.Position).ToList(), seats, reach);
                Assert.IsEmpty(seatless, $"{name}: sit spot(s) with no seat: {string.Join(", ", seatless.Select(i => sitSpots[i].name))}");

                int seatPieces = root.transform.Find("Pieces").Cast<Transform>().Count(p => OutpostSeating.IsSeat(p.name.Substring(0, p.name.LastIndexOf('_'))));
                int ownSpots = root.GetComponentsInChildren<SettlementSpot>(true).Count(s => s.name.StartsWith("Spot_Seat_"));
                Assert.AreEqual(seatPieces, ownSpots, $"{name}: a sit spot for every loose seat");
            }
        }

        [Test]
        public void EveryOutpostNamesItsWalkableHeartOnTheGround()
        {
            foreach ((string name, GameObject root) in contents)
            {
                SettlementHeart[] hearts = root.GetComponentsInChildren<SettlementHeart>(true);
                Assert.AreEqual(1, hearts.Length, $"{name}: one heart, so the settlement's guess cannot land on a deck");
                Assert.AreEqual(0f, hearts[0].transform.position.y, 1e-3f, $"{name}: the heart is on the ground");
            }
        }

        [Test]
        public void EveryLadderEndsOnItsDeck()
        {
            foreach ((string name, GameObject root) in contents)
            {
                foreach (Ladder ladder in root.GetComponentsInChildren<Ladder>(true))
                {
                    Assert.IsTrue(ladder.IsValid, $"{name}/{ladder.name} has no top or exit");
                    Assert.Greater(ladder.TopHeight, ladder.Foot.y + 1f, $"{name}/{ladder.name} climbs less than a metre");
                    Assert.AreEqual(ladder.TopHeight, ladder.ExitPoint.y, LadderTopTolerance, $"{name}/{ladder.name}: the exit is not at the step-off height");
                }
            }
        }

        [Test]
        public void EveryPropHasAHomeRestAStopServesAndAnItemAResidentCanCarry()
        {
            var carry = new SerializedObject(ResidentTuning.Instance).FindProperty("carryItems");
            var carriable = Enumerable.Range(0, carry.arraySize).Select(i => carry.GetArrayElementAtIndex(i).objectReferenceValue).ToList();

            foreach ((string name, GameObject root) in contents)
            {
                foreach (SettlementProp prop in root.GetComponentsInChildren<SettlementProp>(true))
                {
                    Assert.Contains(prop.Item, carriable, $"{name}/{prop.name}: {prop.Item.name} is not in ResidentTuning.carryItems");
                    Assert.IsNotNull(prop.Home, $"{name}/{prop.name} has no home rest");
                }
                foreach (PropRest rest in root.GetComponentsInChildren<PropRest>(true))
                    Assert.IsNotNull(rest.Spot, $"{name}/{rest.name} is served by no stop");
            }
        }
    }
}
