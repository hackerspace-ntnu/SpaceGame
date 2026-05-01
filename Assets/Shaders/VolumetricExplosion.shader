Shader "Custom/VolumetricExplosion"
{
    Properties
    {
        _MainTex ("Noise Texture", 2D) = "white" {}
        _Speed ("Explosion Speed", Float) = 1.0
        _Scale ("Explosion Scale", Float) = 1.0
        _ExplosionIntensity ("Explosion Intensity", Float) = 1.0
        _CustomTime ("Custom Time", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Speed;
            float _Scale;
            float _ExplosionIntensity;
            float _CustomTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = hash(i);
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(a, b, u.x)
                     + (c - a) * u.y * (1.0 - u.x)
                     + (d - b) * u.x * u.y;
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;

                float2x2 m = float2x2(0.80, -0.60,
                                      0.60,  0.80);

                for (int i = 0; i < 6; i++)
                {
                    v += a * noise(p);
                    p = mul(m, p) * 2.02 + 17.3;
                    a *= 0.5;
                }

                return v;
            }

            float texNoise(float2 p)
            {
                return tex2D(_MainTex, frac(p)).r;
            }

            float texFbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;

                float2x2 m = float2x2(1.6, -1.2,
                                      1.2,  1.6);

                for (int i = 0; i < 4; i++)
                {
                    v += a * texNoise(p);
                    p = mul(m, p) * 0.5 + 0.031;
                    a *= 0.5;
                }

                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = uv * 6.0 - 3.0;
                p.x *= 1.0;

                float t = _CustomTime * _Speed;  // Time from controller - never decreases
                float life = t / 8.0;
                float2 q = p;

                // Cap time growth at 6 seconds - don't grow after that
                float tGrowth = min(t, 6.0);

                float dist = length(q);
                float2 dir = normalize(q + 1e-5);
                float ang = atan2(q.y, q.x);

                // Domain warp for turbulence
                float2 warp;
                warp.x = fbm(p * 2.4 + float2( tGrowth * 0.45, -tGrowth * 0.20));
                warp.y = fbm(p * 2.4 + float2(-tGrowth * 0.30,  tGrowth * 0.35));
                warp = warp * 2.0 - 1.0;

                float2 wp = p + warp * 0.22;

                // Irregular outer radius
                float baseRadius = tGrowth * 0.33;

                float angularNoise = fbm(float2(cos(ang), sin(ang)) * 3.0 + float2(tGrowth * 0.4, -tGrowth * 0.2));
                float radialNoise  = fbm(wp * 3.0 - dir * tGrowth * 1.6);
                float lobes        = fbm(float2(ang * 2.5, tGrowth * 0.35));

                float outerRadius = baseRadius;
                outerRadius += (angularNoise - 0.5) * 0.28;
                outerRadius += (radialNoise  - 0.5) * 0.18;
                outerRadius += (lobes        - cos(_Time.y)) * 0.52;

                outerRadius = max(outerRadius, 0.001);

                // Filled cloudy volume mask
                float volumeMask = smoothstep(outerRadius, outerRadius - 0.38, dist);

                float inside = clamp(1.0 - dist / outerRadius, 0.0, 1.0);

                // Procedural noise
                float d1 = fbm(wp * 4.0 - dir * tGrowth * 1.5);
                float d2 = fbm(wp * 7.5 + float2(0.0, tGrowth * 0.6));
                float d3 = fbm(wp * 12.0 - float2(tGrowth * 0.8, -tGrowth * 0.3));

                // Texture noise for richer cloudy breakup
                float2 tp1 = wp * 0.22 + float2(t * 0.025, -t * 0.018);
                float2 tp2 = wp * 0.40 + warp * 0.08 + float2(-t * 0.014, t * 0.021);
                float2 tp3 = wp * 0.75 - dir * t * 0.03;

                float td1 = texFbm(tp1);
                float td2 = texFbm(tp2);
                float td3 = texFbm(tp3);

                float texCloud = td1 * 0.9 + td2 * 0.95 + td3 * 0.15;

                float density = d1 * 0.40 + d2 * 0.22 + d3 * 0.10 + texCloud * 0.928;

                float cloud = volumeMask * smoothstep(0.18, 1.05, density + inside * 0.95);

                float clumps = volumeMask * smoothstep(0.32, 0.98,
                               d1 + d2 * 0.25 + texCloud * 0.55 + inside * 0.65);

                float fire = volumeMask * smoothstep(0.22, 1.12,
                             density + texCloud * 0.25 + inside * 1.25);

                float coreShape = dist + (fbm(p * 5.5 + tGrowth) - 0.5) * 0.12;
                float core = smoothstep(0.26, 0.0, coreShape) * exp(-tGrowth * 0.55);

                float edge = smoothstep(outerRadius + 0.02, outerRadius - 0.10, dist) *
                             (1.0 - smoothstep(outerRadius - 0.22, outerRadius - 0.34, dist));

                // Sparks - fade out after explosion phase
                float sparks = 0.0;
                for (int i = 0; i < 28; i++)
                {
                    float fi = float(i);
                    float a = fi * 6.2831853 / 28.0 + hash(float2(fi, 2.7)) * 1.5;
                    float2 sdir = float2(cos(a), sin(a));

                    float speed = 0.35 + hash(float2(fi, 7.1)) * 0.95;
                    float wobble = fbm(sdir * 4.0 + fi + tGrowth) * 0.22;

                    float2 sp = sdir * tGrowth * (speed + wobble);
                    float size = 0.012 + hash(float2(fi, 9.4)) * 0.018;

                    float s = smoothstep(size, 0.0, length(p - sp));
                    s *= smoothstep(3.0, 1.0, t);  // Fade out sparks earlier

                    sparks += s;
                }

                float smokeField = fbm(wp * 2.8 + float2(t * 0.1, t * 0.22));
                float smokeRadius = outerRadius + 0.18 + smokeField * 0.18;
                float smoke = smoothstep(smokeRadius, smokeRadius - 0.42, dist);
                smoke *= (1.0 - volumeMask * 0.65);
                smoke *= smoothstep(0.55, 2.8, t);

                // SOOT - enhanced to be inside explosion and linger longer
                float sootTime    = smoothstep(0.4, 1.8, t);      // Start earlier
                float sootLate    = smoothstep(1.5, 6.0, t);      // Much longer late phase
                float sootEnd     = smoothstep(4.0, 6.0, t);      // Linger until end
                float sootBoost   = lerp(0.35, 0.60, sootLate);

                float sootTex1 = texFbm(wp * 0.26 + float2(-_Time.y * 0.035,  _Time.y * 0.018));
                float sootTex2 = texFbm(wp * 0.52 + float2( _Time.y * 0.012, -_Time.y * 0.027));
                float sootTex3 = texFbm(wp * 0.95 + float2(-_Time.y * 0.022,  _Time.y * 0.015));

                float sootFbm1 = fbm(wp * 2.6 + float2(-_Time.y * 0.20, _Time.y * 0.09));
                float sootFbm2 = fbm(wp * 4.8 + float2( _Time.y * 0.11,-_Time.y * 0.16));

                float sootPattern = sootTex1 * 0.34
                                  + sootTex2 * 0.24
                                  + sootTex3 * 0.10
                                  + sootFbm1 * 0.40
                                  + sootFbm2 * 0.12;

                // Soot exists inside the explosion
                float sootRegion = volumeMask * lerp(0.75, 1.15, 1.0 - inside * 0.55);

                float soot = sootRegion
                           * sootTime
                           * smoothstep(0.36, 0.72, sootPattern + sootBoost * 0.18);

                float ashPockets = sootRegion
                                 * sootLate
                                 * smoothstep(0.42, 0.78, texCloud + sootTex2 * 0.65 + d2 * 0.25);

                float sootTakeover = sootRegion
                                   * sootEnd
                                   * smoothstep(0.28, 0.68, sootPattern + texCloud * 0.45 + (1.0 - inside) * 0.20);

                float sootAmt = clamp(
                    soot * 1.05 +
                    ashPockets * 0.95 +
                    sootTakeover * 1.35,
                    0.0, 1.0
                );

                // Color
                float3 darkRed  = float3(0.16, 0.02, 0.00);
                float3 red      = float3(0.82, 0.07, 0.00);
                float3 orange   = float3(1.00, 0.34, 0.03);
                float3 yellow   = float3(1.00, 0.78, 0.18);
                float3 white    = float3(1.00, 0.96, 0.82);
                float3 smokeCol = float3(0.08, 0.075, 0.07);
                float3 sootCol  = float3(0.02, 0.018, 0.016);

                float3 col = float3(0.0, 0.0, 0.0);

                col += darkRed * cloud * 1.00;
                col += red     * fire  * 1.10;
                col += orange  * pow(fire, 1.3) * 1.55;
                col += yellow  * pow(clumps * inside, 1.8) * 1.7;

                col += white * core * 2.0;
                col += yellow * core * 1.2;

                col += orange * edge * 1.2;
                col += float3(1.0, 0.45, 0.12) * sparks * 1.7;

                col *= 1.0 - sootAmt * 0.65;

                col = lerp(col, sootCol, sootAmt * 0.88);

                col = lerp(col, smokeCol, smoke * 0.55 + sootLate * sootAmt * 0.18);

                // Smooth fade out at the end - no harsh black transition
                float endFade = smoothstep(7.5, 8.0, t);
                col = lerp(col, float3(0.0, 0.0, 0.0), endFade * 0.8);

                col = 1.0 - exp(-col * 1.4);

                // Calculate alpha - transparent where there's no explosion
                float alpha = volumeMask * (cloud + fire + clumps) + edge + sparks * 0.3;
                alpha += smoke * 0.4;
                alpha += sootAmt * 0.7;  // Soot contributes to alpha
                alpha = smoothstep(0.0, 1.0, alpha);
                
                // Fade out alpha at the very end
                alpha *= (1.0 - endFade);
                
                // Apply overall intensity from controller
                alpha *= _ExplosionIntensity;

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}

