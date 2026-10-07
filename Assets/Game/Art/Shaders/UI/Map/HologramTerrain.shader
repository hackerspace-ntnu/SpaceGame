Shader "Hologram/Terrain"
{
    Properties
    {
        _Color           ("Tint", Color)               = (0.45, 0.95, 1.0, 1.0)
        _LowColor        ("Valley Tint", Color)        = (0.10, 0.45, 0.65, 1.0)
        _HighColor       ("Peak Tint",   Color)        = (0.80, 1.00, 1.00, 1.0)
        _Intensity       ("Intensity", Range(0, 5))    = 1.4
        _MinY            ("Min Y", Float)              = 0
        _MaxY            ("Max Y", Float)              = 200
        _Fresnel         ("Fresnel Power", Range(0, 8))= 2.5
        _FresnelStrength ("Fresnel Strength", Range(0, 4)) = 1.6
        _ContourSpacing  ("Contour Spacing (m)", Float)= 20
        _ContourThickness("Contour Thickness", Range(0, 0.5)) = 0.08
        _ScanlineSpeed   ("Scanline Speed", Float)     = 0.6
        _ScanlineDensity ("Scanline Density", Float)   = 40
        _ScanlineStrength("Scanline Strength", Range(0,1)) = 0.10
        _Flicker         ("Flicker", Range(0, 1))      = 0.04
        _GridSize        ("Grid Spacing (m)", Float)   = 32
        _GridStrength    ("Grid Strength", Range(0, 2))= 0.4

        _FogColor        ("Fog Color", Color)          = (0.20, 0.40, 0.55, 1.0)
        _FogIntensity    ("Fog Intensity", Range(0, 4))= 0.6
        _FogNoiseScale   ("Fog Noise Scale", Float)    = 0.015
        _FogNoiseSpeed   ("Fog Noise Speed", Float)    = 0.08
        _DiscoveryRadius ("Discovery Radius (m)", Float)= 700
        _DiscoveryFalloff("Discovery Falloff (m)", Float)= 280
        _DiscoveryCount  ("Discovery Count", Int)      = 0
        _FogEnabled      ("Fog Enabled", Float)        = 1
        _FogSlopeDim     ("Fog Slope Dim", Range(0,1)) = 0.85
        _FogViewDim      ("Fog View Dim", Range(0,1))  = 0.6

        _MapRadius       ("Map Radius (m)", Float)     = 1100
        _MapEdgeFalloff  ("Map Edge Falloff (m)", Float)= 450

        _HiddenPOIColor  ("Hidden POI Fog Color", Color) = (1.0, 0.25, 0.20, 1.0)
        _HiddenPOIIntensity("Hidden POI Fog Intensity", Range(0, 4)) = 1.6
        _HiddenPOICount  ("Hidden POI Count", Int)     = 0
    }

    SubShader
    {
        Tags { "Queue"="Overlay+1" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Cull   Off
        Lighting Off
        ZWrite Off
        ZTest  LEqual
        Blend  One One

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float3 worldPos  : TEXCOORD0;
                float3 worldNorm : TEXCOORD1;
                float3 viewDir   : TEXCOORD2;
                float  localY    : TEXCOORD3;
                float2 simXZ     : TEXCOORD4; // simulated-world XZ for fog sampling
            };

            float4 _Color, _LowColor, _HighColor;
            float  _Intensity;
            float  _MinY, _MaxY;
            float  _Fresnel, _FresnelStrength;
            float  _ContourSpacing, _ContourThickness;
            float  _ScanlineSpeed, _ScanlineDensity, _ScanlineStrength;
            float  _Flicker;
            float  _GridSize, _GridStrength;

            float4 _FogColor;
            float  _FogIntensity, _FogNoiseScale, _FogNoiseSpeed;
            float  _DiscoveryRadius, _DiscoveryFalloff, _FogEnabled;
            float  _FogSlopeDim, _FogViewDim;
            int    _DiscoveryCount;
            // Per-renderer (set via MaterialPropertyBlock): simulated world-space
            // origin of this chunk in XZ. Vertex object-space xz * scale + this = sim world XZ.
            float4 _ChunkWorldOriginXZ;
            // Per-renderer scale that maps baked object-space XZ (which spans the
            // bake-time chunkSize) to the current chunkSize. xy = (scaleX, scaleZ).
            float4 _ChunkObjectToSimScaleXZ;
            // Circular map vignette (in simulated world XZ).
            float4 _MapCenterXZ; // xy = sim-world XZ center
            float  _MapRadius, _MapEdgeFalloff;
            #define MAX_DISCOVERY_POINTS 256
            float4 _DiscoveryPoints[MAX_DISCOVERY_POINTS]; // xz = world position

            // Hidden + undiscovered POIs that recolor the surrounding fog reddish.
            // .xy = sim-world XZ, .z = radius (m), .w = falloff (m).
            #define MAX_HIDDEN_POIS 32
            float4 _HiddenPOIs[MAX_HIDDEN_POIS];
            float4 _HiddenPOIColor;
            float  _HiddenPOIIntensity;
            int    _HiddenPOICount;

            // Cheap value noise on world XZ for animated fog wisps.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos       = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.viewDir   = normalize(_WorldSpaceCameraPos - o.worldPos);
                o.localY    = v.vertex.y;
                float2 scl  = _ChunkObjectToSimScaleXZ.xy;
                if (scl.x < 0.0001) scl.x = 1.0;
                if (scl.y < 0.0001) scl.y = 1.0;
                o.simXZ     = v.vertex.xz * scl + _ChunkWorldOriginXZ.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Height-based color (low = valley, high = peak).
                float h = saturate((i.localY - _MinY) / max(0.0001, (_MaxY - _MinY)));
                fixed3 height = lerp(_LowColor.rgb, _HighColor.rgb, h);

                // Contour lines on local Y.
                float band = i.localY / max(0.0001, _ContourSpacing);
                float distLine = abs(band - round(band));
                float contour = 1.0 - smoothstep(0.0, _ContourThickness, distLine);

                // Grid on world XZ — gives the "tactical map" feel.
                float2 g = i.worldPos.xz / max(0.0001, _GridSize);
                float gx = abs(frac(g.x) - 0.5);
                float gz = abs(frac(g.y) - 0.5);
                float grid = smoothstep(0.48, 0.5, max(gx, gz)) * _GridStrength;

                // Fresnel rim — brighter at silhouette, gives volumetric edge.
                float fr = pow(1.0 - saturate(dot(normalize(i.worldNorm), normalize(i.viewDir))), _Fresnel);
                float rim = fr * _FresnelStrength;

                // Scanline + flicker.
                float scan = 1.0 - _ScanlineStrength
                             + _ScanlineStrength * sin((i.worldPos.y + _Time.y * _ScanlineSpeed) * _ScanlineDensity);
                float flick = 1.0 + sin(_Time.y * 23.7) * _Flicker;

                fixed3 col = height * _Color.rgb * _Intensity;
                col += contour * _Color.rgb * 1.2;
                col += grid    * _Color.rgb;
                col += rim     * _Color.rgb;
                col *= scan * flick;

                // Fog of war: visibility = max over discovery points of soft circle.
                // visibility 1 = fully revealed terrain, 0 = fully fogged.
                float visibility = 0.0;
                if (_FogEnabled > 0.5 && _DiscoveryCount > 0)
                {
                    float falloff = max(0.0001, _DiscoveryFalloff);
                    float inner = max(0.0, _DiscoveryRadius - falloff);
                    int count = min(_DiscoveryCount, MAX_DISCOVERY_POINTS);
                    for (int k = 0; k < count; k++)
                    {
                        float2 d = i.simXZ - _DiscoveryPoints[k].xz;
                        float dist = length(d);
                        float v = 1.0 - smoothstep(inner, _DiscoveryRadius, dist);
                        visibility = max(visibility, v);
                    }
                }
                else
                {
                    visibility = 1.0;
                }

                if (_FogEnabled > 0.5)
                {
                    // Animated wispy fog over simulated world XZ.
                    float2 np = i.simXZ * _FogNoiseScale;
                    float n = vnoise(np + _Time.y * _FogNoiseSpeed) * 0.6
                            + vnoise(np * 2.3 - _Time.y * _FogNoiseSpeed * 0.7) * 0.4;

                    // Hidden+undiscovered POI influence: a soft circular weight
                    // around each hidden POI, max'd across all POIs. Used to
                    // tint the fog reddish in their general area without
                    // pinpointing the exact location.
                    float hiddenWeight = 0.0;
                    if (_HiddenPOICount > 0)
                    {
                        int hcount = min(_HiddenPOICount, MAX_HIDDEN_POIS);
                        for (int hk = 0; hk < hcount; hk++)
                        {
                            float2 hd = i.simXZ - _HiddenPOIs[hk].xy;
                            float hr = max(0.0001, _HiddenPOIs[hk].z);
                            float hf = max(0.0001, _HiddenPOIs[hk].w);
                            float hdist = length(hd);
                            // 1 inside (radius - falloff), smoothly fades to 0 at radius.
                            float inner = max(0.0, hr - hf);
                            float w = 1.0 - smoothstep(inner, hr, hdist);
                            hiddenWeight = max(hiddenWeight, w);
                        }
                        // Modulate by the same noise as the fog so the red tint
                        // also reads as wispy rather than as a flat disc.
                        hiddenWeight *= (0.55 + n * 0.9);
                    }

                    fixed3 baseFogCol = _FogColor.rgb * (_FogIntensity * (0.55 + n * 0.9));
                    fixed3 hiddenFogCol = _HiddenPOIColor.rgb * _HiddenPOIIntensity;
                    fixed3 fogCol = lerp(baseFogCol, hiddenFogCol, saturate(hiddenWeight));
                    // Keep grid faintly visible through the fog so the chunked feel reads.
                    fogCol += grid * _Color.rgb * 0.35;

                    // Steep slopes (peak faces) and view-grazing surfaces
                    // accumulate too much fog brightness in the additive blend
                    // because more surface area lands per screen pixel. Dim the
                    // fog there so peaks aren't visually "revealed" through fog.
                    float3 N = normalize(i.worldNorm);
                    float upDot = saturate(N.y);                  // 1 on flat ground, 0 on vertical cliff
                    float slopeAtten = lerp(1.0 - _FogSlopeDim, 1.0, upDot);
                    float viewDot = saturate(dot(N, normalize(i.viewDir)));
                    float viewAtten = lerp(1.0 - _FogViewDim, 1.0, viewDot);
                    fogCol *= slopeAtten * viewAtten;

                    col = lerp(fogCol, col, visibility);
                }

                // Round map vignette: distance from sim-world center, with a fuzzy
                // noise-perturbed edge so it doesn't read as a hard circle. Uses
                // additive blend, so multiplying by mask = fade to invisible.
                if (_MapRadius > 0.001)
                {
                    float2 d = i.simXZ - _MapCenterXZ.xy;
                    float dist = length(d);
                    // Fuzz the boundary with low-frequency noise.
                    float edgeNoise = vnoise(i.simXZ * 0.02 + _Time.y * 0.05) - 0.5;
                    float fuzz = edgeNoise * _MapEdgeFalloff * 0.6;
                    float falloff = max(0.0001, _MapEdgeFalloff);
                    float mask = 1.0 - smoothstep(_MapRadius + fuzz, _MapRadius + falloff + fuzz, dist);
                    col *= mask;
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
