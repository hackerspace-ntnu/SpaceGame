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
            };

            float4 _Color, _LowColor, _HighColor;
            float  _Intensity;
            float  _MinY, _MaxY;
            float  _Fresnel, _FresnelStrength;
            float  _ContourSpacing, _ContourThickness;
            float  _ScanlineSpeed, _ScanlineDensity, _ScanlineStrength;
            float  _Flicker;
            float  _GridSize, _GridStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos       = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.viewDir   = normalize(_WorldSpaceCameraPos - o.worldPos);
                o.localY    = v.vertex.y;
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

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
