Shader "SpaceGame/RuinScannerPulse"
{
    // Ground-projected scan with rays from the muzzle to the moving line.
    //
    // Two render branches share this shader, switched by uv.y:
    //   * uv.y >= 0  → ground strip. Lays on the terrain in front of the gun.
    //                  uv.y is the longitudinal coord (0 = muzzle, 1 = far),
    //                  uv.x is across-width. A sharp moving line travels from
    //                  the far end (uv.y=1) toward the muzzle. Already-scanned
    //                  area (in front of the line) shows a faint tint. Sides
    //                  fade with _EdgeFade.
    //   * uv.y <  0  → ray bundle from muzzle to the current line. Each ray's
    //                  vertex stores -t along the ray in uv.y (uv.y = -1 at
    //                  muzzle, uv.y = 0 at the far end). The shader only
    //                  lights the portion of each ray between muzzle and the
    //                  current sweep position.
    Properties
    {
        _Color        ("Color",                Color)             = (0.45, 0.95, 1.0, 1.0)
        _LineIntensity("Line Intensity",       Range(0, 16))      = 8.0
        _RayIntensity ("Ray Intensity",        Range(0, 16))      = 3.0
        _ScannedAlpha ("Scanned Area Alpha",   Range(0, 1))       = 0.06
        _BandWidth    ("Line Width (uv)",      Range(0.005, 0.2)) = 0.025
        _BandSharpness("Line Sharpness",       Range(1, 32))      = 8
        _EdgeFade     ("Side Edge Fade",       Range(0, 0.5))     = 0.06
        _NearLimit    ("Sweep Near Limit",     Range(0, 1))       = 0.4
        _FarLimit     ("Sweep Far Limit",      Range(0, 1))       = 1.0
        _Progress     ("Progress",             Range(0, 1))       = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            fixed4 _Color;
            float  _LineIntensity;
            float  _RayIntensity;
            float  _ScannedAlpha;
            float  _BandWidth;
            float  _BandSharpness;
            float  _EdgeFade;
            float  _NearLimit;
            float  _FarLimit;
            float  _Progress;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Ease the pulse in/out so it doesn't pop on spawn/despawn.
                float lifeEnvelope = smoothstep(0.0, 0.06, _Progress)
                                   * smoothstep(1.0, 0.92, _Progress);

                // Sweep oscillates between _FarLimit and _NearLimit across
                // the lifetime: far → near → far. Triangle wave on
                // _Progress: 0→0.5 maps far→near, 0.5→1 maps near→far.
                // _NearLimit/_FarLimit are in UV.y space (= depth/maxRange),
                // so the sweep can be confined to a higher portion of the
                // beam instead of running all the way to the muzzle.
                float tri = abs(_Progress * 2.0 - 1.0);   // 1 at endpoints, 0 mid
                float sweep = lerp(_NearLimit, _FarLimit, tri);
                float minSweep = min(_NearLimit, _FarLimit);
                float maxSweep = max(_NearLimit, _FarLimit);

                // ---- Ray plane branch ----
                // Solid wedge of color from the muzzle (apex) out to the
                // strip's far edge (base). Lit only between muzzle and the
                // current sweep position — beyond the line is dark.
                if (i.uv.y < 0.0)
                {
                    // uv.y in [-1, 0]: -1 = apex/muzzle, 0 = far base.
                    float rayT = i.uv.y + 1.0;

                    // Soft cutoff at the sweep position so the plane has a
                    // crisp leading edge that meets the strip's bright line
                    // without a hard step.
                    float head = smoothstep(sweep + 0.01, sweep - 0.01, rayT);
                    // Fade toward the muzzle so the source feels focused and
                    // the wedge widens visually toward the line.
                    float fill = saturate(rayT / max(0.001, sweep));
                    // Soften the long edges of the wedge so it doesn't look
                    // like a flat triangle decal.
                    float edge = smoothstep(0.0, _EdgeFade, i.uv.x)
                               * smoothstep(1.0, 1.0 - _EdgeFade, i.uv.x);

                    float a = saturate(fill * head * edge * _RayIntensity * lifeEnvelope);
                    fixed3 rgb = _Color.rgb * a;
                    return fixed4(rgb, a);
                }

                // ---- Ground strip branch ----

                // Soften the long sides of the strip so the rectangle has a
                // clean edge instead of a hard rectangular cutout.
                float edge = smoothstep(0.0, _EdgeFade, i.uv.x)
                           * smoothstep(1.0, 1.0 - _EdgeFade, i.uv.x);

                // Already-scanned area: UV.y values the line has actually
                // passed over. The line walks between minSweep and maxSweep,
                // and once it has reached minSweep it has covered everything
                // from minSweep up to maxSweep.
                float lowestSeen = lerp(maxSweep, minSweep, saturate(_Progress * 2.0));
                float scannedFill = step(lowestSeen, i.uv.y)
                                  * step(i.uv.y, maxSweep)
                                  * _ScannedAlpha;

                // Moving line — thin band centered on uv.y = sweep.
                float dist = abs(i.uv.y - sweep) / max(0.001, _BandWidth);
                float band = pow(saturate(1.0 - dist), _BandSharpness) * _LineIntensity;

                float a = saturate((band + scannedFill) * edge * lifeEnvelope);
                fixed3 rgb = _Color.rgb * a;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
