// 卡通糖果水：UI 层技能脉冲（URP HLSL）。保留 _Center/_Radius（战场像素）与 _Phase 三阶段契约：
// 0 蓄力（描边环 + 扫描水环）、1 释放（冲击水环 + 水滴）、2 余波（破碎泡沫环）。_Detail 0 时只保留主环与进度。
Shader "Defense90/OrbitalPulse"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Tint ("Pulse Color", Color) = (0.31,0.765,0.91,1)
        _Foam ("Foam", Color) = (1,0.99,0.96,1)
        _Deep ("Deep / Outline", Color) = (0.118,0.42,0.52,1)
        _Center ("Center In Battlefield Pixels", Vector) = (320,340,0,0)
        _Radius ("Radius", Float) = 170
        _Progress ("Stage Progress", Range(0,1)) = 0
        _Phase ("Telegraph Release Hit", Float) = 0
        _Intensity ("Intensity", Range(0,4)) = 1
        _Detail ("Normal 1 Simplified 0", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Tint, _Foam, _Deep, _Center;
            float _Radius, _Progress, _Phase, _Intensity, _Detail;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            v2f vert(appdata v)
            {
                v2f o; o.pos = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; o.color = v.color; return o;
            }

            float Edge(float edge, float x) { float w = max(fwidth(x), 1e-4); return smoothstep(edge - w, edge + w, x); }
            float Band(float d, float r, float width) { return 1 - Edge(width, abs(d - r)); }
            float4 Layer(float4 base, float3 color, float alpha) { return float4(lerp(base.rgb, color, alpha), max(base.a, alpha)); }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv * float2(640, 680) - _Center.xy) / max(_Radius, 1);
                float d = length(p);
                float angle = atan2(p.y, p.x);
                bool full = _Detail > 0.5;
                float t = _Progress;
                float3 water = _Tint.rgb * _Intensity;
                float4 c = float4(water, (1 - Edge(0.95, d)) * 0.1);
                if (_Phase < 0.5)
                {
                    c = Layer(c, _Deep.rgb, Band(d, 1, 0.04) * 0.7);
                    c = Layer(c, water, Band(d, 1, 0.028) * (0.5 + t * 0.5));
                    c = Layer(c, _Foam.rgb, Band(d, t, 0.025) * step(0.03, t));
                    if (full)
                    {
                        float dash = Edge(0.35, frac(angle * 1.9099 + t));
                        c = Layer(c, _Foam.rgb, Band(d, 1.08, 0.015) * dash * 0.8);
                    }
                }
                else if (_Phase < 1.5)
                {
                    float fade = 1 - t * 0.6;
                    c = float4(water, (1 - Edge(0.95, d)) * 0.25 * (1 - t));
                    c = Layer(c, _Deep.rgb, Band(d, t, 0.07) * fade * 0.6);
                    c = Layer(c, water, Band(d, t, 0.05) * fade);
                    c = Layer(c, _Foam.rgb, Band(d, t, 0.018) * fade);
                    if (full)
                    {
                        float sector = 6.2831853 / 10;
                        float center = (floor((angle + 3.1415927) / sector) + 0.5) * sector - 3.1415927;
                        float2 drop = float2(cos(center), sin(center)) * (t * 1.15 + 0.05);
                        c = Layer(c, water, (1 - Edge(0.05 * (1 - t), length(p - drop))) * fade);
                        c = Layer(c, _Foam.rgb, Band(d, t * 0.6, 0.02) * fade * 0.7);
                    }
                }
                else
                {
                    float fade = 1 - t;
                    c = float4(water, 0);
                    c = Layer(c, water, Band(d, 1, 0.03) * fade * 0.6);
                    if (full)
                    {
                        float sector = angle * 9.5492966;
                        float broken = step(0.25, sin(sector + sin(sector * 3) * 0.7));
                        c = Layer(c, _Foam.rgb, broken * Band(d, 0.8 + t * 0.4, 0.025) * fade);
                    }
                }
                return float4(c.rgb, saturate(c.a) * _Tint.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
