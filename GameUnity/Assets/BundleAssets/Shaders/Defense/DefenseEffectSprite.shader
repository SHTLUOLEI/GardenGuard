// 卡通糖果水：击杀反馈用的轻量 Sprite 特效（URP HLSL，无纹理采样）。
// _Shape：0 水泡破裂（击杀点） / 1 金币闪光（跟随飞行金币）。
// 进度取顶点 alpha（0→1），透明度按 1 - 进度淡出；每种形态一份共享材质，逐实例只改 SpriteRenderer.color。
// _Detail：1 完整（飞溅水滴、高光、十字星芒），0 简化（只保留水泡轮廓，闪光由运行时整层关闭）。
Shader "Defense90/EffectSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Water ("Water", Color) = (0.31,0.765,0.91,1)
        _Foam ("Foam", Color) = (1,0.99,0.96,1)
        _Deep ("Deep / Outline", Color) = (0.118,0.42,0.52,1)
        _Accent ("Sparkle", Color) = (1,0.86,0.35,1)
        _Shape ("Pop / Sparkle", Float) = 0
        _Detail ("Detail (1 Full / 0 Simple)", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Water, _Foam, _Deep, _Accent;
            float _Shape, _Detail;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            v2f vert(appdata v)
            {
                v2f o; o.pos = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; o.color = v.color; return o;
            }

            float Edge(float edge, float x) { float w = max(fwidth(x), 1e-4); return smoothstep(edge - w, edge + w, x); }
            float Band(float d, float r, float width) { return 1 - Edge(width, abs(d - r)); }
            float Disk(float2 p, float r) { return 1 - Edge(r, length(p)); }
            float4 Layer(float4 base, float3 color, float alpha) { return float4(lerp(base.rgb, color, alpha), max(base.a, alpha)); }

            // 先鼓起一个带高光的水泡，过半后破成水环并甩出水滴。
            float4 Pop(float2 p, float t, bool full)
            {
                float d = length(p);
                float4 c = 0;
                float grow = saturate(t * 2);
                float burst = saturate(t * 2 - 1);
                float radius = lerp(0.25, 0.55, grow);
                if (burst <= 0)
                {
                    c = Layer(c, _Deep.rgb, Disk(p, radius + 0.06) * 0.6);
                    c = Layer(c, _Water.rgb, Disk(p, radius) * 0.75);
                    c = Layer(c, _Foam.rgb, Band(d, radius - 0.03, 0.025));
                    if (full) c = Layer(c, _Foam.rgb, Disk(p - float2(-0.4, 0.4) * radius, radius * 0.28));
                }
                else
                {
                    float ring = lerp(radius, 0.85, burst);
                    c = Layer(c, _Water.rgb, Band(d, ring, 0.06 * (1 - burst) + 0.015));
                    c = Layer(c, _Foam.rgb, Band(d, ring, 0.02));
                    if (full)
                    {
                        float angle = atan2(p.y, p.x);
                        float sector = 6.2831853 / 8;
                        float center = (floor((angle + 3.1415927) / sector) + 0.5) * sector - 3.1415927;
                        float2 drop = float2(cos(center), sin(center)) * (ring + 0.1);
                        c = Layer(c, _Water.rgb, Disk(p - drop, 0.07 * (1 - burst * 0.6)));
                    }
                }
                return c;
            }

            // 四角星芒 + 柔光点，随进度旋转缩放，表现金币收集的闪光。
            float4 Sparkle(float2 p, float t, bool full)
            {
                float s = sin(t * 6.2831853 * 1.5), co = cos(t * 6.2831853 * 1.5);
                float2 q = float2(p.x * co - p.y * s, p.x * s + p.y * co);
                float pulse = 0.65 + 0.35 * sin(t * 25);
                float star = 1 - Edge(0.06, abs(q.x) * abs(q.y) * 6 + length(q) * 0.12 / pulse);
                float4 c = float4(_Accent.rgb, star);
                c = Layer(c, _Foam.rgb, Disk(q, 0.12 * pulse));
                if (full) c = Layer(c, _Accent.rgb, (1 - Edge(0.55, length(p))) * 0.25 * pulse);
                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = i.color.a;
                float2 p = i.uv * 2 - 1;
                bool full = _Detail > 0.5;
                float4 c = _Shape < 0.5 ? Pop(p, t, full) : Sparkle(p, t, full);
                float fade = _Shape < 0.5 ? 1 - smoothstep(0.6, 1, t) : 1 - t * t;
                return float4(c.rgb * i.color.rgb, saturate(c.a) * fade);
            }
            ENDHLSL
        }
    }
}
