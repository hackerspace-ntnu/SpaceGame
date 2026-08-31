// What a versus start has to keep being true about itself: every team lands on its own point,
// facing the way that point asks, and no two teams fly the same line down.
//
// These are the failures that are invisible in play. A ship that lands facing 90 degrees off still
// lands, and the wreck is then wrong forever because the hull is persisted exactly where the
// trajectory left it. A formation whose arcs are all identical still flies, and only looks like a
// bug when four ships pass through each other. And a livery table that no longer matches the ship's
// materials paints nothing at all, with a clean console, in the one mode where colour is how you
// tell friend from enemy.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class ArrivalFormationTests
    {
        /// <summary>Degrees of float drift treated as the same heading.</summary>
        private const float AngleTolerance = 0.05f;

        /// <summary>Metres of float drift treated as noise rather than as a miss.</summary>
        private const float PositionTolerance = 0.01f;

        /// <summary>
        /// The shipped defaults. Deliberately the real values rather than hand-made ones, so a
        /// retune that breaks an invariant is caught here rather than in play.
        /// </summary>
        private static ArrivalPath Authored() => ArrivalPath.Default;

        /// <summary>The layout a four-team match actually gets, from the shipped ring maths.</summary>
        private static ShipSpawnPoint[] Ring(int teams) =>
            ShipSpawnLayout.Ring(new Vector2(2500f, 500f), 120f, teams);

        // ───────────────────────────────────────────────────────── landing where it was told to

        [Test]
        public void EveryTeamLandsOnItsOwnPoint()
        {
            ShipSpawnPoint[] points = Ring(4);

            for (int team = 0; team < points.Length; team++)
            {
                Vector3 impact = points[team].At(37f);

                ArrivalPath path = ArrivalFormation.PathFor(Authored(), team, points.Length, impact,
                                                            points[team].Yaw, 0.3f);

                ArrivalTrajectory.Evaluate(1f, path, out Vector3 landed, out _);

                Assert.AreEqual(impact.x, landed.x, PositionTolerance,
                    $"{VersusRules.TeamName(team)} did not land on its own spawn point.");
                Assert.AreEqual(impact.y, landed.y, PositionTolerance);
                Assert.AreEqual(impact.z, landed.z, PositionTolerance);
            }
        }

        [Test]
        public void EveryTeamLandsFacingTheWayItsPointAsks()
        {
            // The heading is not steered — ArrivalTrajectory points the hull along the way it is
            // travelling — so the ONLY way to control where a ship ends up facing is the bearing it
            // starts from. If this drifts, every wreck in the arena sits at an arbitrary angle and
            // stays that way, because the hull is persisted where the descent left it.
            ShipSpawnPoint[] points = Ring(4);

            for (int team = 0; team < points.Length; team++)
            {
                ArrivalPath path = ArrivalFormation.PathFor(Authored(), team, points.Length,
                                                            points[team].At(0f), points[team].Yaw, 0.3f);

                ArrivalTrajectory.Evaluate(1f, path, out _, out Quaternion landed);

                Assert.AreEqual(0f, Mathf.DeltaAngle(points[team].Yaw, landed.eulerAngles.y),
                    AngleTolerance,
                    $"{VersusRules.TeamName(team)} landed facing {landed.eulerAngles.y}, not the " +
                    $"{points[team].Yaw} its spawn point asked for.");
            }
        }

        [Test]
        public void TheLandingHeadingHoldsWhicheverWayTheShipSpiralsIn()
        {
            // Odd teams mirror the sweep, which flips the sign inside the derivation. The bearing
            // maths has to survive that or half the formation lands backwards — and "half" is the
            // shape of bug that looks like a one-off when somebody sees it once.
            foreach (float sweep in new[] { 110f, -110f, 0f, 359f })
            {
                ArrivalPath path = Authored();
                path.SweepDegrees = sweep;
                path.StartBearing = ArrivalFormation.BearingForLandingYaw(217f, sweep);

                ArrivalTrajectory.Evaluate(1f, path, out _, out Quaternion landed);

                Assert.AreEqual(0f, Mathf.DeltaAngle(217f, landed.eulerAngles.y), AngleTolerance,
                    $"A descent sweeping {sweep} degrees did not arrive on its wanted heading.");
            }
        }

        // ──────────────────────────────────────────────────────────── no two ships fly one line

        [Test]
        public void NoTwoTeamsStartFromTheSamePlace()
        {
            ShipSpawnPoint[] points = Ring(4);
            var starts = new List<Vector3>();

            for (int team = 0; team < points.Length; team++)
            {
                ArrivalPath path = ArrivalFormation.PathFor(Authored(), team, points.Length,
                                                            points[team].At(0f), points[team].Yaw, 0.3f);

                ArrivalTrajectory.Evaluate(0f, path, out Vector3 start, out _);

                foreach (Vector3 other in starts)
                    Assert.Greater(Vector3.Distance(start, other), 100f,
                        "Two teams begin their descent within 100 m of each other, so their ships " +
                        "fly down through one another.");

                starts.Add(start);
            }
        }

        [Test]
        public void NeighbouringTeamsSpiralOppositeWays()
        {
            ArrivalPath even = ArrivalFormation.PathFor(Authored(), 0, 4, Vector3.zero, 0f, 0.3f);
            ArrivalPath odd = ArrivalFormation.PathFor(Authored(), 1, 4, Vector3.zero, 0f, 0.3f);

            Assert.AreEqual(Authored().SweepDegrees, even.SweepDegrees, 0.001f,
                "Team zero must fly exactly the authored arc, or retuning that arc stops being " +
                "visible in the game.");
            Assert.AreEqual(-Authored().SweepDegrees, odd.SweepDegrees, 0.001f);
        }

        [Test]
        public void TheBankFollowsTheSweep()
        {
            // A hull that mirrored its turn but not its roll banks OUT of the corner it is flying,
            // which reads as a ship in trouble rather than a ship arriving.
            ArrivalPath odd = ArrivalFormation.PathFor(Authored(), 1, 4, Vector3.zero, 0f, 0.3f);

            Assert.AreEqual(Mathf.Sign(odd.SweepDegrees), Mathf.Sign(odd.MaxBankDegrees),
                "The bank and the sweep disagree about which way the ship is turning.");
        }

        [Test]
        public void EveryTeamKeepsAFlyableArc()
        {
            // A lateral budget at or below zero is the degenerate descent ArrivalDirector refuses:
            // a spiral with no radius has no heading. The stagger must never be able to reach it.
            for (int teams = 1; teams <= VersusRules.MaxTeams; teams++)
                for (int team = 0; team < teams; team++)
                {
                    ArrivalPath path = ArrivalFormation.PathFor(Authored(), team, teams, Vector3.zero,
                                                                0f, ArrivalFormation.MaxSpread);

                    Assert.Greater(path.LateralBudget, 0f,
                        $"Team {team} of {teams} was staggered onto a zero-radius descent.");
                    Assert.Greater(path.StartAltitude, 0f,
                        $"Team {team} of {teams} was staggered down to the ground.");
                }
        }

        [Test]
        public void TheAuthoredBudgetsAreCeilingsNoTeamExceeds()
        {
            // The lateral budget is a WORLD-STREAMING limit and the start altitude is the top of
            // the band the sky reads correctly in. A stagger that spread symmetrically would push
            // half the formation past both — a frame-rate problem on somebody else's machine, and a
            // sky that goes wrong at the top of the arc. Neither shows up as an error.
            ArrivalPath authored = Authored();

            for (int teams = 1; teams <= VersusRules.MaxTeams; teams++)
                for (int team = 0; team < teams; team++)
                {
                    ArrivalPath path = ArrivalFormation.PathFor(authored, team, teams, Vector3.zero,
                                                                0f, ArrivalFormation.MaxSpread);

                    Assert.LessOrEqual(path.LateralBudget, authored.LateralBudget + 0.001f,
                        $"Team {team} of {teams} starts further out than the streaming budget allows.");
                    Assert.LessOrEqual(path.StartAltitude, authored.StartAltitude + 0.001f,
                        $"Team {team} of {teams} starts above the authored ceiling.");
                }
        }

        [Test]
        public void ALoneTeamFliesTheAuthoredArcUntouched()
        {
            // A formation of one is a story world in all but name, and must not be handed one
            // arbitrary end of a range that has no other end.
            ArrivalPath only = ArrivalFormation.PathFor(Authored(), 0, 1, Vector3.zero, 180f, 0.3f);

            Assert.AreEqual(Authored().LateralBudget, only.LateralBudget, 0.001f);
            Assert.AreEqual(Authored().StartAltitude, only.StartAltitude, 0.001f);
            Assert.AreEqual(Authored().SweepDegrees, only.SweepDegrees, 0.001f);
            Assert.AreEqual(0f, ArrivalFormation.Fraction(0, 1), 0.001f);
        }

        [Test]
        public void TheFieldSpansEveryTeamAndNoFurther()
        {
            Assert.AreEqual(0f, ArrivalFormation.Fraction(0, 4), 0.001f);
            Assert.AreEqual(1f, ArrivalFormation.Fraction(3, 4), 0.001f);

            // A team index from a peer with different rules must be folded, not indexed off the end
            // of the field — the same courtesy every other value arriving over the wire gets.
            Assert.AreEqual(1f, ArrivalFormation.Fraction(99, 4), 0.001f);
            Assert.AreEqual(0f, ArrivalFormation.Fraction(-5, 4), 0.001f);
        }

        [Test]
        public void NoTwoTeamsShareAnArc()
        {
            // Two ships on the same lateral budget AND the same altitude fly the same shape, and
            // then only their landing points keep them apart — which at the top of the descent,
            // where the points are furthest from the hulls, is not far enough.
            var seen = new List<Vector2>();

            for (int team = 0; team < 4; team++)
            {
                ArrivalPath path = ArrivalFormation.PathFor(Authored(), team, 4, Vector3.zero, 0f, 0.3f);
                var shape = new Vector2(path.LateralBudget, path.StartAltitude);

                foreach (Vector2 other in seen)
                    Assert.Greater(Vector2.Distance(shape, other), 1f,
                        "Two teams were given the same arc, so their descents are indistinguishable.");

                seen.Add(shape);
            }
        }
    }

    /// <summary>
    /// The ship livery. The load-bearing test is
    /// <see cref="EveryPaintedMaterialStillExistsOnTheShip"/>, for the reason
    /// <c>SuitCustomizationTests</c> spells out: the table matches materials by NAME, so a rebuild
    /// that renames one does not break the build, throw, or log anything at import time — every
    /// team's ship just quietly comes out the same colour.
    /// </summary>
    public class ShipAccentTests
    {
        /// <summary>The ship every team arrives in, and the one the arrival flies.</summary>
        private const string ShipPrefabPath =
            "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";

        private static GameObject Ship()
        {
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            Assert.IsNotNull(ship, $"No ship prefab at {ShipPrefabPath}.");
            return ship;
        }

        private static HashSet<string> PaintNamesOnShip()
        {
            var names = new HashSet<string>();

            foreach (Renderer renderer in Ship().GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null)
                        names.Add(ShipAccentPalette.BaseName(material.name));

            return names;
        }

        [Test]
        public void EveryPaintedMaterialStillExistsOnTheShip()
        {
            HashSet<string> onShip = PaintNamesOnShip();

            foreach (SuitPalette.Relationship relationship in ShipAccentPalette.Relationships)
                Assert.IsTrue(onShip.Contains(relationship.MaterialName),
                    $"ShipAccentPalette expects '{relationship.MaterialName}' on the ship, and it is " +
                    "not there — that part of the hull no longer takes its team's colour. Names " +
                    $"actually present: {string.Join(", ", onShip)}");
        }

        [Test]
        public void TheHullGreysAndGlassAreLeftAlone()
        {
            // These are most of the ship, and they are what stops a team hull reading as a solid
            // block of colour at any distance. The canopy in particular is glass, not paint.
            string[] untouched =
            {
                "Mat_Lander_Hull_Primary",
                "Mat_Lander_Canopy",
                "Mat_Lander_Deck",
                "Mat_Lander_Wall_Interior",
                "Mat_Lander_Mech_Dark",
                "Mat_Lander_Equipment",
            };

            foreach (string name in untouched)
                foreach (SuitPalette.Relationship relationship in ShipAccentPalette.Relationships)
                    Assert.AreNotEqual(name, relationship.MaterialName,
                        $"'{name}' is part of the ship's neutral bodywork and must not be recoloured.");
        }

        [Test]
        public void TheShipCarriesTheComponentsThatPaintIt()
        {
            // The livery is two components on the prefab, and neither of them fails loudly when
            // absent: with no ShipTeamAccent nothing ever publishes a swatch, and every team's ship
            // sits there in the authored orange looking perfectly fine.
            GameObject ship = Ship();

            Assert.IsNotNull(ship.GetComponent<ShipAccentRecolor>(),
                "PlayerShip has no ShipAccentRecolor, so it has nothing to paint it.");
            Assert.IsNotNull(ship.GetComponent<ShipTeamAccent>(),
                "PlayerShip has no ShipTeamAccent, so no team colour ever reaches the clients.");
        }

        [Test]
        public void TheDoubleSidedSuffixIsStrippedBeforeMatching()
        {
            // Every material on a lander carries it — DoubleSidedMaterials is run over the whole
            // model by the ship builder — so a table matched against raw names matches nothing and
            // paints nothing, silently. This is the single line that stands between the feature and
            // doing exactly that.
            Assert.AreEqual("Mat_Paint_Safety_Orange",
                ShipAccentPalette.BaseName("Mat_Paint_Safety_Orange (DoubleSided)"));
            Assert.AreEqual("Mat_Paint_Safety_Orange",
                ShipAccentPalette.BaseName("Mat_Paint_Safety_Orange (DoubleSided) (Instance)"));
            Assert.AreEqual("Mat_Paint_Safety_Orange",
                ShipAccentPalette.BaseName("Mat_Paint_Safety_Orange"));
        }

        [Test]
        public void TheLiveryIsWiredForSaving()
        {
            // A versus ship is made mid-match, so its livery lives only in a NetworkVariable until
            // something writes it to the record. Without the saver a saved match reloads with every
            // hull back in its authored paint and nothing logged — and the ship itself still
            // persists its pose and its parts, which is what makes the omission invisible.
            var ship = new GameObject("LiveryPolicyProbe");

            try
            {
                ship.AddComponent<ShipTeamAccent>();
                SpaceGame.Core.Persistence.SaveablePolicy.Ensure(ship, out _);

                Assert.IsNotNull(ship.GetComponent<SpaceGame.Core.Persistence.ShipAccentSaveable>(),
                    "A hull with a ShipTeamAccent was wired for saving without its livery, so the " +
                    "team colour is the one thing about it that will not come back.");
            }
            finally
            {
                Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void AnUnclaimedHullKeepsItsAuthoredPaint()
        {
            Assert.IsFalse(ShipAccentPalette.TryDerive(ShipAccentPalette.NoTeam,
                                                       "Mat_Paint_Safety_Orange", out _),
                "A ship on no team must keep the paint it was authored in, not be repainted in the " +
                "first swatch the moment it spawns.");
        }

        [Test]
        public void TheLiveryTakesTheTeamColourItself()
        {
            // The reference material wears the chosen swatch exactly. If it ever drifts, a team's
            // ship and that team's suits stop being the same colour — which is the entire point.
            Assert.IsTrue(ShipAccentPalette.TryDerive(4, "Mat_Paint_Safety_Orange", out Color painted));

            Color chosen = SuitPalette.ColorOf(4);

            Assert.AreEqual(chosen.r, painted.r, 0.001f);
            Assert.AreEqual(chosen.g, painted.g, 0.001f);
            Assert.AreEqual(chosen.b, painted.b, 0.001f);
        }

        [Test]
        public void EveryTeamInAFullMatchGetsATellableColour()
        {
            // The default team colours are spread across the palette, and the ship livery inherits
            // that spread. Two teams in the same orange is not a cosmetic annoyance in a mode where
            // the only thing telling you who to shoot is the colour of a hull.
            int[] colors = TeamColorRules.DefaultColors(VersusRules.MaxTeams, SuitPalette.Count);
            var seen = new List<Color>();

            foreach (int swatch in colors)
            {
                Assert.IsTrue(ShipAccentPalette.TryDerive(swatch, "Mat_Lander_Nacelle",
                                                          out Color nacelle));

                foreach (Color other in seen)
                {
                    float distance = Mathf.Abs(nacelle.r - other.r) + Mathf.Abs(nacelle.g - other.g) +
                                     Mathf.Abs(nacelle.b - other.b);

                    Assert.Greater(distance, 0.15f,
                        "Two teams' ships come out the same colour, so they cannot be told apart.");
                }

                seen.Add(nacelle);
            }
        }
    }
}
