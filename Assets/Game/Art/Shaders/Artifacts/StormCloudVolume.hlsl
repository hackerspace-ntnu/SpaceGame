#ifndef SPACEGAME_STORMCLOUDVOLUME_INCLUDED
#define SPACEGAME_STORMCLOUDVOLUME_INCLUDED

// Shared by the two halves of the storm a Storm Flask uncorks: StormCloud.shader (the boiling
// body overhead) and StormVeil.shader (the rain column under it, which is also the INSIDE of the
// storm). Both are raymarched volumes rather than painted surfaces, and this file holds every
// part of that they agree on — the frame, the ray/volume intersections, the turbulence, the
// lighting stand-ins and the two guards a shell-marched volume in this project always needs.
//
// WHY VOLUMETRIC AT ALL, WHEN THE FIRST VERSION WAS A SURFACE. A painted lens has one depth per
// pixel, so nothing inside it can move past anything else: walk under it and it slides like a
// decal, and the churn is a pattern crawling on a fixed shape rather than cloud turning over.
// The same lesson the sandstorm learned the same way (Environment.md): painting the analytic
// shape onto a shell mesh gave a brown cylinder. It also settles the "from the inside" half of
// this artifact for free — a volume marched from the camera IS its own interior, so standing
// under the storm needs no second effect, no camera-parented quad and no render feature.
//
// ── THE FRAME, AND THE ONE THING THAT MAKES IT SURVIVABLE ────────────────────────────────────
//
// storm_cloud.py authors the meshes in a normalised CLOUD FRAME: centred on the origin, XZ
// radius 1, +Y up, body at y >= 0, rain veil hanging to y = -1. The transform carries the real
// size (12 m radius; the veil's node stretches its own axis by 1.25 to reach the design's 15 m).
//
// That frame does NOT arrive in positionOS. An `_exportlib` FBX leaves the Blender-to-Unity turn
// and the centimetre file scale on the NODE (`lr = (270.02, 0, 0)`, `ls = (100, 100, 100)`), so
// raw mesh Z is up and everything is at 1/100 — measured: Mesh_StormCloud_RainVolume.bounds is
// size (0.0184, 0.0184, 0.0100) against the 1.84 x 1.84 x 1.0 the script authors. Read raw,
// `length(positionOS.xz)` comes out near 0.009 instead of near 0.92 and every radial term reads
// the wrong axis at the wrong magnitude. StormCloudToFrame undoes exactly that node.
//
// THE MARCH RUNS IN WORLD SPACE AND SAMPLES IN THE CLOUD FRAME, which is what keeps `t` in
// METRES. Transform the world-space (unit-length) ray into the cloud frame and intersect there:
// because the direction was normalised in world space before the transform, the `t` that comes
// back out of the intersection is still a world distance. That matters more here than it looks —
// the veil's node is scaled NON-UNIFORMLY (12, 12, 15), so a march parameterised in cloud units
// would have a step that is 12 m long horizontally and 15 m long vertically, and extinction
// tuned at one camera angle would be wrong at another. In metres it is one number.
//
// ── THE TWO GUARDS ──────────────────────────────────────────────────────────────────────────
//
// Both shaders draw Cull Front / ZTest Always / ZWrite Off, and both therefore need:
//
//   * StormCloudPinFarPlane, because Cull Front means the fragment comes from the volume's FAR
//     side. That is nothing for a 24 m cloud until the cloud is 990 m away, at which point the
//     far side crosses the 1000 m far clip every camera prefab in this project uses and the
//     storm renders with a straight-edged hole in it. Depth means nothing to these passes, so
//     clamping costs nothing. Same fix, same reason, as SandstormWall.shader.
//
//   * StormCloudSceneClamp, because with ZTest Always the depth TEXTURE is the only thing that
//     can bury the storm behind a ridge — and it must be applied ONLY where there is geometry.
//     A sky pixel's depth is the far PLANE, which is nearest along the view axis, so clamping to
//     it cuts the storm off hardest exactly where the player is looking: the cloud would vanish
//     as you turned to face it.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "ArtifactSubstance.hlsl"

// ---------------------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------------------

/// A world point in the cloud frame: XZ radius 1, +Y up, body above 0, veil below.
float3 StormCloudToFrame(float3 positionWS, float meshToUnit)
{
    float3 positionOS = TransformWorldToObject(positionWS);
    return float3(positionOS.x, positionOS.z, -positionOS.y) * meshToUnit;
}

/// The same map for a DIRECTION. Not normalised afterwards, deliberately: the length it comes
/// back with is exactly what makes an intersection against it return `t` in world metres.
float3 StormCloudDirectionToFrame(float3 directionWS, float meshToUnit)
{
    float3 d = mul((float3x3)GetWorldToObjectMatrix(), directionWS);
    return float3(d.x, d.z, -d.y) * meshToUnit;
}

// ---------------------------------------------------------------------------------------
// Analytic bounds
// ---------------------------------------------------------------------------------------
// The MESH is the silhouette that raises the fragment; this is what the march actually runs
// against. It is deliberately a little INSIDE the mesh (the body against a lens whose rim
// wanders between 0.72 and 1.00 of the radius, the veil exactly on the authored 0.92), because a
// volume that reached outside its own shell would be cut off flat at the silhouette. Nothing is
// lost by that: what makes the storm's outline ragged is the noise eroding the volume, which is
// far more violent than any rim a mesh can hold, and it moves.
//
// ONE SHAPE SERVES BOTH, and the cloud body is why it is a cylinder rather than the lens-shaped
// ellipsoid it started as. An ellipsoid's radius goes to ZERO at its base, so a flat disc of
// cloud modelled that way has no underside at all — it pinches to a point exactly where the rain
// leaves it, and the storm renders with a bright band of open sky between the cloud and its own
// veil. A cylinder gives the flat dark base the design asks for; the density's own radial and
// vertical falloffs do the doming, where they can be shaped independently.

/// A finite open cylinder about the Y axis. Open because the veil mesh is: a rain column has no
/// lid and no floor, and giving the march caps would put a flat sheet of rain over the player's
/// head at the exact moment they walk under it.
///
/// SINGLE EXIT, and deliberately. An earlier version returned early from three places while
/// writing two `out` parameters; it compiled clean, reported `isSupported`, carried zero shader
/// messages — and the material drew as Unity's magenta error shader on most frames and as
/// nothing on the rest. The straight-line form below behaves.
bool StormCloudRayCylinder(float3 origin, float3 direction, float radius, float yMin, float yMax,
                           out float tNear, out float tFar)
{
    float a = dot(direction.xz, direction.xz);
    float b = dot(origin.xz, direction.xz);
    float c = dot(origin.xz, origin.xz) - radius * radius;

    // Big enough to keep the slab test meaningful, small enough that a ray this close to
    // vertical is inside the column for its whole length anyway.
    bool axial = a <= 1e-9;

    float discriminant = b * b - a * c;
    bool wallHit = !axial && discriminant > 0.0;

    float root = sqrt(max(discriminant, 0.0));
    float wallNear = wallHit ? (-b - root) / a : -1e9;
    float wallFar = wallHit ? (-b + root) / a : 1e9;

    // A ray straight up the axis never meets the wall, so the quadratic degenerates. That is the
    // view of somebody standing under the middle of the storm looking up — the one view this
    // effect exists for — so it is answered with the height slab alone rather than discarded.
    bool insideWall = axial && c <= 0.0;

    // The horizontal planes. A ray with no vertical component is inside the slab for its whole
    // length or outside it for all of it.
    bool level = abs(direction.y) < 1e-6;
    float invAxial = 1.0 / (direction.y + (level ? 1e-6 : 0.0));
    float slabA = (yMin - origin.y) * invAxial;
    float slabB = (yMax - origin.y) * invAxial;
    float slabNear = level ? -1e9 : min(slabA, slabB);
    float slabFar = level ? 1e9 : max(slabA, slabB);
    bool insideSlab = !level || (origin.y >= yMin && origin.y <= yMax);

    tNear = max(wallNear, slabNear);
    tFar = min(wallFar, slabFar);

    return (wallHit || insideWall) && insideSlab && tFar > tNear && tFar > 0.0;
}

// ---------------------------------------------------------------------------------------
// Motion
// ---------------------------------------------------------------------------------------
// Everything below is what separates "a cloud" from "a violent cloud", and all three are
// applied to the SAMPLE POINT rather than to the density: a field that is scrolled turns over,
// a field that is faded only blinks.

/// Billow turbulence — three octaves of value noise folded about its midpoint.
///
/// The fold is the whole point. Plain value noise makes BLOTCHES, which is what the surface
/// version of this shader had and why it read as smoke; folding it puts a crease where the
/// signed field crosses zero, and a field full of creases is the cauliflower edge a convective
/// cloud has. Three octaves rather than the two ArtifactSubstance settles for elsewhere,
/// because here the noise is not a shading detail — it IS the silhouette, and the third octave
/// is what puts fingers on the rim.
float StormCloudBillow(float3 position)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float normalisation = 0.0;

    [unroll]
    for (int octave = 0; octave < 3; octave++)
    {
        sum += amplitude * (1.0 - abs(2.0 * SubstanceNoise(position) - 1.0));
        normalisation += amplitude;
        position = position * 2.13 + 7.7;
        amplitude *= 0.5;
    }

    return sum / normalisation;
}

/// Push the sample point around by a low-frequency copy of itself.
///
/// A domain warp is the cheapest violence there is: it costs three noise taps and it turns every
/// straight feature in the field downstream of it into a curl. Without it the billows are round
/// and the cloud looks like it is simmering; with it they shear into each other.
float3 StormCloudWarp(float3 position, float scale, float strength, float drift)
{
    float3 q = position * scale + drift;
    float3 warp = float3(SubstanceNoise(q), SubstanceNoise(q + 19.3), SubstanceNoise(q + 47.1)) - 0.5;
    return position + warp * strength;
}

/// Turn the sample point about the storm's own axis, FASTER NEAR THE MIDDLE.
///
/// Uniform rotation is invisible on a field with no landmark in it. Differential rotation is not:
/// the core outruns the rim, so the noise is continuously sheared into spirals and the storm
/// reads as rotating rather than as drifting. It is why a real mesocyclone is legible from below.
float3 StormCloudSpin(float3 position, float radiansPerSecond, float coreBoost, float time)
{
    float radius = length(position.xz);
    float angle = radiansPerSecond * time * (1.0 + coreBoost * saturate(1.0 - radius));

    float s, c;
    sincos(angle, s, c);
    return float3(c * position.x - s * position.z, position.y, s * position.x + c * position.z);
}

// ---------------------------------------------------------------------------------------
// Light
// ---------------------------------------------------------------------------------------

/// Transmittance toward the sun through `depth` metres of already-accumulated cloud.
///
/// THREE TERMS, NOT ONE, and this is not a tuning preference. A single Beer term through the
/// optical depth a cloud this thick carries renders it BLACK — the sandstorm shipped exactly
/// that bug. The extra terms with progressively smaller extinction stand in for the light that
/// arrives after bouncing, which inside a cloud is most of the light there is.
float StormCloudSunTransmittance(float depth, float extinction)
{
    float tau = depth * extinction;
    return 0.5 * exp(-tau) + 0.35 * exp(-tau * 0.25) + 0.15 * exp(-tau * 0.06);
}

/// Henyey-Greenstein: how much more light scatters forward than back. What gives the rim facing
/// the sun its glow, and the reason a cloud looks different from its two sides.
float StormCloudPhase(float cosTheta, float anisotropy)
{
    float g = clamp(anisotropy, -0.95, 0.95);
    float g2 = g * g;
    float denominator = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * PI * pow(max(denominator, 1e-3), 1.5));
}

/// How hard a bolt lights this sample.
///
/// IN WORLD SPACE, from the strike point the cloud already replicates. A bolt lighting the cloud
/// from a point INSIDE it is what makes lightning read as being in the storm rather than as the
/// whole disc flickering: the near billows go bright, the far ones stay dark, and the shape of
/// the cloud is briefly legible from its own inside.
float StormCloudBoltGlow(float3 positionWS, float3 boltWS, float flash, float reach)
{
    float d = distance(positionWS, boltWS) / max(reach, 1e-3);
    return flash / (1.0 + d * d);
}

// ---------------------------------------------------------------------------------------
// Guards
// ---------------------------------------------------------------------------------------

/// Keep a Cull Front fragment off the far clip plane. See the file header.
void StormCloudPinFarPlane(inout float4 positionCS)
{
#if UNITY_REVERSED_Z
    positionCS.z = max(positionCS.z, 0.0);
#else
    positionCS.z = min(positionCS.z, positionCS.w);
#endif
}

/// End the march at whatever the scene put in front of the storm — and only where there IS
/// something to end it. See the file header for why the sky is exempt.
///
/// TWO ENDS OF THE DEPTH RANGE ARE BOTH "NO INFORMATION", and the second one is the trap. The
/// far end is the sky, which every volumetric in this project already skips. The NEAR end is a
/// camera that is not producing `_CameraDepthTexture` at all: an unbound texture samples as
/// Unity's white default, which under a reversed-Z projection reads as the NEAR CLIP PLANE — so
/// the clamp cuts the march to about thirty centimetres and the storm disappears without a
/// warning, a console message or a shader error. That is not only an editor problem: a camera
/// that renders the storm without having asked for a depth texture does the same thing in a
/// build, and there is no legible difference between "geometry exactly on the near plane" and
/// "no depth at all", so the near end is treated as no information too.
float StormCloudSceneClamp(float2 screenUv, float3 rayDirectionWS, float tFar)
{
    float rawDepth = SampleSceneDepth(screenUv);

#if UNITY_REVERSED_Z
    bool noDepth = rawDepth <= 0.0 || rawDepth >= 0.9999;
#else
    bool noDepth = rawDepth >= 1.0 || rawDepth <= 0.0001;
#endif

    if (noDepth) return tFar;

    float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float viewCos = max(1e-4, dot(rayDirectionWS, -UNITY_MATRIX_V[2].xyz));
    return min(tFar, sceneEyeDepth / viewCos);
}

#endif // SPACEGAME_STORMCLOUDVOLUME_INCLUDED
