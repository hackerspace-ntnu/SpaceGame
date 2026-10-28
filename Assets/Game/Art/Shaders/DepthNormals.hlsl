#ifndef SPACEGAME_DEPTHNORMALS_INCLUDED
#define SPACEGAME_DEPTHNORMALS_INCLUDED

// What every custom opaque shader's DepthNormals pass writes into _CameraNormalsTexture.
//
// The pass is not optional. SSAO runs a DepthNormals prepass, and with depth priming on that
// prepass IS the depth the forward pass tests against with ZTest Equal: an opaque shader with
// no DepthNormals pass is missing from it and draws nothing at all, and one that inherits
// Lit's pass through its FallBack is tested against an undisplaced mesh and flickers wherever
// its own vertex shader moves it. Neither throws. The pass must run the same vertex code and
// the same clip as the forward pass, and needs
// `#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT` for the encoding below.
//
// Expects Core.hlsl to be included first.

half4 EncodeDepthNormal(float3 normalWS)
{
    normalWS = normalize(normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormal = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
    return half4(PackFloat2To888(octNormal), 0);
#else
    return half4(normalWS, 0);
#endif
}

// The depth prepasses of a shader that neither moves its vertices nor clips its pixels.
struct RigidDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

struct RigidDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
};

RigidDepthVaryings RigidDepthVert(RigidDepthAttributes IN)
{
    RigidDepthVaryings OUT;
    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
    return OUT;
}

half4 RigidDepthFrag(RigidDepthVaryings IN) : SV_Target { return 0; }

half4 RigidDepthNormalsFrag(RigidDepthVaryings IN) : SV_Target
{
    return EncodeDepthNormal(IN.normalWS);
}

#endif // SPACEGAME_DEPTHNORMALS_INCLUDED
