using System;
using UnityEngine;

/// <summary>
/// A 3D voxel <see cref="ITerrainDensity"/> (<see cref="IsHeightfield"/> == false) that builds an
/// eroded-desert-rock formation — a Utah-canyon mesa or escarpment — as a TRUE 3D MASS whose
/// horizontal cross-section varies with height.
///
/// <para><b>Why this replaces <c>OverhangSdf</c>.</b> The old model started from a single-valued
/// heightfield <c>p.y - f(x,z)</c> and unioned protruding shelf slabs onto its flat walls, so
/// overhangs always read as attributes bolted onto a vertical wall. This model has no heightfield
/// underneath it. The solid is defined directly as:</para>
///
/// <code>solid(p) = horizontalDistanceFromAxis(p) - bodyRadius(heightFraction, angle)</code>
///
/// <para>Because <see cref="RockBodyProfile.RadiusMultiplier"/> makes the radius a function of
/// HEIGHT, the body can be narrow at the base and bulge wide overhead, pinch to a waist and flare
/// again, and lean. An overhang is simply any height band that is wider than the band beneath it —
/// it is a DIRECT CONSEQUENCE of the radius profile, never placed. The radius profile is
/// noise-driven, so the bulges, pinches and undercuts EMERGE from the generation.</para>
///
/// <para><b>One SDF, two shapes — the centreline abstraction.</b> The body is organised around a
/// vertical AXIS that may itself be a line. For a MESA the axis is a single point (a degenerate
/// line) and <c>horizontalDistanceFromAxis</c> is the radial distance — a tower. For a CLIFF the
/// axis is the cliff-edge line; <c>horizontalDistanceFromAxis</c> is the perpendicular distance to
/// that line, so the formation becomes a WALL whose face profile (how far rock extends out from the
/// edge) still varies with height and along the line. Both share all the radius/erosion/lean code.</para>
///
/// <para>Negative = solid rock, positive = air, zero = surface. Deterministic off the seed.</para>
/// </summary>
public sealed class RockBodySdf : ITerrainDensity
{
    /// <summary>Mesa = radial tower around a point axis. Cliff = wall along a line axis.</summary>
    public enum BodyShape { RadialTower, LinearWall }

    readonly OverhangSettings _settings;
    readonly Bounds _bounds;
    readonly int _seed;
    readonly BodyShape _shape;

    // Vertical extent of the body.
    readonly float _groundY;        // local Y the body sits on
    readonly float _summitY;        // local Y of the flat top
    readonly float _totalHeight;    // _summitY - _groundY

    // Nominal half-extent of the formation: radial reach (mesa) or wall half-thickness (cliff).
    readonly float _nominalRadius;

    // Radial-tower axis (mesa): a single XZ point.
    readonly Vector2 _axisXZ;

    // Optional footprint shape for the radial tower: signed metres INSIDE the designer-drawn
    // footprint polygon at a local XZ (positive inside, negative outside). When non-null the body's
    // base cross-section follows this polygon instead of a circle of _nominalRadius, so footprint
    // Width / Breadth / outline edits actually shape the overhang mesa. Null = legacy round tower.
    Func<float, float, float> _footprintDistInside;

    // Linear-wall axis (cliff): centre-line in XZ plus its perpendicular. lateralSign picks which
    // side of the line the rock body grows on (the high side of the escarpment).
    readonly Vector2 _lineA, _lineB;
    readonly float _lineLen;

    readonly Func<float, float, float> _groundFn;   // never carve below this

    // Optional macro relief on the summit cap: (x, z) -> signed metres added to the flat
    // summit Y. Null = a perfectly flat top (legacy). Lets the mesa plateau undulate with
    // the same profile the heightfield path uses, so overhang toggling keeps the top consistent.
    Func<float, float, float> _summitReliefFn;

    public Bounds Bounds => _bounds;
    public bool IsHeightfield => false;

    /// <summary>
    /// Builds a RADIAL rock body — a mesa / butte / hoodoo tower around a vertical point axis.
    /// </summary>
    /// <param name="axisXZ">Local-space XZ position of the vertical axis (footprint centre).</param>
    /// <param name="nominalRadius">Nominal radial reach of the formation in metres.</param>
    /// <param name="groundY">Local Y the body sits on.</param>
    /// <param name="summitY">Local Y of the flat top.</param>
    /// <param name="groundFn">Underlying ground height — the body never carves below it.</param>
    /// <param name="volumeBounds">Local-space volume the mesher walks (must contain the widest bulge).</param>
    /// <param name="settings">Rock-body knobs. Never null — caller passes a default instance.</param>
    /// <param name="seed">Deterministic seed.</param>
    public RockBodySdf(
        Vector2 axisXZ, float nominalRadius, float groundY, float summitY,
        Func<float, float, float> groundFn, Bounds volumeBounds, OverhangSettings settings, int seed)
    {
        _shape = BodyShape.RadialTower;
        _settings = settings ?? new OverhangSettings();
        _bounds = volumeBounds;
        _seed = seed;
        _axisXZ = axisXZ;
        _nominalRadius = Mathf.Max(0.5f, nominalRadius);
        _groundY = groundY;
        _summitY = Mathf.Max(summitY, groundY + 1f);
        _totalHeight = _summitY - _groundY;
        _groundFn = groundFn ?? ((x, z) => groundY);
    }

    /// <summary>
    /// Optional macro elevation relief applied to the summit cap: a delegate mapping local XZ to a
    /// signed metre offset added to the flat summit Y. Pass null (or never call) for a flat top.
    /// Keep the offset's magnitude well under <see cref="_totalHeight"/>; the caller must also size
    /// the voxel volume tall enough to contain the highest point the relief produces.
    /// </summary>
    public void SetSummitRelief(Func<float, float, float> summitReliefFn)
        => _summitReliefFn = summitReliefFn;

    /// <summary>
    /// Makes the RADIAL tower follow a designer-drawn footprint polygon instead of a circle. The
    /// delegate maps a local XZ point to signed metres INSIDE the footprint outline (positive
    /// inside, negative outside) — exactly <see cref="FeatureContext.FootprintDistanceInside"/>.
    ///
    /// <para>With this set, the body's base cross-section IS the polygon: a non-square Width vs
    /// Breadth, a hand-edited outline and the mesa's outline-warp all reach the overhang mesh. The
    /// height-varying <see cref="RockBodyProfile.RadiusMultiplier"/> still bulges and pinches that
    /// shape, so overhangs emerge the same way — they just emerge from the real footprint.</para>
    ///
    /// <para>No-op for the linear-wall (cliff) shape, which already follows its edge line.</para>
    /// </summary>
    public void SetFootprint(Func<float, float, float> footprintDistInside)
    {
        if (_shape == BodyShape.RadialTower)
            _footprintDistInside = footprintDistInside;
    }

    /// <summary>
    /// Builds a LINEAR rock body — an escarpment wall along a centre-line. The wall's face profile
    /// (perpendicular reach) varies with height and along the line, so the face bulges and undercuts.
    /// </summary>
    /// <param name="lineA">Start of the cliff-edge line in local XZ.</param>
    /// <param name="lineB">End of the cliff-edge line in local XZ.</param>
    /// <param name="halfThickness">Nominal perpendicular reach of the wall body in metres.</param>
    public RockBodySdf(
        Vector2 lineA, Vector2 lineB, float halfThickness, float groundY, float summitY,
        Func<float, float, float> groundFn, Bounds volumeBounds, OverhangSettings settings, int seed)
    {
        _shape = BodyShape.LinearWall;
        _settings = settings ?? new OverhangSettings();
        _bounds = volumeBounds;
        _seed = seed;
        _lineA = lineA;
        _lineB = lineB;
        Vector2 d = lineB - lineA;
        _lineLen = Mathf.Max(0.01f, d.magnitude);
        _nominalRadius = Mathf.Max(0.5f, halfThickness);
        _groundY = groundY;
        _summitY = Mathf.Max(summitY, groundY + 1f);
        _totalHeight = _summitY - _groundY;
        _groundFn = groundFn ?? ((x, z) => groundY);
    }

    /// <summary>Signed density: negative inside the rock, positive in air, zero at the surface.</summary>
    public float Sample(Vector3 p)
    {
        // --- Height fraction up the body (0 = ground, 1 = summit) ---------------------------
        float t = (p.y - _groundY) / _totalHeight;

        // --- Domain-warp the sample point so the whole body reads as eroded sandstone -------
        // A low-frequency 3D warp displaces where we evaluate the body, giving carved, wavy
        // strata rather than a geometric solid. Cheap: warp vector reused for the whole sample.
        Vector3 q = p;
        if (_settings.erosion > 0f)
        {
            float wf = _settings.erosionFrequency;
            Vector3 warp = new Vector3(
                NoiseDistortion.Sample(p, wf, _seed + 411),
                NoiseDistortion.Sample(p, wf, _seed + 822) * 0.6f,   // less vertical drift
                NoiseDistortion.Sample(p, wf, _seed + 233));
            q = p + warp * _settings.erosion;
        }
        float warpedT = (q.y - _groundY) / _totalHeight;

        // --- Lean: the axis drifts in XZ as the body rises ----------------------------------
        Vector2 leanOffset = Vector2.zero;
        if (_settings.lean > 0f)
        {
            // Low-frequency noise over height -> a smooth, continuous drift, not a kink.
            float lx = (Mathf.PerlinNoise(t * 1.3f + _seed * 0.013f, 4.7f) * 2f - 1f);
            float lz = (Mathf.PerlinNoise(8.9f, t * 1.3f + _seed * 0.017f) * 2f - 1f);
            // Drift grows with height (base stays put) and scales with the nominal radius.
            float leanMag = _settings.lean * _nominalRadius * 0.9f * Mathf.Clamp01(t);
            leanOffset = new Vector2(lx, lz) * leanMag;
        }

        // --- Horizontal distance from the (leaned) axis + angle around it -------------------
        float horiz, angle;
        HorizontalCoords(q, leanOffset, out horiz, out angle);

        // --- Body radius at this height + angle (the heart of the model) --------------------
        float clampedT = Mathf.Clamp01(warpedT);
        float radiusMul = RockBodyProfile.RadiusMultiplier(clampedT, angle, _settings, _seed);
        float radius = _nominalRadius * radiusMul;

        // The core solid: negative inside the body, positive in air.
        float solid;
        if (_shape == BodyShape.RadialTower && _footprintDistInside != null)
        {
            // FOOTPRINT-AWARE base cross-section. The body's shape at the base IS the designer's
            // footprint polygon, not a circle: distInside is signed metres into that outline
            // (positive inside). -distInside is therefore the analogous "negative inside" SDF.
            //
            // The height profile bulges and pinches that shape by offsetting the footprint
            // boundary outward/inward: radiusMul > 1 grows the cross-section OUT past the footprint
            // (an overhang), radiusMul < 1 pinches it IN. The offset is expressed in metres via the
            // nominal reach so it has the same physical scale the circular path used.
            //
            // The query uses the warped+leaned point q so erosion and lean still displace the
            // cross-section; subtracting leanOffset would undo the lean we deliberately applied.
            float distInside = _footprintDistInside(q.x, q.z);
            float boundaryOffset = (radiusMul - 1f) * _nominalRadius;
            solid = -distInside - boundaryOffset;
        }
        else
        {
            // Circular cross-section: inside where horizontal distance is less than the radius.
            solid = horiz - radius;
        }

        // --- Fine craggy surface detail on the rock faces -----------------------------------
        if (_settings.sideJaggedness > 0f)
        {
            float crag = TerrainNoiseHelper.Fbm(
                new Vector3(p.x, p.y * 0.8f, p.z), _settings.sideJaggednessFrequency,
                _seed ^ 0x6C1A77B3, 3);
            solid += crag * _settings.sideJaggedness;
        }

        // --- Cap the body top and floor so it is a finite, walkable-topped mass -------------
        // Top: solid only below the summit. SmoothMin'd as an intersection (max) so the flat
        // summit cap blends into the bulging walls instead of meeting them at a hard rim.
        // The summit Y can be displaced by macro relief so the plateau has real high/low ground.
        float summitY = _summitReliefFn != null ? _summitY + _summitReliefFn(p.x, p.z) : _summitY;
        float topCut = p.y - summitY;                        // >0 above summit -> air
        solid = SmoothMax(solid, topCut, _totalHeight * 0.06f);

        // Floor fill: keep the rock solid down to (and a little below) the underlying ground so
        // the body grounds into the terrain — but ONLY directly beneath the rock body, not across
        // the whole volume. The volume bounds are far wider than the formation; filling every
        // below-ground voxel would mesh a flat ground apron all around the feature. We therefore
        // gate the fill by horizontal extent: it applies only inside the body's base cross-section
        // (plus a small skirt for a clean joint), so outside the rock the below-ground space
        // stays air and no apron is generated.
        float groundHere = _groundFn(p.x, p.z);
        float footprintSkirt = _nominalRadius * 0.15f + _settings.sideJaggedness;
        bool underBody;
        if (_shape == BodyShape.RadialTower && _footprintDistInside != null)
        {
            // Footprint-aware gate: under the body wherever the column is inside the footprint
            // polygon, widened by the skirt so the ground joint is clean. Uses the warped point q
            // so the gate tracks the same eroded base outline the solid test uses.
            underBody = _footprintDistInside(q.x, q.z) >= -footprintSkirt;
        }
        else
        {
            float baseRadius = _nominalRadius
                               * RockBodyProfile.RadiusMultiplier(0f, angle, _settings, _seed);
            underBody = horiz <= baseRadius + footprintSkirt;
        }
        if (underBody)
        {
            float floorFill = groundHere - p.y;              // >0 below ground -> force solid
            solid = Mathf.Min(solid, -floorFill);
        }
        else
        {
            // Outside the body footprint: never let the below-ground fill leak in. Clamp the
            // surface so anything below ground out here reads as air, leaving no flat apron.
            float aboveGround = p.y - groundHere;            // <0 below ground
            if (aboveGround < 0f) solid = Mathf.Max(solid, -aboveGround);
        }

        return solid;
    }

    /// <summary>
    /// Computes the horizontal distance from the body axis and the angle around it for point
    /// <paramref name="q"/>, branching on the body shape. For a radial tower the axis is a point;
    /// for a linear wall it is the cliff-edge line and "angle" is a signed lateral parameter that
    /// still feeds the angular term of the radius profile so the wall face lobes along its length.
    /// </summary>
    void HorizontalCoords(Vector3 q, Vector2 leanOffset, out float horiz, out float angle)
    {
        if (_shape == BodyShape.RadialTower)
        {
            Vector2 c = _axisXZ + leanOffset;
            float dx = q.x - c.x;
            float dz = q.z - c.y;   // Vector2 stores XZ: .y is the world-Z component
            horiz = Mathf.Sqrt(dx * dx + dz * dz);
            angle = Mathf.Atan2(dz, dx);
            return;
        }

        // Linear wall: measure distance to the cliff-edge SEGMENT (not the infinite line), so the
        // wall is a finite body that rounds off and CLOSES at its short ends rather than running
        // out flat to the volume bounds. The lean offset shifts the whole segment sideways.
        Vector2 perpDir = new Vector2(-(_lineB - _lineA).y, (_lineB - _lineA).x) / _lineLen;
        Vector2 a = _lineA + perpDir * Vector2.Dot(leanOffset, perpDir);
        Vector2 b = _lineB + perpDir * Vector2.Dot(leanOffset, perpDir);
        Vector2 ab = b - a;
        Vector2 p2 = new Vector2(q.x, q.z);

        // Clamped projection → nearest point ON the (leaned) segment.
        float along = Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / (_lineLen * _lineLen));
        Vector2 onSegment = a + ab * along;

        // horiz = full 2D distance to the segment. Alongside the wall this equals the
        // perpendicular distance; past an end it also picks up the along-axis overshoot, so the
        // body's solid (horiz - radius) closes the short ends with a rounded cap.
        horiz = Vector2.Distance(p2, onSegment);

        // Which side of the wall the point is on — for the angular term below.
        float side = Vector2.Dot(p2 - onSegment, perpDir);
        // "Angle" for a wall mixes position ALONG the line with the side, so the radius
        // profile's angular term makes the face bulge and pinch down the length of the cliff.
        angle = along * Mathf.PI * 2f + (side >= 0f ? 0f : Mathf.PI);
    }

    /// <summary>Smooth maximum (intersection of two SDFs) — the soft-blended counterpart of
    /// <see cref="SdfPrimitives.SmoothMin"/>, so the summit cap meets the walls without a hard rim.</summary>
    static float SmoothMax(float a, float b, float k)
    {
        return -SdfPrimitives.SmoothMin(-a, -b, k);
    }

    /// <summary>Folded SDF — no single surface height. Returns the summit Y as a sane stub.</summary>
    public float SurfaceHeight(float localX, float localZ) => _summitY;
}
