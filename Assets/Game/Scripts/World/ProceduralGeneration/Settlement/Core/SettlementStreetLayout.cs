// A planned settlement, start to finish: grow the streets and line them with buildings until every
// building has a frontage, cut the ground into terraces that follow the streets, sculpt the terrain to
// them, stand the buildings on their terraces, then lay the stairs, terrace walls, street surfaces and
// door paths. Settlement.Generate calls this instead of its cluster growth when the config has a street
// style; decorations and characters follow the same way for both.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementStreetLayout
    {
        /// <summary>The child of Generated every stair, wall, street piece and door path is laid under.</summary>
        public const string StreetsRootName = "Streets";

        // Streets draw from their own sequence off the settlement's seed.
        private const int StreetSeedSalt = 0x57EE7;
        // Terrace cells this far round a plot are held at the plot's ground height, so its walls clear it.
        private const float PlotPadding = 1f;
        // A door path is only laid when the door faces its street at least this squarely (cosine).
        private const float DoorFacesStreet = 0.7f;
        // Door paths round a plaza stop this far from its centre, where its node lies.
        private const float PlazaHeart = 3f;
        private static readonly Vector2 DefaultFootprint = new Vector2(8f, 8f);

        public static SettlementLayoutResult Generate(SettlementConfig config, List<GameObject> prefabs, Transform root, Vector3 center, int seed)
        {
            SettlementStreetStyle style = config.streets;
            var result = new SettlementLayoutResult();
            var rng = new SettlementPlacementUtil.SeededRng(seed ^ StreetSeedSalt);
            var centerXZ = new Vector2(center.x, center.z);
            Terrain[] terrains = Terrain.activeTerrains;
            Func<Vector2, float> ground = xz => SettlementPlacementUtil.TerrainHeightAt(terrains, xz, center.y);

            var halfWidths = new float[3];
            for (int order = 0; order < halfWidths.Length; order++)
                halfWidths[order] = SettlementStreetPaver.PavedHalfWidth(style, order) + style.verge;

            // 1. Streets and plots, grown together until every building has a frontage.
            var waiting = new List<SettlementStreetPlots.Building>(prefabs.Count);
            foreach (var prefab in prefabs)
                waiting.Add(new SettlementStreetPlots.Building(prefab, SettlementPlacementUtil.MeasureFootprint(prefab, DefaultFootprint)));
            waiting.Sort((a, b) => (b.local.width * b.local.height).CompareTo(a.local.width * a.local.height));

            int buildingCount = waiting.Count;
            float townRadius = SettlementStreetPlots.TownRadius(waiting, style.buildingCoverage);
            // The town centre: a plaza ringed by the largest buildings; its lanes leave through gaps in the ring.
            SettlementPlaza.Layout plaza = buildingCount > 0
                ? SettlementPlaza.Plan(waiting, style, config.minBuildingGap, centerXZ, halfWidths[SettlementStreetNetwork.MainStreet], ref rng)
                : null;
            if (plaza != null) waiting = new List<SettlementStreetPlots.Building>(plaza.rest);
            var everyBuilding = new List<SettlementStreetPlots.Building>(waiting);
            float plazaRadius = plaza?.radius ?? 0f;
            var network = new SettlementStreetNetwork(style, centerXZ, halfWidths, ground, ref rng, plaza?.lanes);
            var plots = new SettlementStreetPlots(style, config.minBuildingGap, config.maxBuildingGap, centerXZ, townRadius, plazaRadius);
            if (plaza != null) plots.AddFixed(plaza.ring);
            while (waiting.Count > 0)
            {
                float needed = 0f;
                foreach (var building in waiting) needed += building.local.width + config.maxBuildingGap;
                network.Grow(network.Frontage + needed * style.frontageSlack, plots.Footprints(), ref rng);
                int before = waiting.Count;
                plots.Place(network, waiting, ref rng);
                if (waiting.Count == before && !network.CanGrow) break;
            }
            // The plaza's ring belongs to the lane nearest each building: its level, and the lane kept to the ring.
            plots.AttachPlazaPlots(network);
            // Growth overshoots: cut every street back to its last house and drop the ones nobody lives on.
            plots.Renumber(network.Trim(SettlementStreetNetwork.KeepLengths(network.streets, plots.LastHouseArcs(network), style.trimMargin)));
            // While growing, the branches every branchSpacing kept houses off the street they leave; most of them are
            // gone now, and the gaps with them. Line what is left again, closer together, and cut it back once more.
            var compact = new SettlementStreetPlots(style, config.minBuildingGap, config.maxBuildingGap, centerXZ, townRadius, plazaRadius);
            compact.AddFixed(plots.plots.FindAll(plot => plot.facesPlaza));
            var again = new List<SettlementStreetPlots.Building>(everyBuilding);
            compact.Place(network, again, ref rng);
            if (again.Count <= waiting.Count)
            {
                plots = compact;
                plots.Renumber(network.Trim(SettlementStreetNetwork.KeepLengths(network.streets, plots.LastHouseArcs(network), style.trimMargin)));
            }

            // Which stretch of which street is road, slabs or stone path; stone paths follow the ground and never step.
            List<SettlementStreetSurfaces.Section>[] sections = style.IsTileMode
                ? SettlementStreetSurfaces.Assign(network.streets, centerXZ, style, buildingCount)
                : null;
            Func<float, bool> TerracedOn(int s) => sections == null ? null : arc => SettlementStreetSurfaces.IsTerraced(sections[s], arc);

            foreach (var street in network.streets)
                foreach (var point in street.points)
                    result.extent = Mathf.Max(result.extent, Vector2.Distance(point, centerXZ) + street.halfWidth);
            foreach (var plot in plots.plots)
                result.extent = Mathf.Max(result.extent, Vector2.Distance(plot.footprint.center, centerXZ) + plot.footprint.Circumradius);
            result.extent += config.outskirts;

            // 2. Terraces: street profiles first (parents before their branches), then the ground under each
            // stair chain, the streets, and every plot at its street's level.
            var field = new SettlementTerraceField(centerXZ, result.extent, style.fieldCellSize, style.stepHeight, style.groundSmoothing, ground);
            var profiles = new List<SettlementTerraceField.StreetProfile>(network.streets.Count);
            Func<int, float> chainLength = style.ChainsStairs ? levels => SettlementStairChain.Length(style, levels) : null;
            for (int s = 0; s < network.streets.Count; s++)
            {
                var street = network.streets[s];
                int? start = street.parent >= 0 ? profiles[street.parent].LevelAt(street.parentArc)
                           : s > 0 ? profiles[0].LevelAt(0f)   // the main street's second half leaves the centre where the first did
                           : null;
                int? end = street.endsOn >= 0 && street.endsOn < s ? profiles[street.endsOn].LevelAt(street.endsOnArc) : null;
                var profile = field.Profile(street, start, end, style.minRunBetweenStairs, style.StairLevels(street.order), chainLength, TerracedOn(s),
                                            startHold: street.parent < 0 ? plazaRadius : 0f);
                if (end.HasValue && profile.levels[profile.levels.Count - 1] != end.Value) result.streetsEndingAtWalls++;
                profiles.Add(profile);
            }
            // A chain's corridor is ramped down the hill under its nosings before its street keeps its level everywhere else.
            var chains = style.ChainsStairs && sections != null
                ? SettlementStairChain.Plan(style, network, profiles, field, prefab => SettlementPlacementUtil.MeasureFootprint(prefab, DefaultFootprint), sections)
                : new List<SettlementStairChain.Placement>();
            // Terrace-kit roads lie at their street's exact level instead of on the ground, so the band held
            // level reaches one heightmap sample past the street; else the terrain rises over their kerbs.
            float groundMargin = style.IsTileMode && terrains.Length > 0 ? terrains[0].terrainData.heightmapScale.x : 0f;
            // The plaza is one level, inside the fronts of the buildings round it.
            if (plaza != null) field.LockDisk(centerXZ, plazaRadius, profiles[0].LevelAt(0f));
            foreach (var chain in chains)
            {
                int levels = chain.levels;
                field.LockRamp(chain.top, chain.forward, chain.halfWidth + groundMargin, chain.length, chain.topY,
                               along => style.stairGroundBelowNosing + SettlementStairChain.DropAt(along, levels, style.flightRun, style.landingRun,
                                                                                                 style.stepHeight, style.flightsPerLanding));
            }
            for (int s = 0; s < network.streets.Count; s++) field.Lock(network.streets[s], profiles[s], groundMargin, TerracedOn(s));
            // A plot's level decides where walls and stairs go; the building itself stands on the highest
            // natural ground under it, so it sits in the hillside instead of on a platform raised above it.
            var plotHeights = new List<float>(plots.plots.Count);
            foreach (var plot in plots.plots)
            {
                int level = profiles[plot.street].LevelAt(plot.arc);
                float height = HighestGroundUnder(plot.footprint, ground, style.fieldCellSize);
                plotHeights.Add(height);
                field.Lock(plot.footprint, PlotPadding, level, height);
            }
            // Behind a retaining wall the ground stays one level down for half the wall's depth, so the
            // heightmap's step is buried in the wall instead of in front of it.
            field.Grade(style.gradeDistance, style.wallReach, SettlementStreetPaver.WallDepth(style) * 0.5f);

            // 3. Ground, levelled under the streets and plots and graded back to natural around them.
            List<SettlementFootprint> plotFootprints = plots.Footprints();
            result.terrainBackup = SettlementTerrainSculptor.ShapeTerraces(center, result.extent, config.blendDistance, field);

            // 4. Buildings, each on its terrace.
            for (int i = 0; i < plots.plots.Count; i++)
            {
                var plot = plots.plots[i];
                var position = new Vector3(plot.pivot.x, plotHeights[i], plot.pivot.y);
                result.buildings.Add(SettlementPlacementUtil.SpawnPrefab(plot.prefab, root, position, plot.rotation).transform);
                result.buildingFootprints.Add(plot.footprint);
            }

            // 5. Stairs, terrace walls, street slabs and door paths.
            Transform streetsRoot = new GameObject(StreetsRootName).transform;
            streetsRoot.SetParent(root, worldPositionStays: false);
            var paver = new SettlementStreetPaver(style, streetsRoot, root);
            if (style.ChainsStairs && sections != null) paver.LayStairChains(chains, plotFootprints);
            else paver.LayStairs(network, profiles, field);
            paver.LayWalls(field, plotFootprints, centerXZ, result.extent, ref rng);
            if (sections != null) paver.LayTerraceStreets(network, profiles, field, chains, sections, ref rng);
            else paver.LayStreets(network, ref rng);
            for (int i = 0; i < plots.plots.Count; i++)
                LayDoorPath(paver, style, network, sections, plots.plots[i], result.buildings[i], centerXZ, ref rng);

            result.paving = paver.Laid;
            result.pavingRoot = streetsRoot;
            result.wallsTooTall = paver.WallsTooTall;
            result.summary = Summary(network, paver, style, plots.BackRows);
            // The streets that leave the centre, on the sculpted ground and paved wherever they are not a stone path: the
            // muster spot is placed where one's paving ends. The street kit paves every street end to end.
            for (int s = 0; s < network.streets.Count; s++)
            {
                var street = network.streets[s];
                if (street.parent >= 0) continue;
                var lane = new SettlementMuster.StreetPoint[street.points.Count];
                for (int i = 0; i < lane.Length; i++)
                {
                    Vector2 xz = street.points[i];
                    lane[i] = new SettlementMuster.StreetPoint(new Vector3(xz.x, ground(xz), xz.y),
                                                              sections == null || SettlementStreetSurfaces.IsTerraced(sections[s], street.arcs[i]));
                }
                result.lanes.Add(lane);
            }
            return result;
        }

        private static string Summary(SettlementStreetNetwork network, SettlementStreetPaver paver, SettlementStreetStyle style, int backRows)
        {
            float streetLength = 0f;
            var byOrder = new int[3];
            foreach (var street in network.streets)
            {
                streetLength += street.Length;
                byOrder[Mathf.Min(street.order, byOrder.Length - 1)]++;
            }
            string summary = $"{network.streets.Count} streets ({byOrder[0]} main, {byOrder[1]} side, {byOrder[2]} alleys) over {streetLength:0} m, " +
                             $"{backRows} back-row houses";

            if (style.ChainsStairs)
            {
                var byLevels = new List<string>();
                int chains = 0;
                for (int levels = 1; levels < paver.ChainsByLevels.Length; levels++)
                {
                    if (paver.ChainsByLevels[levels] == 0) continue;
                    chains += paver.ChainsByLevels[levels];
                    byLevels.Add($"{paver.ChainsByLevels[levels]}x{levels}");
                }
                summary += $", {chains} stair chains ({string.Join(" ", byLevels)} levels) of {paver.StairsLaid} flights and {paver.LandingsLaid} landings";
            }
            else summary += $", {paver.StairsLaid} flights of stairs";

            summary += style.StacksWalls
                ? $", {paver.WallsLaid} terrace walls over {paver.CoursesLaid} courses with {paver.PillarsLaid} pillars"
                : $", {paver.WallsLaid} terrace walls";
            summary += style.IsTileMode
                ? $", {paver.TilesLaid} road tiles, {paver.NodesLaid} nodes, {paver.EndsLaid} road ends, {paver.SlabsLaid} slabs, {paver.StonesLaid} stepping stones"
                : $", {paver.SlabsLaid} slabs";
            return summary;
        }

        private static float HighestGroundUnder(in SettlementFootprint footprint, Func<Vector2, float> ground, float spacing)
        {
            float highest = float.NegativeInfinity;
            foreach (Vector2 point in footprint.GridPoints(spacing))
                highest = Mathf.Max(highest, ground(point));
            return highest;
        }

        private static void LayDoorPath(SettlementStreetPaver paver, SettlementStreetStyle style, SettlementStreetNetwork network,
                                        List<SettlementStreetSurfaces.Section>[] sections, SettlementStreetPlots.Plot plot, Transform building,
                                        Vector2 center, ref SettlementPlacementUtil.SeededRng rng)
        {
            var street = network.streets[plot.street];
            var door = SettlementEntrance.MainDoorway(building, plot.footprint);
            if (plot.facesPlaza)
            {
                // Round a plaza every door path runs to its middle, stopping short so the paths do not pile up there.
                paver.LayDoorStub(door.position, door.outward, Vector2.Distance(door.position, center) - PlazaHeart, ref rng);
                return;
            }
            float arc = street.Project(door.position, out float distance);
            Vector2 towardStreet = (street.PointAt(arc) - door.position).normalized;
            if (Vector2.Dot(door.outward, towardStreet) < DoorFacesStreet) return;

            float pavedHalf = sections == null
                ? SettlementStreetPaver.PavedHalfWidth(style, street.order)
                : SettlementStreetPaver.PavedHalfWidth(style, street.order, SettlementStreetSurfaces.At(sections[plot.street], arc));
            paver.LayDoorStub(door.position, door.outward, distance - pavedHalf, ref rng);
        }
    }
}
