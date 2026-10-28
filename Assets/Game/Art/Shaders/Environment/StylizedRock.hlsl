#ifndef SPACEGAME_STYLIZEDROCK_INCLUDED
#define SPACEGAME_STYLIZEDROCK_INCLUDED

// Shared by every pass of StylizedRock.shader, so the forward, shadow, depth and depth-normals
// passes all place a rock identically.
//
// One draw is one model at one Mesh LOD of one rock size class: SV_InstanceID + _SegmentOffset
// indexes _RockVisible, the rocks RockCull.compute kept this frame. There is no object-to-world
// matrix at all.
//
//   Recolour - the texture's brightness, not its colour, picks a shade on a dark-mid-light ramp,
//              so one painted rock texture becomes red sandstone, yellow sandstone or grey basalt.
//              _KeepOriginal shows the texture's own colours instead (mossy granite).
//   Variety  - each rock leans toward _VariationColor and brightens or darkens by its tint.
//   Dust     - faces turned up gather the ground's colour, broken up by world-space noise, so a
//              rock sits in its desert instead of on it.
//   Contact  - the lowest part of the mesh darkens a little, where the ground shades it.

#include "GrassShared.hlsl"
#include "RockShared.hlsl"

StructuredBuffer<RockVisible> _RockVisible;
int _SegmentOffset;

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half   _KeepOriginal;
    half4  _DarkColor;
    half4  _MidColor;
    half4  _LightColor;
    half   _RampLow;
    half   _RampHigh;
    half4  _VariationColor;
    half   _VariationStrength;
    half   _TintVariation;
    half4  _DustColor;
    half   _DustAmount;
    half   _DustStart;
    half   _DustSoftness;
    float  _DustNoiseScale;
    half   _ContactDarken;
    float  _ContactHeight;
    half   _LightWrap;
    half   _AmbientStrength;
    half   _ShadowStrength;
CBUFFER_END

struct RockAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    uint   instanceID : SV_InstanceID;
};

struct RockVertex
{
    float3 positionWS;
    float3 normalWS;
    float  tint;
    float  contact;    // 0 at the mesh's base .. 1 from _ContactHeight up
};

RockVertex PlaceRockVertex(RockAttributes IN)
{
    RockVisible rock = _RockVisible[_SegmentOffset + IN.instanceID];
    float3 stretch = rock.stretchTint.xyz;
    float3 local = IN.positionOS.xyz * stretch * rock.positionScale.w;

    RockVertex v;
    v.positionWS = rock.positionScale.xyz + RotateByQuaternion(rock.rotation, local);
    // A stretched surface's normal scales by the inverse stretch.
    v.normalWS = normalize(RotateByQuaternion(rock.rotation, IN.normalOS / stretch));
    v.tint = rock.stretchTint.w;
    v.contact = saturate(IN.positionOS.y / max(_ContactHeight, 1e-4));
    return v;
}

half3 RockAlbedo(float2 uv, float3 positionWS, float3 normalWS, float tint, float contact)
{
    half3 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    half brightness = saturate((dot(texel, half3(0.299, 0.587, 0.114)) - _RampLow) / max(_RampHigh - _RampLow, 1e-3));
    half3 ramp = brightness < 0.5
        ? lerp(_DarkColor.rgb, _MidColor.rgb, brightness * 2.0)
        : lerp(_MidColor.rgb, _LightColor.rgb, brightness * 2.0 - 1.0);
    half3 colour = lerp(ramp, texel, _KeepOriginal);

    colour = lerp(colour, _VariationColor.rgb * (0.5 + brightness), smoothstep(0.35, 1.0, tint) * _VariationStrength);
    colour *= 1.0 + (frac(tint * 7.31) - 0.5) * _TintVariation;

    half noise = GrassNoise(positionWS.xz / max(_DustNoiseScale, 0.01)) * 0.6
               + GrassNoise(positionWS.xz / max(_DustNoiseScale, 0.01) * 3.1 + 17.0) * 0.4;
    half up = smoothstep(_DustStart, _DustStart + _DustSoftness, normalWS.y + (noise - 0.5) * 0.35);
    colour = lerp(colour, _DustColor.rgb, up * _DustAmount);

    colour *= lerp(1.0 - _ContactDarken, 1.0, contact);
    return colour;
}

#endif
