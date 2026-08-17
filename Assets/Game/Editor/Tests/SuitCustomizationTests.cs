using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The suit colour feature, minus the parts that need a live Lobby service and two machines.
    ///
    /// <para>
    /// The load-bearing test here is <see cref="EveryRecolouredMaterialStillExistsOnTheModel"/>.
    /// <see cref="SuitRecolor"/> matches materials <b>by name</b>, so a Blender re-export that
    /// renames one does not break the build, throw, or log anything at import time — it just
    /// silently stops recolouring part of the astronaut. This is the only thing standing between
    /// that and a player reporting that their trim went orange again.
    /// </para>
    /// </summary>
    public class SuitCustomizationTests
    {
        /// <summary>
        /// The model actually in use — the one astronaut_export.py writes and both
        /// PlayerCharacter.prefab and LobbyPreviewAstronaut.prefab are built from. Pointing this at
        /// a stale second copy would let the test pass while the shipped suit stopped recolouring.
        /// </summary>
        private const string ModelPath =
            "Assets/Game/Art/Models/Characters/Astronaut/astronaut.fbx";

        // ─────────────────────────────────────────────────────────────────────── the model

        [Test]
        public void EveryRecolouredMaterialStillExistsOnTheModel()
        {
            HashSet<string> onModel = MaterialNamesOnModel();

            foreach (SuitPalette.Relationship relationship in SuitPalette.Relationships)
            {
                Assert.IsTrue(onModel.Contains(relationship.MaterialName),
                    $"SuitPalette expects '{relationship.MaterialName}' on {ModelPath}, and it is " +
                    "not there. A re-export renamed it, so that part of the suit no longer takes " +
                    $"the player's colour. Names actually present: {string.Join(", ", onModel)}");
            }
        }

        [Test]
        public void TheNeutralMaterialsAreLeftAlone()
        {
            // The greys and blacks are most of the model, and they are what stops a bright suit
            // reading as a jelly bean. If one of them ever ends up in the table, every astronaut
            // turns into a single solid colour.
            string[] neutrals =
            {
                "Material.044", // suit body
                "Material.050", // helmet
                "Material.051", // gloves, boots
                "Material.052", // hardware
                "Material.054", // tubing
            };

            foreach (string neutral in neutrals)
                foreach (SuitPalette.Relationship relationship in SuitPalette.Relationships)
                    Assert.AreNotEqual(neutral, relationship.MaterialName,
                        $"'{neutral}' is one of the astronaut's neutrals and must not be recoloured.");
        }

        [Test]
        public void TheHeadBoneTheNameHangsOverStillExists()
        {
            // LobbyPreviewRank finds it by name to hang the nameplate on. Without it the name is
            // drawn at the astronaut's feet.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, $"No astronaut model at {ModelPath}.");

            bool found = false;
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
                if (bone.name == "mixamorig:Head")
                    found = true;

            Assert.IsTrue(found, "mixamorig:Head is gone from the astronaut rig, so the lobby " +
                                 "nameplates have nothing to hang on.");
        }

        // ───────────────────────────────────────────────────────────────────────── palette

        [Test]
        public void EmberIsFirstSoAnUntouchedPlayerLooksUnchanged()
        {
            // Swatch 0 is what a peer on an older build, a corrupt preference, or an unparseable
            // lobby value all fall back to. It has to be the colour the astronaut already was.
            Assert.AreEqual("Ember", SuitPalette.NameOf(0));

            Color ember = SuitPalette.ColorOf(0);
            var authored = (Color)new Color32(0xE7, 0x77, 0x1C, 255);

            Assert.AreEqual(authored.r, ember.r, 0.001f);
            Assert.AreEqual(authored.g, ember.g, 0.001f);
            Assert.AreEqual(authored.b, ember.b, 0.001f);
        }

        [Test]
        public void TheHarnessWearsTheChosenColourExactly()
        {
            // The three zero-offset materials are the dominant zone. If the maths drifted, the part
            // the player is actually looking at would not be the colour they picked.
            foreach (SuitPalette.Relationship relationship in SuitPalette.Relationships)
            {
                if (relationship.HueOffset != 0f) continue;

                for (int i = 0; i < SuitPalette.Count; i++)
                {
                    Color chosen = SuitPalette.ColorOf(i);
                    Color derived = SuitPalette.Derive(chosen, relationship);

                    Assert.AreEqual(chosen.r, derived.r, 0.01f, SuitPalette.NameOf(i));
                    Assert.AreEqual(chosen.g, derived.g, 0.01f, SuitPalette.NameOf(i));
                    Assert.AreEqual(chosen.b, derived.b, 0.01f, SuitPalette.NameOf(i));
                }
            }
        }

        [Test]
        public void EveryDerivedColourIsInGamut()
        {
            for (int i = 0; i < SuitPalette.Count; i++)
            {
                foreach (SuitPalette.Relationship relationship in SuitPalette.Relationships)
                {
                    Color c = SuitPalette.Derive(SuitPalette.ColorOf(i), relationship);
                    string where = $"{SuitPalette.NameOf(i)} / {relationship.MaterialName}";

                    Assert.IsTrue(c.r is >= 0f and <= 1f, $"red out of range for {where}");
                    Assert.IsTrue(c.g is >= 0f and <= 1f, $"green out of range for {where}");
                    Assert.IsTrue(c.b is >= 0f and <= 1f, $"blue out of range for {where}");
                }
            }
        }

        [Test]
        public void TheSwatchesAreTellableApart()
        {
            // Two swatches a player cannot distinguish are two presses that appear to do nothing.
            for (int a = 0; a < SuitPalette.Count; a++)
            {
                for (int b = a + 1; b < SuitPalette.Count; b++)
                {
                    Color x = SuitPalette.ColorOf(a);
                    Color y = SuitPalette.ColorOf(b);

                    float distance = Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b);

                    Assert.Greater(distance, 0.25f,
                        $"'{SuitPalette.NameOf(a)}' and '{SuitPalette.NameOf(b)}' are too close to " +
                        "tell apart on a figure across the lobby.");
                }
            }
        }

        [Test]
        public void SteppingWrapsAtBothEnds()
        {
            // Neither arrow is ever a dead end — a cycler that stops responding at one end reads as
            // a broken button rather than as the end of a list.
            Assert.AreEqual(SuitPalette.Count - 1, SuitPalette.Step(0, -1));
            Assert.AreEqual(0, SuitPalette.Step(SuitPalette.Count - 1, 1));
            Assert.AreEqual(1, SuitPalette.Step(0, 1));
        }

        [Test]
        public void OutOfRangeIndicesAreClampedRatherThanThrowing()
        {
            // These arrive from a peer's NetworkVariable and from PlayerPrefs written by an older
            // build. Throwing here would take the roster down on every poll.
            Assert.AreEqual(0, SuitPalette.Clamp(-99));
            Assert.AreEqual(SuitPalette.Count - 1, SuitPalette.Clamp(9999));
            Assert.DoesNotThrow(() => SuitPalette.ColorOf(-1));
            Assert.DoesNotThrow(() => SuitPalette.NameOf(int.MaxValue));
        }

        [Test]
        public void TheRandomDefaultIsNeverANearNeutral()
        {
            // Bone and Graphite are legitimate choices and a terrible first impression: an astronaut
            // that arrives almost uncoloured reads as the feature having failed.
            for (int i = 0; i < 200; i++)
            {
                int picked = SuitPalette.RandomDefault();

                Assert.Less(picked, SuitPalette.VividCount,
                    $"'{SuitPalette.NameOf(picked)}' was handed out as a default.");
                Assert.GreaterOrEqual(picked, 0);
            }
        }

        // ──────────────────────────────────────────────────────────────────── applying it

        [Test]
        public void ApplyPaintsEverySlotTheTableNames()
        {
            GameObject figure = InstantiateModel();

            try
            {
                var recolor = figure.AddComponent<SuitRecolor>();

                // Counted off the model rather than hard-coded: the point is that the component and
                // the model agree, not that either matches a number written down here.
                int expected = CountMatchingSlots(figure);
                Assert.Greater(expected, 0, "The astronaut has no recolourable slots at all.");
                Assert.AreEqual(expected, recolor.TargetCount);

                recolor.Apply(6);
                Assert.AreEqual(6, recolor.Current);
            }
            finally { Object.DestroyImmediate(figure); }
        }

        [Test]
        public void ApplyLeavesTheNeutralSlotsWithoutAnOverride()
        {
            GameObject figure = InstantiateModel();

            try
            {
                var recolor = figure.AddComponent<SuitRecolor>();
                recolor.Apply(2);

                var block = new MaterialPropertyBlock();

                foreach (Renderer renderer in figure.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;

                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        if (slots[slot] == null || IsRecoloured(slots[slot].name)) continue;

                        renderer.GetPropertyBlock(block, slot);

                        Assert.IsTrue(block.isEmpty,
                            $"'{slots[slot].name}' is one of the astronaut's neutrals and came back " +
                            "with a property override on it.");
                    }
                }
            }
            finally { Object.DestroyImmediate(figure); }
        }

        [Test]
        public void ApplyClampsRatherThanThrowingOnAnUnknownSwatch()
        {
            GameObject figure = InstantiateModel();

            try
            {
                var recolor = figure.AddComponent<SuitRecolor>();

                Assert.DoesNotThrow(() => recolor.Apply(int.MaxValue));
                Assert.AreEqual(SuitPalette.Count - 1, recolor.Current);
            }
            finally { Object.DestroyImmediate(figure); }
        }

        // ─────────────────────────────────────────────────────────────────── lobby transport

        [Test]
        public void PlayerDataCarriesTheSuitColour()
        {
            Player player = LobbySession.BuildPlayer("Ferdinand", 7);

            Assert.IsTrue(player.Data.ContainsKey(LobbySession.KeySuitColor));
            Assert.AreEqual("7", player.Data[LobbySession.KeySuitColor].Value);
        }

        [Test]
        public void PlayerDataClampsAColourItWasHandedOutOfRange()
        {
            Player player = LobbySession.BuildPlayer("Ferdinand", 9999);

            Assert.AreEqual((SuitPalette.Count - 1).ToString(),
                            player.Data[LobbySession.KeySuitColor].Value);
        }

        [Test]
        public void SuitColorUpdateSendsOnlyTheColour()
        {
            // Including the name would make every arrow press rewrite it, so a rename typed
            // elsewhere mid-lobby could be reverted by a colour change.
            var options = LobbySession.BuildSuitColorOptions(3);

            Assert.AreEqual(1, options.Data.Count);
            Assert.IsTrue(options.Data.ContainsKey(LobbySession.KeySuitColor));
        }

        [Test]
        public void SuitColorsSurvivesEveryShapeOfBadData()
        {
            // All four of these are real: a player mid-join has no data, a player from an older
            // build has no key, a hand-edited value is not a number, and a peer on a newer build
            // sends an index this build has never heard of.
            var lobby = LobbyWith(
                PlayerWith(null),
                PlayerWith(new Dictionary<string, PlayerDataObject>()),
                PlayerWith(Data("SuitColor", "not-a-number")),
                PlayerWith(Data("SuitColor", "9999")));

            int[] colors = LobbySession.SuitColors(lobby);

            Assert.AreEqual(4, colors.Length);
            Assert.AreEqual(0, colors[0]);
            Assert.AreEqual(0, colors[1]);
            Assert.AreEqual(0, colors[2]);
            Assert.AreEqual(SuitPalette.Count - 1, colors[3]);
        }

        [Test]
        public void SuitColorsIsIndexAlignedWithPlayerNames()
        {
            // The rank reads the two arrays by the same index. If they ever disagreed on length or
            // order, players would wear each other's colours.
            var lobby = LobbyWith(
                PlayerWith(Both("Ferdinand", "4")),
                PlayerWith(Both("Tobb", "6")));

            string[] names = LobbySession.PlayerNames(lobby);
            int[] colors = LobbySession.SuitColors(lobby);

            Assert.AreEqual(names.Length, colors.Length);
            Assert.AreEqual("Ferdinand", names[0]);
            Assert.AreEqual(4, colors[0]);
            Assert.AreEqual("Tobb", names[1]);
            Assert.AreEqual(6, colors[1]);
        }

        [Test]
        public void SuitColorsHandlesNoLobbyAtAll()
        {
            Assert.AreEqual(0, LobbySession.SuitColors(null).Length);
        }

        [Test]
        public void SlotLookupsFindThePlayerTheyWereAskedFor()
        {
            // Keyed on the authentication id rather than on the name, because two friends both
            // called Pilot would otherwise share a slot — and the cycler would appear under the
            // wrong astronaut.
            Lobby lobby = LobbyHostedBy("host-id",
                PlayerWith("host-id", Both("Ferdinand", "0")),
                PlayerWith("guest-id", Both("Tobb", "6")));

            Assert.AreEqual(0, LobbySession.SlotOf(lobby, "host-id"));
            Assert.AreEqual(1, LobbySession.SlotOf(lobby, "guest-id"));
            Assert.AreEqual(0, LobbySession.HostSlot(lobby));
        }

        [Test]
        public void SlotLookupsAnswerMinusOneRatherThanGuessing()
        {
            // -1 is what makes the cycler hide instead of appearing under somebody else's figure.
            Lobby lobby = LobbyHostedBy(null, PlayerWith("host-id", Both("Ferdinand", "0")));

            Assert.AreEqual(-1, LobbySession.SlotOf(lobby, "nobody"));
            Assert.AreEqual(-1, LobbySession.SlotOf(lobby, null));
            Assert.AreEqual(-1, LobbySession.SlotOf(null, "someone"));
            Assert.AreEqual(-1, LobbySession.HostSlot(null));

            // A lobby whose host has left is a real state the poll can return, and it must not
            // resolve to slot 0 and hand somebody else the underline.
            Assert.AreEqual(-1, LobbySession.HostSlot(lobby));
        }

        // ──────────────────────────────────────────────────────────────────────── helpers

        private static HashSet<string> MaterialNamesOnModel()
        {
            var names = new HashSet<string>();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (asset is Material material)
                    names.Add(material.name);

            Assert.Greater(names.Count, 0, $"No materials found on {ModelPath}.");
            return names;
        }

        private static GameObject InstantiateModel()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, $"No astronaut model at {ModelPath}.");

            return Object.Instantiate(model);
        }

        private static bool IsRecoloured(string materialName)
        {
            foreach (SuitPalette.Relationship relationship in SuitPalette.Relationships)
                if (relationship.MaterialName == materialName)
                    return true;

            return false;
        }

        private static int CountMatchingSlots(GameObject figure)
        {
            int count = 0;

            foreach (Renderer renderer in figure.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;

                for (int slot = 0; slot < slots.Length; slot++)
                    if (slots[slot] != null && IsRecoloured(slots[slot].name))
                        count++;
            }

            return count;
        }

        private static Dictionary<string, PlayerDataObject> Data(string key, string value) =>
            new() { { key, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, value) } };

        private static Dictionary<string, PlayerDataObject> Both(string name, string color)
        {
            var data = Data(LobbySession.KeyPlayerName, name);
            data[LobbySession.KeySuitColor] =
                new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, color);
            return data;
        }

        private static Player PlayerWith(Dictionary<string, PlayerDataObject> data) =>
            new(id: "player", data: data);

        private static Player PlayerWith(string id, Dictionary<string, PlayerDataObject> data) =>
            new(id: id, data: data);

        private static Lobby LobbyWith(params Player[] players) =>
            new(id: "lobby", players: new List<Player>(players));

        /// <summary>
        /// Named apart from <see cref="LobbyWith"/> rather than overloading it: a call passing a null
        /// host is ambiguous between the two, and "a lobby whose host has left" is exactly the case
        /// worth testing.
        /// </summary>
        private static Lobby LobbyHostedBy(string hostId, params Player[] players) =>
            new(id: "lobby", hostId: hostId, players: new List<Player>(players));
    }
}
