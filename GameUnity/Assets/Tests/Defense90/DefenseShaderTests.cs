using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Defense90Tests
{
    // 渲染每种形态的两档画面：必须可见、两档有可测差异、不同形态互不相同，防止分档或形态分支失效。
    public sealed class DefenseShaderTests
    {
        private const int SIZE = 64;
        private const string WATER = "Assets/BundleAssets/Defense90/World/DefenseWaterMaterial.mat";
        private const string EFFECT = "Assets/BundleAssets/Defense90/World/DefenseEffectMaterial.mat";

        private GameObject m_camera, m_sprite;
        private SpriteRenderer m_renderer;
        private RenderTexture m_target;
        private Texture2D m_pixels;
        private Sprite m_quad;
        private Material m_material;

        [SetUp] public void SetUp()
        {
            m_quad = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/BundleAssets/Defense90/World/WaterQuadSprite.asset");
            m_camera = new GameObject("ShaderTestCamera"); m_sprite = new GameObject("ShaderTestSprite") { layer = 31 };
            var camera = m_camera.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 1.05f; camera.aspect = 1; camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(20000, 20000, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            m_target = new RenderTexture(SIZE, SIZE, 0, RenderTextureFormat.ARGB32); camera.targetTexture = m_target;
            m_pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            m_sprite.transform.position = new Vector3(20000, 20000, 0);
            m_sprite.transform.localScale = Vector3.one * 2 / m_quad.bounds.size.x;
            m_renderer = m_sprite.AddComponent<SpriteRenderer>(); m_renderer.sprite = m_quad;
        }

        [TearDown] public void TearDown()
        {
            RenderTexture.active = null;
            if (m_material != null) Object.DestroyImmediate(m_material);
            Object.DestroyImmediate(m_camera); Object.DestroyImmediate(m_sprite);
            Object.DestroyImmediate(m_target); Object.DestroyImmediate(m_pixels);
        }

        private Color32[] Render()
        {
            var camera = m_camera.GetComponent<Camera>();
            camera.Render(); RenderTexture.active = m_target;
            m_pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0); m_pixels.Apply();
            return m_pixels.GetPixels32();
        }

        private Material Use(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.NotNull(source, path);
            Assert.IsFalse(ShaderUtil.ShaderHasError(source.shader), source.shader.name);
            Assert.IsTrue(source.shader.isSupported, source.shader.name);
            // 只销毁本测试复制的材质；SpriteRenderer 默认材质是内置资源。
            if (m_material != null) Object.DestroyImmediate(m_material);
            m_material = new Material(source);
            m_renderer.sharedMaterial = m_material;
            return m_material;
        }

        private static long Coverage(Color32[] pixels)
        {
            long sum = 0;
            foreach (Color32 pixel in pixels) sum += pixel.a;
            return sum;
        }

        private static float Difference(Color32[] a, Color32[] b)
        {
            long sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) + Mathf.Abs(a[i].a - b[i].a);
            return sum / (float)(a.Length * 4 * 255);
        }

        private Color32[] Water(Material material, int mode, float progress, int detail, int droplets)
        {
            material.SetFloat("_Mode", mode); material.SetFloat("_Progress", progress); material.SetFloat("_Opacity", 1);
            material.SetFloat("_Detail", detail); material.SetFloat("_Droplets", droplets);
            m_renderer.color = Color.white;
            return Render();
        }

        [Test] public void WaterModesAreVisibleAndFullTierDiffersFromSimple()
        {
            var material = Use(WATER);
            // 每种形态取有代表性的进度；水滴数按完整档 6、简化档 2 模拟档位表。
            float[] progress = { .6f, .5f, .4f, 0, 0 };
            var full = new Color32[5][];
            for (int mode = 0; mode < 5; mode++)
            {
                full[mode] = Water(material, mode, progress[mode], 1, 6);
                Color32[] simple = Water(material, mode, progress[mode], 0, 2);
                Assert.Greater(Coverage(full[mode]), SIZE * SIZE * 8, $"模式 {mode} 完整档必须可见");
                Assert.Greater(Coverage(simple), SIZE * SIZE * 4, $"模式 {mode} 简化档必须可见");
                // 彗星水弹不读 _Detail，分档由运行时切换形态 4/3 实现。
                if (mode < 4) Assert.Greater(Difference(full[mode], simple), .01f, $"模式 {mode} 两档应有可见差异");
            }
            for (int a = 0; a < 5; a++)
                for (int b = a + 1; b < 5; b++)
                    Assert.Greater(Difference(full[a], full[b]), .01f, $"模式 {a} 与 {b} 图案不应相同");
        }

        [Test] public void KillSplashUsesKillDropletCountThroughVertexGreen()
        {
            var material = Use(WATER);
            material.SetFloat("_Mode", 2); material.SetFloat("_Progress", -1); material.SetFloat("_Opacity", 1);
            material.SetFloat("_Detail", 0); material.SetFloat("_Droplets", 0); material.SetFloat("_KillDroplets", 10);
            m_renderer.color = new Color(1, 1, 1, .3f); Color32[] hit = Render();
            m_renderer.color = new Color(1, 0, 1, .3f); Color32[] kill = Render();
            Assert.Greater(Coverage(kill), Coverage(hit), "击杀水花应额外绘制水滴");
        }

        [Test] public void EffectShapesAreVisibleAndSimpleTierDropsDetail()
        {
            var material = Use(EFFECT);
            Assert.AreEqual("Defense90/EffectSprite", material.shader.name);
            for (int shape = 0; shape < 2; shape++)
            {
                material.SetFloat("_Shape", shape);
                // 水泡取鼓起阶段（高光只在完整档），闪光取中段。
                m_renderer.color = new Color(1, 1, 1, shape == 0 ? .3f : .4f);
                material.SetFloat("_Detail", 1); Color32[] full = Render();
                material.SetFloat("_Detail", 0); Color32[] simple = Render();
                // 星芒是细长图形，覆盖面积天然较小。
                Assert.Greater(Coverage(full), SIZE * SIZE * 2, $"形态 {shape} 必须可见");
                Assert.Greater(Difference(full, simple), .002f, $"形态 {shape} 两档应有差异");
            }
        }

        [Test] public void PulseShaderCompilesForUrp()
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameLogic.Defense90.DefenseAssets>("Assets/BundleAssets/Defense90/DefenseAssets.asset");
            Shader shader = assets.PulseMaterial.shader;
            Assert.AreEqual("Defense90/OrbitalPulse", shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
        }

        [Test] public void TimelineEditsThroughSerializedObjectAreUndoable()
        {
            var timeline = ScriptableObject.CreateInstance<GameLogic.Defense90.DefenseSkillTimeline>();
            try
            {
                Undo.IncrementCurrentGroup();
                var serialized = new SerializedObject(timeline);
                serialized.FindProperty(nameof(GameLogic.Defense90.DefenseSkillTimeline.SkillDroplets)).intValue = 3;
                serialized.ApplyModifiedProperties();
                Assert.AreEqual(3, timeline.SkillDroplets);
                Undo.PerformUndo();
                Assert.AreEqual(12, timeline.SkillDroplets, "编辑器参数修改应可撤销");
            }
            finally { Object.DestroyImmediate(timeline); }
        }
    }
}
