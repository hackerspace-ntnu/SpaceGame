Shader "Custom/BallLighting"
{
    Properties
    {
        iTime("iTime", Float) = 0
        iResolution("iResolution", Vector) = (512, 512, 1, 1)
        iMouse("iMouse", Vector) = (0, 0, 0, 0)

        _UvScale("UV Scale", Float) = 0.4
        _NoisyRadiusScale("Noisy Radius Scale", Float) = 0.02
        _StreamRadius("Stream Radius", Float) = 0.073
        _StreamStrength("Stream Strength", Float) = 2.35
        _HazeFalloff("Haze Falloff", Float) = 120
        _CorePunchFalloff("Core Punch Falloff", Float) = 50
        _CoreStrength("Core Strength", Float) = 1.0
        _PlasmaStrength("Plasma Strength", Float) = 0.42
        _FinalBrightness("Final Brightness", Float) = 1.35
        _CoreMaskRadius("Core Mask Radius", Float) = 0.12
        _CoreMaskSoftness("Core Mask Softness", Float) = 0.05

        _HazeColor("Haze Color", Color) = (0.02, 0.04, 0.20, 1)
        _CloudColor("Cloud Color", Color) = (0.04, 0.09, 0.46, 1)
        _BlowoutColor("Blowout Color", Color) = (0.07, 0.18, 0.90, 1)
        _HotCoreColor("Hot Core Color", Color) = (1.25, 1.30, 1.45, 1)
        _CorePunchColor("Core Punch Color", Color) = (0.18, 0.48, 1.15, 1)
        _StreamColor("Stream Color", Color) = (0.45, 0.85, 1.55, 1)
        _CoreColor("Core Color", Color) = (0.08, 0.14, 0.62, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "BallLightning"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float iTime;
                float4 iResolution;
                float4 iMouse;
                float _UvScale;
                float _NoisyRadiusScale;
                float _StreamRadius;
                float _StreamStrength;
                float _HazeFalloff;
                float _CorePunchFalloff;
                float _CoreStrength;
                float _PlasmaStrength;
                float _FinalBrightness;
                float _CoreMaskRadius;
                float _CoreMaskSoftness;
                float4 _HazeColor;
                float4 _CloudColor;
                float4 _BlowoutColor;
                float4 _HotCoreColor;
                float4 _CorePunchColor;
                float4 _StreamColor;
                float4 _CoreColor;
            CBUFFER_END

            static const float TAU = 6.2832;

            float2x2 Rotate(float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float2x2(c, s, -s, c);
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 hash21(float p)
            {
                float2 q = frac(float2(p * 123.34, p * 456.21));
                q += dot(q, q + 45.32);
                return frac(float2(q.x * q.y, q.x + q.y)) * 2.0 - 1.0;
            }

            float noise2(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash12(i + float2(0.0, 0.0));
                float b = hash12(i + float2(1.0, 0.0));
                float c = hash12(i + float2(0.0, 1.0));
                float d = hash12(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm2(float2 uv)
            {
                float s = 0.0;
                float a = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    s += a * noise2(uv);
                    uv = uv * 2.02 + float2(17.3, 9.1);
                    a *= 0.5;
                }
                return s;
            }

            float2 coreCenter(float t, float mx, float my)
            {
                return float2(
                    mx * 0.013 * sin(t * 0.65),
                    my * 0.008 * cos(t * 0.83)
                );
            }

            float4 PlasmaNode(float2 p, float t)
            {
                float2 q = p * 0.7;
                q = mul(Rotate(0.10 * sin(t * 0.7)), q);

                float2 cloudOffset = float2(0.020, 0.010);
                float2 qc = q - cloudOffset;

                float core =
                    exp(-2600.0 * dot(q - float2(0.002, -0.001), q - float2(0.002, -0.001))) +
                    0.8 * exp(-3400.0 * dot(q - float2(-0.002, 0.001), q - float2(-0.002, 0.001)));

                float body = exp(-460.0 * dot(q, q));

                float2 wq = qc;
                wq.x *= 0.85;
                wq.y *= 1.20;
                wq += 0.020 * float2(
                    fbm2(wq * 8.0 + float2(0.0, t * 0.45)) - 0.5,
                    fbm2(wq * 8.0 + float2(4.2, -t * 0.38)) - 0.5
                );

                float nr = length(wq);
                float cloudMask = exp(-1000.0 * nr * nr);

                float n0 = fbm2(wq * 6.0 + float2(0.0, t * 0.30));
                float n1 = fbm2(wq * 11.0 + float2(3.7, -t * 0.55));
                float n2 = fbm2(wq * 18.0 + float2(-2.4, t * 0.85));

                float cloudNoise = 0.55 * n0 + 0.30 * n1 + 0.15 * n2;
                float cloud = cloudMask * smoothstep(0.48, 0.82, cloudNoise);
                cloud *= 0.75 + 0.45 * fbm2(wq * 13.0 + float2(t * 0.20, 0.0));

                float ang = atan2(q.y, q.x);
                float r = length(q);

                float stressBand = smoothstep(0.020, 0.030, r) * (1.0 - smoothstep(0.030, 0.060, r));
                float stressNoise = fbm2(float2(ang * 4.5, t * 1.8 + r * 90.0));
                float stress = stressBand * (0.4 + 1.1 * stressNoise) * (0.5 + 1.2 * body + 0.8 * core);

                return float4(core, body, cloud, stress);
            }

            float CircleSDF(float2 p, float r)
            {
                return length(p) - r;
            }

            float LineSDF(float2 p, float2 a, float2 b, float s)
            {
                float2 pa = a - p;
                float2 ba = a - b;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h) - s;
            }

            float RandomFloat(float2 seed)
            {
                seed = sin(seed * float2(123.45, 546.23)) * 345.21 + 12.57;
                return frac(seed.x * seed.y);
            }

            float SimpleNoise(float2 uv, float octaves)
            {
                float sn = 0.0;
                float amplitude = 1.0;
                float deno = 0.0;
                int octaveCount = (int)clamp(octaves, 1.0, 6.0);

                for (int i = 1; i <= 6; i++)
                {
                    if (i > octaveCount)
                    {
                        break;
                    }

                    float2 grid = smoothstep(0.0, 1.0, frac(uv));
                    float2 id = floor(uv);
                    float2 offs = float2(0.0, 1.0);
                    float bl = RandomFloat(id);
                    float br = RandomFloat(id + offs.yx);
                    float tl = RandomFloat(id + offs);
                    float tr = RandomFloat(id + offs.yy);
                    sn += lerp(lerp(bl, br, grid.x), lerp(tl, tr, grid.x), grid.y) * amplitude;
                    deno += amplitude;
                    uv *= 3.5;
                    amplitude *= 0.5;
                }

                return sn / max(deno, 1e-5);
            }

            float3 Bolt(float2 uv, float len, float ind, float isFlicker)
            {
                float2 t = float2(0.0, fmod(iTime, 200.0) * 2.0);

                float sn = SimpleNoise(uv * 20.0 - t * 3.0 + float2(ind * 1.5, 0.0), 2.0) * 2.0 - 1.0;
                uv.x += sn * 0.03 * smoothstep(0.0, 0.2, abs(uv.y));
                float l0 = LineSDF(uv, float2(0.0, 0.0), float2(0.0, len), 0.0001);
                float3 l = float3(l0, l0, l0);
                l = 0.1 / max(float3(0.0, 0.0, 0.0), l) * float3(0.1, 0.2, 0.6);
                l = clamp(1.0 - exp(l * -0.02), 0.0, 1.0) * smoothstep(len - 0.01, 0.0, abs(uv.y));
                float3 bolt = l;

                uv = mul(Rotate(TAU * 0.125), uv);
                sn = SimpleNoise(uv * 25.0 - t * 4.0 + float2(ind * 2.3, 0.0), 2.0) * 2.0 - 1.0;
                uv.x += sn * uv.y * 0.8 * smoothstep(0.1, 0.25, len);
                len *= 0.5;
                l0 = LineSDF(uv, float2(0.0, 0.0), float2(0.0, len), -1e-3);
                l = float3(l0, l0, l0);
                l = 0.2 / max(float3(0.0, 0.0, 0.0), l) * float3(0.1, 0.2, 0.6);
                l = clamp(1.0 - exp(l * -0.03), 0.0, 1.0) * smoothstep(len * 0.7, 0.0, abs(uv.y));
                bolt += l;

                float hz = 4.0;
                hz *= iTime * (0.5 + isFlicker) * TAU;
                float rr = RandomFloat(float2(ind, ind)) * 0.2 * TAU;
                float flicker = sin(hz + rr) * 0.5 + 0.5;
                float flickerMultiplier = max(smoothstep(0.5, 0.0, flicker), isFlicker);
                return bolt * flickerMultiplier;
            }

            float LightningStreams(float2 p, float t, float radius)
            {
                float d = length(p);
                float contain = 1.0 - smoothstep(radius * 0.80, radius, d);

                float2 q = p;
                q = mul(0.5 * Rotate(sin(t)), q);
                q += 0.010 * float2(
                    fbm2(q * 12.0 + float2(0.0, t * 0.8)) - 0.5,
                    fbm2(q * 12.0 + float2(4.0, -t * 0.7)) - 0.5
                );

                float s1 = abs(q.x + 0.18 * (fbm2(q * 18.0 + float2(t * 1.2, 0.0)) - 0.5));
                float s2 = abs(dot(q, normalize(float2(0.75, 0.65))) + 0.14 * (fbm2(q.yx * 20.0 + float2(0.0, t * 1.0)) - 0.5));
                float s3 = abs(dot(q, normalize(float2(-0.65, 0.76))) + 0.12 * (fbm2(q * 22.0 + float2(2.0, -t * 1.1)) - 0.5));

                float l1 = exp(-s1 * 220.0);
                float l2 = exp(-s2 * 240.0);
                float l3 = exp(-s3 * 260.0);

                float breakup1 = smoothstep(0.45, 0.85, fbm2(q * 30.0 + float2(t * 2.2, 1.0)));
                float breakup2 = smoothstep(0.40, 0.82, fbm2(q * 28.0 + float2(-t * 1.8, 3.0)));
                float breakup3 = smoothstep(0.42, 0.84, fbm2(q * 26.0 + float2(t * 2.0, 5.0)));

                float streams = l1 * breakup1 + l2 * breakup2 + l3 * breakup3;
                float centerBias = 1.0 - smoothstep(radius * 0.15, radius * 0.95, d);
                return streams * contain * centerBias;
            }

            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float2 DirectedBoltPoint(float tt, float2 A, float2 B, float seed, float t)
            {
                float2 dir = normalize(B - A);
                float2 n = float2(-dir.y, dir.x);
                float len = length(B - A);

                float phase = floor(t * 8.0);
                float s = seed + phase * 19.37;

                float env = sin(3.14159265 * tt);

                float big = 0.0;
                big += (SimpleNoise(float2(tt * 1.6 + s * 0.13, 1.7), 2.0) - 0.5) * 3.8;
                big += (SimpleNoise(float2(tt * 2.7 + s * 0.21, 3.9), 2.0) - 0.5) * 2.2;

                float mid = 0.0;
                mid += (SimpleNoise(float2(tt * 6.0 + s * 0.9, 7.1), 2.0) - 0.5) * 1.1;
                mid += (SimpleNoise(float2(tt * 9.5 + s * 1.4, 11.3), 2.0) - 0.5) * 0.45;

                float fine = 0.0;
                fine += (SimpleNoise(float2(tt * 20.0 + s * 2.1, 17.7), 2.0) - 0.5) * 0.45;
                fine += sign(sin(tt * 23.0 + s * 0.7)) * 0.04;

                float off = (big + mid + fine) * env;
                float amp = min(len * 0.22, 0.22);
                return lerp(A, B, tt) + n * off * amp;
            }

            float3 DirectedBolt(float2 uv, float2 A, float2 B, float seed, float t, float thickness)
            {
                float d = 1e9;
                float2 prev = DirectedBoltPoint(0.0, A, B, seed, t);

                for (int i = 1; i <= 36; i++)
                {
                    float tt = (float)i / 36.0;
                    float2 cur = DirectedBoltPoint(tt, A, B, seed, t);
                    d = min(d, LineSDF(uv, prev, cur, 0.0));
                    prev = cur;
                }

                float core = exp(-d / thickness * 10.0);
                float glow = exp(-d / thickness * 2.2);

                float3 bolt = float3(0.0, 0.0, 0.0);
                bolt += float3(exp(-d * 800.0), 0.22, 1.0) * glow;
                bolt += float3(0.95, 0.80, 1.35) * core;
                return bolt;
            }

            float2 DirectedBoltTangent(float tt, float2 A, float2 B, float seed, float t)
            {
                float e = 0.01;
                float2 p0 = DirectedBoltPoint(max(0.0, tt - e), A, B, seed, t);
                float2 p1 = DirectedBoltPoint(min(1.0, tt + e), A, B, seed, t);
                return normalize(p1 - p0);
            }

            void mainImage(out float4 fragColor, in float2 fragCoord)
            {
                float2 uv = (fragCoord.xy - 0.5 * iResolution.xy) / iResolution.y;
                float3 col = float3(0.0, 0.0, 0.0);
                uv *= _UvScale;

                float t = iTime;
                uv -= coreCenter(t * 2.0, 1.0 * hash11(2.0), 2.0 * hash11(4.0));

                float2 C = coreCenter(t, 1.0, 1.0);
                float2 p = uv - C;

                float2 mouseP = float2(0.20, -0.18);
                if (iMouse.z > 0.0)
                {
                    mouseP = (iMouse.xy - 0.5 * iResolution.xy) / iResolution.y;
                    mouseP *= 0.4;
                    mouseP -= coreCenter(t * 2.0, 1.0 * hash11(2.0), 2.0 * hash11(4.0));
                    mouseP -= C;
                }

                float2 A = float2(0.0, 0.0);
                float2 B = float2(0.30, -0.28);

                if (iMouse.z > 0.0)
                {
                    B = (iMouse.xy - 0.5 * iResolution.xy) / iResolution.y;
                    B *= 0.4;
                    B -= coreCenter(t * 2.0, 1.0 * hash11(2.0), 2.0 * hash11(4.0));
                }

                float4 node = PlasmaNode(p, t);
                float hotCore = node.x;
                float body = node.y;
                float cloud = node.z;
                float stress = node.w;

                float r = 0.02 * SimpleNoise(uv * 50.0 - float2(0.0, fmod(iTime, 2.0) * 5.0), 3.0);
                float distToCore = length(p);
                float edgeNoise = SimpleNoise(p * 50.0 - float2(0.0, fmod(iTime, 2.0) * 5.0), 3.0);
                float noisyRadius = _NoisyRadiusScale * edgeNoise;
                float streamMask = LightningStreams(p, t, _StreamRadius);

                float blowout = exp(-165.0 * distToCore * distToCore) * (0.28 + 0.90 * body);
                float cloudGlow = cloud * (0.35 + 0.45 * fbm2((p - float2(0.020, 0.010)) * 10.0 + float2(0.0, t * 0.4)));
                float hazeMask = exp(-_HazeFalloff * distToCore * distToCore);
                float haze = hazeMask * (0.4 + 0.6 * fbm2(p * 7.0 + float2(t * 0.15, 0.0)));
                float micro = 0.0;

                float3 plasma = float3(0.0, 0.0, 0.0);
                plasma += _HazeColor.rgb * haze * 0.20;
                plasma += _CloudColor.rgb * cloudGlow * 0.95;
                plasma += _BlowoutColor.rgb * blowout * 2.5;
                plasma += float3(0.35, 0.55, 1.05) * micro * 0.22;
                plasma += _HotCoreColor.rgb * hotCore * _CoreStrength;

                float corePunch = exp(-_CorePunchFalloff * distToCore * distToCore);
                plasma += _CorePunchColor.rgb * corePunch * 0.75;
                float3 animatedStreamColor = _StreamColor.rgb;
                animatedStreamColor.r += 0.15 * sin(t * 1.3);
                plasma += animatedStreamColor * streamMask * (1.0 + 0.65 * stress) * _StreamStrength;

                float3 core = 0.6 / max(0.0001, CircleSDF(p, noisyRadius)) * _CoreColor.rgb;
                core = 1.0 - exp(core * -0.05);
                col = core;
                col += plasma * _PlasmaStrength;

                if (iMouse.z > 0.0)
                {
                    col += DirectedBolt(uv, A, B, 77.0, t, 0.032);

                    float branchT = clamp(sin(t) + cos(t * 4.0) * cos(t * 2.0), 0.2, 0.7);
                    float2 branchStart = DirectedBoltPoint(branchT, A, B, 77.0, t);
                    float2 mainTan = DirectedBoltTangent(branchT, A, B, 77.0, t);

                    float branchSide = sign(sin(floor(t * 1.0) + 2.3));
                    float2 branchDir = mul(Rotate(branchSide * 10.0), mainTan);
                    branchDir = normalize(branchDir + 0.25 * hash21(17.0 + floor(t * 6.0)));

                    float mainLen = length(B - A);
                    float2 branchEnd = branchStart - branchDir * (mainLen * branchT);
                    col += DirectedBolt(uv, branchStart, B, 77.0 * (1.5 - branchT), t, 0.015) * 0.99;

                    float2 dir = A - B;
                    float boltCountMouse = floor(RandomFloat(float2(floor(iTime / 0.2), floor(iTime / 0.2))) * 3.0);
                    for (int i = 0; i < 3; i++)
                    {
                        if ((float)i >= boltCountMouse)
                        {
                            break;
                        }

                        float ii = (float)i;
                        float angle = atan2(dir.y, dir.x);
                        angle += ii * 0.1;
                        col += Bolt(mul(Rotate(-(angle - TAU * 0.25)), (uv - A)), 0.8 * RandomFloat(float2(angle, angle)) * 0.5 + 0.01, t, 1.0);
                    }

                    float rootGlow = exp(-20.0 * length(uv - A));
                    col += float3(0.95, 0.35, 1.0) * rootGlow * 1.6;
                }

                float boltCount = floor(RandomFloat(float2(floor(iTime / 0.2), floor(iTime / 0.2))) * 2.0);
                int boltCountInt = (int)boltCount;
                if (boltCountInt > 0)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (i >= boltCountInt)
                        {
                            break;
                        }

                        float ii = (float)i;
                        float angle = ii * TAU / max(boltCount, 1.0);
                        angle += RandomFloat(float2(120.0, cos(iTime))) + RandomFloat(float2(boltCount + floor(iTime * 5.0 + ii), boltCount + floor(iTime * 5.0 + ii))) * 2.95;
                        col += Bolt(mul(Rotate(angle), uv), 0.8 * RandomFloat(float2(angle, angle)) * 0.5 + 0.01, ii, 0.0);
                    }
                }

                float maskSoftness = max(0.0001, _CoreMaskSoftness);
                float maskInner = max(0.0, _CoreMaskRadius - maskSoftness);
                float coreMask = 1.0 - smoothstep(maskInner, _CoreMaskRadius, distToCore);
                col *= coreMask;
                col *= max(0.0, _FinalBrightness);
             
                fragColor = float4(col, 1.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 fragCoord = IN.uv * iResolution.xy;
                float4 color;
                mainImage(color, fragCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
