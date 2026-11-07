// 卡通糖果水：Defense90 战场水系特效统一 Shader（URP HLSL，无纹理采样）。
// _Mode：0 范围提示 / 1 水柱 / 2 水花扩散 / 3 水滴与阴影 / 4 彗星水弹。
// _Detail：1 完整、0 简化，对应守点特效档位表 ShaderDetail；简化档只保留轮廓、双色与描边。
// 批量契约：_Progress < 0 时进度取顶点 alpha，_Opacity *= 1 - 进度；顶点绿通道 < 0.5 表示击杀水花。
Shader "Defense90/WorldWater"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Water ("Water", Color) = (0.31,0.765,0.91,1)
        _Foam ("Foam", Color) = (1,0.99,0.96,1)
        _Deep ("Deep / Outline", Color) = (0.118,0.42,0.52,1)
        _Mode ("Area / Column / Splash / Drop / Comet", Float) = 0
        _Progress ("Progress (-1: Vertex Splash)", Range(-1,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Detail ("Detail (1 Full / 0 Simple)", Range(0,1)) = 1
        _Droplets ("Hit Droplets", Float) = 6
        _KillDroplets ("Kill Droplets", Float) = 10
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
            float4 _Water, _Foam, _Deep;
            float _Mode, _Progress, _Opacity, _Detail, _Droplets, _KillDroplets;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            v2f vert(appdata v)
            {
                v2f o; o.pos = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; o.color = v.color; return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            float Fbm(float2 p) { return Noise(p) * 0.65 + Noise(p * 2.13 + 7.1) * 0.35; }
            // 屏幕导数抗锯齿的硬边，保持卡通的清晰轮廓。
            float Edge(float edge, float x) { float w = max(fwidth(x), 1e-4); return smoothstep(edge - w, edge + w, x); }
            float Band(float d, float r, float width) { return 1 - Edge(width, abs(d - r)); }
            float Disk(float2 p, float r) { return 1 - Edge(r, length(p)); }
            float Caustic(float2 p, float t)
            {
                float a = sin(p.x * 9 + sin(p.y * 7 + t * 3) * 1.6 + t * 2);
                float b = sin(p.y * 10 + sin(p.x * 6 - t * 2.4) * 1.4 - t * 1.7);
                return Edge(0.82, a * b * 0.5 + 0.5);
            }
            float Bubbles(float2 p, float t, float density)
            {
                float2 grid = p * density + float2(0, -t);
                float2 cell = floor(grid), f = frac(grid) - 0.5;
                float h = Hash(cell);
                float2 o = float2(h - 0.5, frac(h * 13.7) - 0.5) * 0.5;
                float r = 0.12 + 0.1 * frac(h * 7.3);
                return step(0.45, h) * Band(length(f - o), r, 0.035);
            }

            // 图层合成：深色描边 -> 水体 -> 泡沫/高光；返回预乘前的颜色与 alpha。
            float4 Layer(float4 base, float3 color, float alpha)
            {
                return float4(lerp(base.rgb, color, alpha), max(base.a, alpha));
            }

            float4 Area(float2 p, float progress, bool full)
            {
                float d = length(p);
                // 中心透出地面、边缘渐浓，避免蓝色半透明叠在暖色地面上发灰。
                float4 c = float4(_Water.rgb, (1 - Edge(0.9, d)) * (0.08 + smoothstep(0.45, 0.9, d) * (full ? 0.5 : 0.38)));
                if (full)
                {
                    float caustic = Caustic(p * 1.3, progress * 4) * (1 - Edge(0.86, d));
                    c = Layer(c, _Foam.rgb, caustic * 0.45);
                    c = Layer(c, _Foam.rgb, Bubbles(p, progress * 2.5, 5) * (1 - Edge(0.84, d)) * 0.7);
                    // 外描边 + 旋转虚线环 + 中心准星。
                    c = Layer(c, _Deep.rgb, Band(d, 0.955, 0.03) * 0.75);
                    float angle = atan2(p.y, p.x);
                    float dash = Edge(0.3, frac(angle * 3.8197 + progress * 1.5));
                    c = Layer(c, _Foam.rgb, Band(d, 0.92, 0.035) * dash);
                    c = Layer(c, _Water.rgb, Band(d, 0.92, 0.035) * (1 - dash));
                    float cross = (1 - Edge(0.018, abs(p.x))) + (1 - Edge(0.018, abs(p.y)));
                    c = Layer(c, _Deep.rgb, saturate(cross) * (1 - Edge(0.14, d)) * Edge(0.05, d) * 0.7);
                    c = Layer(c, _Foam.rgb, Band(d, 0.9 * progress, 0.03) * 0.9 * step(0.03, progress));
                }
                else
                {
                    c = Layer(c, _Deep.rgb, Band(d, 0.92, 0.045) * 0.8);
                    c = Layer(c, _Foam.rgb, Band(d, 0.92, 0.02));
                    c = Layer(c, _Foam.rgb, Band(d, 0.9 * progress, 0.02) * 0.8 * step(0.03, progress));
                }
                return c;
            }

            float4 Column(float2 p, float progress, bool full)
            {
                float edge = abs(p.x);
                float width = 0.62;
                if (full) width += (Fbm(float2(p.y * 3 + progress * 6, 1.7)) - 0.5) * 0.18;
                float body = (1 - Edge(width, edge)) * (1 - Edge(0.92, p.y)) * Edge(-0.98, p.y);
                float outline = (1 - Edge(width + 0.12, edge)) * (1 - Edge(0.98, p.y)) * Edge(-1, p.y);
                float4 c = float4(_Deep.rgb, outline * 0.85);
                float3 water = lerp(_Water.rgb, _Foam.rgb, (1 - Edge(0.2, edge)) * 0.55);
                if (full)
                {
                    float flow = Fbm(float2(p.x * 2.5, p.y * 3 + progress * 9));
                    float tone = Edge(0.62, flow) * 0.5 + Edge(0.45, flow) * 0.5;
                    water = lerp(lerp(_Deep.rgb, _Water.rgb, 0.55 + tone * 0.45), _Foam.rgb, Edge(0.74, flow) * 0.8);
                }
                c = Layer(c, water, body);
                if (full)
                {
                    float highlight = (1 - Edge(0.07, abs(p.x + 0.28 + p.y * 0.05))) * body;
                    c = Layer(c, _Foam.rgb, highlight * 0.85);
                    c = Layer(c, _Foam.rgb, Bubbles(float2(p.x * 1.4, p.y), progress * 6, 4) * body);
                    // 底部泡沫裙边随落下展开。
                    float2 q = float2(p.x / (0.8 + progress * 0.6), (p.y + 0.92) * 3);
                    float skirt = Disk(q, 0.55 + 0.25 * sin(p.x * 14 + progress * 10));
                    c = Layer(c, _Foam.rgb, skirt * Edge(-1, p.y));
                }
                return c;
            }

            float4 Splash(float2 p, float progress, bool full, float droplets)
            {
                float d = length(p);
                float angle = atan2(p.y, p.x);
                float radius = 0.2 + progress * 0.62;
                float fade = 1 - progress * 0.5;
                float4 c = 0;
                if (full)
                {
                    float wobble = sin(angle * 7 + progress * 11) * 0.025;
                    c = Layer(c, _Deep.rgb, Band(d + wobble, radius, 0.1) * 0.5 * fade);
                    c = Layer(c, _Water.rgb, Band(d + wobble, radius, 0.08) * fade);
                    c = Layer(c, _Foam.rgb, Band(d + wobble, radius - 0.02, 0.03) * fade);
                    c = Layer(c, _Water.rgb, Band(d, radius * 0.66, 0.045) * fade * 0.9);
                    c = Layer(c, _Foam.rgb, Band(d, radius * 0.38, 0.02) * fade * 0.6);
                    c = Layer(c, _Foam.rgb, Caustic(p * 1.6, progress * 3) * (1 - Edge(radius * 0.6, d)) * fade * 0.5);
                }
                else
                {
                    c = Layer(c, _Deep.rgb, Band(d, radius, 0.085) * fade * 0.5);
                    c = Layer(c, _Water.rgb, Band(d, radius, 0.065) * fade);
                    c = Layer(c, _Foam.rgb, Band(d, radius - 0.015, 0.02) * fade);
                }
                // 弹射水滴：数量来自档位表，沿抛物线外飞并缩小。
                float count = max(droplets, 0);
                [branch] if (count > 0.5)
                {
                    float sector = 6.2831853 / count;
                    float index = floor((angle + 3.1415927) / sector);
                    float center = (index + 0.5) * sector - 3.1415927;
                    float jitter = Hash(float2(index, count));
                    float reach = radius + 0.12 + progress * (0.12 + jitter * 0.14);
                    float2 drop = float2(cos(center), sin(center)) * reach;
                    float size = (0.075 + jitter * 0.04) * (1 - progress * 0.7);
                    float dropDisk = Disk(p - drop, size);
                    c = Layer(c, _Deep.rgb, Disk(p - drop, size + 0.015) * fade * 0.55);
                    c = Layer(c, _Water.rgb, dropDisk * fade);
                    if (full) c = Layer(c, _Foam.rgb, Disk(p - drop + float2(size, -size) * 0.35, size * 0.35) * fade);
                }
                return c;
            }

            float4 Drop(float2 p, bool full)
            {
                float ellipse = length(float2(p.x * 1.15, p.y * 0.85));
                float4 c = float4(_Deep.rgb, (1 - Edge(0.92, ellipse)) * (full ? 0.8 : 0.6));
                c = Layer(c, _Water.rgb, 1 - Edge(0.76, ellipse));
                if (full)
                {
                    c = Layer(c, lerp(_Water.rgb, _Foam.rgb, 0.5), (1 - Edge(0.5, length(p - float2(-0.08, 0.1)))) * 0.6);
                    c = Layer(c, _Foam.rgb, Disk(p - float2(-0.25, 0.28), 0.17));
                }
                return c;
            }

            // 彗星水弹：quad 的 +y 为前进方向，头部在上，尾巴逐渐收窄淡出。
            float4 Comet(float2 p)
            {
                float2 head = float2(0, 0.55);
                float headDisk = length((p - head) * float2(1, 1.1));
                float tail = saturate((head.y - p.y) / 1.55);
                float tailWidth = 0.42 * (1 - tail);
                float tailMask = (1 - Edge(tailWidth, abs(p.x))) * step(p.y, head.y) * Edge(-1, p.y);
                float4 c = float4(_Deep.rgb, (1 - Edge(0.45, headDisk)) * 0.8);
                c = Layer(c, _Water.rgb, tailMask * (1 - tail) * 0.85);
                c = Layer(c, _Foam.rgb, (1 - Edge(0.12, abs(p.x))) * tailMask * (1 - tail) * 0.8);
                c = Layer(c, _Water.rgb, 1 - Edge(0.36, headDisk));
                c = Layer(c, _Foam.rgb, Disk(p - head - float2(-0.12, 0.12), 0.12));
                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float progress = _Progress;
                float opacity = _Opacity;
                float droplets = _Droplets;
                if (progress < 0)
                {
                    progress = i.color.a;
                    opacity *= 1 - progress;
                    if (i.color.g < 0.5) droplets = _KillDroplets;
                    i.color = float4(1, 1, 1, 1);
                }
                float2 p = i.uv * 2 - 1;
                bool full = _Detail > 0.5;
                float4 c;
                [branch] if (_Mode < 0.5) c = Area(p, progress, full);
                else if (_Mode < 1.5) c = Column(p, progress, full);
                else if (_Mode < 2.5) c = Splash(p, progress, full, droplets);
                else if (_Mode < 3.5) c = Drop(p, full);
                else c = Comet(p);
                // 水花在前半程保持实色，末段再淡出；批量与逐材质两条路径使用同一曲线。
                if (_Mode > 1.5 && _Mode < 2.5) opacity = sqrt(saturate(opacity));
                return float4(c.rgb * i.color.rgb, saturate(c.a) * opacity * i.color.a);
            }
            ENDHLSL
        }
    }
}
