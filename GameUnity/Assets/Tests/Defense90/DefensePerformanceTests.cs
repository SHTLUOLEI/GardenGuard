using System;
using System.Reflection;
using GameLogic;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Defense90Tests
{
    public sealed class DefensePerformanceTests
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test] public void EnemyDepthMatchesFullSortAcrossCrossingRemovalReuseAndTies()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab"));
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab"));
            var game = ui.GetComponent<DefenseGame>(); var view = go.GetComponent<DefenseBattlefield>();
            try
            {
                var tables = new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_STRESS));
                round.StartPractice(new[] { new Vector2(320, 420) }, false);
                typeof(DefenseGame).GetProperty("Round").SetValue(game, round);
                typeof(DefenseGame).GetProperty("Tier").SetValue(game, tables.TbDefenseVfxTierConfig.Get(1));
                view.Initialize(game); view.ResetRound(round);
                var sort = (Action<DefenseRound>)Delegate.CreateDelegate(typeof(Action<DefenseRound>), view,
                    typeof(DefenseBattlefield).GetMethod("SortEnemies", PRIVATE));
                var renderers = new SpriteRenderer[round.Enemies.Length];
                for (int i = 0; i < renderers.Length; i++) renderers[i] = view.transform.Find("Enemy_" + i).GetComponent<SpriteRenderer>();
                var expected = new System.Collections.Generic.List<int>(); var random = new System.Random(901107);
                for (int frame = 0; frame < 200; frame++)
                {
                    expected.Clear();
                    for (int i = 0; i < round.Enemies.Length; i++)
                    {
                        round.Enemies[i].Active = frame % 17 != 0 && random.Next(4) != 0;
                        round.Enemies[i].Position.y = random.Next(12); // 密集同脚底、跨越、死亡及槽复用。
                        if (round.Enemies[i].Active) expected.Add(i);
                    }
                    expected.Sort((a, b) => {
                        int y = round.Enemies[b].Position.y.CompareTo(round.Enemies[a].Position.y);
                        return y != 0 ? y : a.CompareTo(b);
                    });
                    sort(round);
                    for (int rank = 0; rank < expected.Count; rank++)
                        Assert.AreEqual(100 + rank, renderers[expected[rank]].sortingOrder, $"frame={frame} rank={rank}");
                    if (frame == 99) view.ResetRound(round);
                }
                sort(round); // 预热后稳定快照：不推进规则，不重复创建数组。
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 300; i++) sort(round);
                Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally
            {
                typeof(DefenseBattlefield).GetMethod("OnDestroy", PRIVATE).Invoke(view, null);
                Object.DestroyImmediate(go); Object.DestroyImmediate(ui);
            }
        }

        [Test] public void BarsCoalesceSmallChangesButReachEndpointsAndFollowParentResize()
        {
            var parent = new GameObject("BarSlot", typeof(RectTransform));
            var child = new GameObject("BarFill", typeof(RectTransform));
            try
            {
                var slot = parent.GetComponent<RectTransform>(); var fill = child.GetComponent<RectTransform>();
                fill.SetParent(slot, false); slot.sizeDelta = new Vector2(400, 20); fill.sizeDelta = new Vector2(200, 20);
                var setBar = (Action<RectTransform, float>)Delegate.CreateDelegate(typeof(Action<RectTransform, float>),
                    typeof(DefenseGame).GetMethod("SetBar", BindingFlags.Static | BindingFlags.NonPublic));
                setBar(fill, .5005f); Assert.AreEqual(200, fill.sizeDelta.x);
                setBar(fill, .502f); Assert.That(fill.sizeDelta.x, Is.EqualTo(200.8f).Within(.001f));
                setBar(fill, 1); Assert.AreEqual(400, fill.sizeDelta.x);
                setBar(fill, 0); Assert.AreEqual(0, fill.sizeDelta.x);
                setBar(fill, .001f); Assert.AreEqual(20, fill.sizeDelta.x, "保留圆头最小宽度");
                slot.sizeDelta = new Vector2(800, 20); setBar(fill, .5f); Assert.AreEqual(400, fill.sizeDelta.x);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }

        [Test] public void HealthLabelReusesTextUntilDisplayedValuesChange()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab"));
            try
            {
                var game = ui.GetComponent<DefenseGame>();
                var tables = new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_NORMAL)); round.Start();
                typeof(DefenseGame).GetProperty("Round").SetValue(game, round);
                var refresh = (Action)Delegate.CreateDelegate(typeof(Action), game, typeof(DefenseGame).GetMethod("RefreshInterface", PRIVATE));
                var label = (UnityEngine.UI.Text)typeof(DefenseGame).GetField("m_healthText", PRIVATE).GetValue(game);
                refresh(); string initial = label.text;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 300; i++) refresh();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, allocated, "生命值和整秒不变时不创建新的 HUD 字符串");
                Assert.AreSame(initial, label.text);
                typeof(DefenseRound).GetProperty("Health").SetValue(round, round.Health - 1);
                refresh(); Assert.AreNotEqual(initial, label.text);
                round.Start(); refresh(); Assert.AreEqual(initial, label.text, "重开仍恢复完整血量文字");
            }
            finally { Object.DestroyImmediate(ui); }
        }

        [Test] public void RepeatedNormalHitsKeepMovingAndSkillKeepsShortHitStop()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab"));
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab"));
            var game = ui.GetComponent<DefenseGame>(); var view = go.GetComponent<DefenseBattlefield>();
            Material splash = null;
            try
            {
                var tables = new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_STRESS));
                round.StartPractice(new[] { new Vector2(320, 420) }, false);
                typeof(DefenseGame).GetProperty("Round").SetValue(game, round);
                typeof(DefenseGame).GetProperty("Tier").SetValue(game, tables.TbDefenseVfxTierConfig.Get(1));
                view.Initialize(game); view.ResetRound(round);
                int callbacks = 0; view.ImpactShown += (dead, tank) => callbacks++;
                var enemy = view.transform.Find("Enemy_0");
                for (int frame = 0; frame < 120; frame++)
                {
                    for (int i = 0; i < 64; i++) round.Impacts[i].Life = Mathf.Max(0, round.Impacts[i].Life - 1f / 60);
                    if (frame % 2 == 0) round.Impacts[frame / 2] = new DefenseImpact {
                        Position = round.Enemies[0].Position, Life = DefenseRound.IMPACT_LIFE, Destroyed = true };
                    round.Enemies[0].Position.x++;
                    view.Refresh(1f / 60, Vector2.zero);
                    Assert.IsFalse(view.HitStopped, "普通击杀不应反复冻结战场");
                    Assert.That(enemy.position.x, Is.EqualTo(view.WorldPoint(round.Enemies[0].Position).x).Within(.0001f));
                }
                Assert.AreEqual(57, callbacks, "水弹仍在原定抵达时间触发音效和金币");
                round.Impacts[63] = new DefenseImpact { Position = round.Enemies[0].Position,
                    Life = DefenseRound.IMPACT_LIFE, Skill = true };
                view.Refresh(0, Vector2.zero); Assert.IsTrue(view.HitStopped);
                view.Refresh(view.Timeline.HitStop + .001f, Vector2.zero); Assert.IsFalse(view.HitStopped);

                // 比较存活水花，已到期槽保留的最后一帧颜色不代表当前动画进度。
                round.Impacts[0] = new DefenseImpact { Position = round.Enemies[0].Position, Life = .4f, Skill = true };
                round.Impacts[1] = new DefenseImpact { Position = round.Enemies[0].Position, Life = .2f, Skill = true };
                view.Refresh(0, Vector2.zero);

                var first = view.transform.Find("Splash_0").GetComponent<SpriteRenderer>();
                var second = view.transform.Find("Splash_1").GetComponent<SpriteRenderer>();
                splash = first.sharedMaterial;
                Assert.AreSame(splash, second.sharedMaterial);
                Assert.AreNotSame(view.WaterMaterial, splash);
                Assert.AreEqual(-1, splash.GetFloat("_Progress"));
                Assert.AreEqual(2, splash.GetFloat("_Mode"));
                Assert.AreNotEqual(first.color.a, second.color.a, "每个水花仍有独立进度");
                var block = new MaterialPropertyBlock(); first.GetPropertyBlock(block);
                Assert.AreEqual(0, block.GetFloat("_Mode"), "不再逐水花提交水参数属性块");
                typeof(DefenseGame).GetProperty("Tier").SetValue(game, tables.TbDefenseVfxTierConfig.Get(2));
                view.Refresh(0, Vector2.zero);
                Assert.AreEqual(game.Tier.ShaderDetail ? 1 : 0, splash.GetFloat("_Detail"), "设置切换立即更新共享材质");
                view.ResetRound(round); Assert.IsFalse(view.HitStopped);
            }
            finally
            {
                typeof(DefenseBattlefield).GetMethod("OnDestroy", PRIVATE).Invoke(view, null);
                Object.DestroyImmediate(go); Object.DestroyImmediate(ui);
            }
            Assert.IsTrue(splash == null, "离开战场释放共享水花材质");
        }

        [Test] public void ScoreFeedbackReusesClockRestoresScaleAndNumbersWarmBeforeCombat()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab"));
            var game = ui.GetComponent<DefenseGame>();
            try
            {
                var tables = new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_STRESS)); round.Start();
                typeof(DefenseGame).GetProperty("Round").SetValue(game, round);
                var fill = (RectTransform)typeof(DefenseGame).GetField("m_healthFill", PRIVATE).GetValue(game);
                typeof(DefenseGame).GetField("m_healthFillImage", PRIVATE).SetValue(game, fill.GetComponent<UnityEngine.UI.Image>());
                typeof(DefenseGame).GetField("m_lastHealth", PRIVATE).SetValue(game, round.Health);
                typeof(DefenseGame).GetField("m_wasReady", PRIVATE).SetValue(game, true);
                var score = (UnityEngine.UI.Text)typeof(DefenseGame).GetField("m_scoreText", PRIVATE).GetValue(game);
                Vector3 authored = new Vector3(1.2f, .8f, 1);
                typeof(DefenseGame).GetField("m_scoreHomeScale", PRIVATE).SetValue(game, authored);
                var hud = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), game, typeof(DefenseGame).GetMethod("UpdateHud", PRIVATE));
                Type tweenType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((tweenType = assembly.GetType("DG.Tweening.DOTween")) != null) break;
                Assert.NotNull(tweenType);
                var isTweening = tweenType.GetMethod("IsTweening", new[] { typeof(object), typeof(bool) });
                for (int i = 1; i <= 600; i++)
                {
                    typeof(DefenseRound).GetProperty("Score").SetValue(round, i);
                    hud(1f / 60);
                    Assert.IsFalse((bool)isTweening.Invoke(null, new object[] { score.transform, false }), "击杀分数反馈不创建Tween");
                }
                Assert.AreNotEqual(authored, score.transform.localScale);
                hud(.3f); Assert.AreEqual(authored, score.transform.localScale);
                ui.GetComponentInChildren<DefenseFloatTextView>(true).Warmup();
                string cached = DefenseNumbers.GetKill(42); DefenseNumbers.Warmup();
                Assert.AreSame(cached, DefenseNumbers.GetKill(42));
                Assert.AreEqual("999!", DefenseNumbers.GetKill(999));
            }
            finally
            {
                DefenseTween.Kill(ui.GetComponentsInChildren<Transform>(true));
                Object.DestroyImmediate(ui);
            }
        }

        [Test] public void VertexSplashMatchesUniformShaderAcrossProgressAndDetail()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab").GetComponent<DefenseBattlefield>();
            var cameraObject = new GameObject("SplashShaderCamera"); var spriteObject = new GameObject("SplashShaderSprite");
            var material = new Material(prefab.WaterMaterial);
            var target = new RenderTexture(96, 96, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(96, 96, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 1.2f; camera.aspect = 1;
                camera.transform.position = new Vector3(10000, 10000, -10); camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.targetTexture = target;
                spriteObject.layer = 31; spriteObject.transform.position = new Vector3(10000, 10000, 0);
                var renderer = spriteObject.AddComponent<SpriteRenderer>(); renderer.sprite = prefab.WhiteSprite;
                renderer.sharedMaterial = material; spriteObject.transform.localScale = Vector3.one * 2 / prefab.WhiteSprite.bounds.size.x;
                material.SetFloat("_Mode", 2); material.SetColor("_Water", prefab.Timeline.Water); material.SetColor("_Foam", prefab.Timeline.Foam);
                Color32[] Render()
                {
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); pixels.Apply(); return pixels.GetPixels32();
                }
                foreach (int detail in new[] { 0, 1 }) foreach (float progress in new[] { 0f, .1f, .5f, .9f, 1f })
                {
                    material.SetFloat("_Detail", detail); material.SetFloat("_Progress", progress); material.SetFloat("_Opacity", 1 - progress);
                    renderer.color = Color.white; Color32[] reference = Render();
                    material.SetFloat("_Progress", -1); material.SetFloat("_Opacity", 1);
                    renderer.color = new Color(1, 1, 1, progress); Color32[] batched = Render();
                    long difference = 0, referenceEnergy = 0;
                    for (int i = 0; i < reference.Length; i++)
                    {
                        difference += Mathf.Abs(reference[i].r - batched[i].r) + Mathf.Abs(reference[i].g - batched[i].g)
                            + Mathf.Abs(reference[i].b - batched[i].b) + Mathf.Abs(reference[i].a - batched[i].a);
                        referenceEnergy += reference[i].r + reference[i].g + reference[i].b;
                    }
                    Assert.Less(difference / (float)(reference.Length * 4 * 255), .01f, "8bit顶点进度量化后的平均像素误差应小于1%");
                    if (progress < 1) Assert.Greater(referenceEnergy, 1000, "必须实际渲染水花，禁止全透明假通过");
                }
                Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader));
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(spriteObject);
                Object.DestroyImmediate(material); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        [Test] public void EnemyPoolPreservesSpawnDamageHideReuseAndStableDepth()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab"));
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab"));
            var game = ui.GetComponent<DefenseGame>();
            var view = go.GetComponent<DefenseBattlefield>();
            Material shadowMaterial = null, dropMaterial = null;
            try
            {
                var tables = new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_STRESS));
                round.StartPractice(new[] { new Vector2(320, 420), new Vector2(320, 420) }, false);
                typeof(DefenseGame).GetProperty("Round").SetValue(game, round);
                typeof(DefenseGame).GetProperty("Tier").SetValue(game, tables.TbDefenseVfxTierConfig.Get(1));
                view.Initialize(game); view.ResetRound(round); view.Refresh(0, Vector2.zero);
                SpriteRenderer Renderer(string name) => view.transform.Find(name).GetComponent<SpriteRenderer>();
                var body = Renderer("Enemy_0"); var shadow = Renderer("Shadow_0"); var health = Renderer("Health_0");
                shadowMaterial = shadow.sharedMaterial; dropMaterial = Renderer("WaterShot_0").sharedMaterial;
                Assert.IsTrue(body.enabled); Assert.IsTrue(shadow.enabled); Assert.IsFalse(health.enabled);
                Assert.AreEqual(view.RunnerSprite, body.sprite);
                Assert.AreSame(shadowMaterial, Renderer("Shadow_1").sharedMaterial);
                Assert.AreNotSame(view.WaterMaterial, shadowMaterial, "不修改源资源材质");
                var block = new MaterialPropertyBlock(); shadow.GetPropertyBlock(block);
                Assert.AreEqual(0, block.GetFloat(Shader.PropertyToID("_Mode")), "SpriteRenderer可有内部属性块，水参数应保存在共享材质");
                Assert.AreEqual(3, shadowMaterial.GetFloat("_Mode"));
                int stableOrder = body.sortingOrder;
                view.Refresh(.1f, Vector2.zero); Assert.AreEqual(stableOrder, body.sortingOrder);

                round.Enemies[0].Tank = true; round.Enemies[0].Flash = .16f;
                round.Enemies[0].Health *= .5f; round.Enemies[0].Position.y++;
                view.Refresh(0, Vector2.zero);
                Assert.AreEqual(view.TankSprite, body.sprite); Assert.IsTrue(health.enabled);
                Assert.AreEqual(new Color(.6f, .9f, 1), body.color);
                Assert.Less(body.sortingOrder, Renderer("Enemy_1").sortingOrder);
                Assert.That(body.transform.localScale.x,
                    Is.EqualTo(round.Rules.Tank.Size * .015f / view.TankSprite.bounds.size.x).Within(.0001f));

                round.Enemies[0].Active = false; view.Refresh(0, Vector2.zero);
                Assert.IsFalse(body.enabled); Assert.IsFalse(shadow.enabled); Assert.IsFalse(health.enabled);
                round.Enemies[0].Active = true; round.Enemies[0].Tank = false; round.Enemies[0].Flash = 0;
                round.Enemies[0].Health = round.Enemies[0].MaxHealth;
                view.Refresh(0, Vector2.zero);
                Assert.IsTrue(body.enabled); Assert.AreEqual(view.RunnerSprite, body.sprite);
                Assert.AreEqual(Color.white, body.color); Assert.IsFalse(health.enabled);
                view.ResetRound(round); Assert.IsFalse(body.enabled); view.Refresh(0, Vector2.zero);
                Assert.IsTrue(body.enabled); Assert.AreSame(shadowMaterial, shadow.sharedMaterial);
            }
            // EditMode 中普通 MonoBehaviour 不执行运行时销毁回调，显式验证清理入口。
            finally
            {
                typeof(DefenseBattlefield).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                Object.DestroyImmediate(go); Object.DestroyImmediate(ui);
            }
            Assert.IsTrue(shadowMaterial == null, "销毁战场时释放运行时阴影材质");
            Assert.IsTrue(dropMaterial == null, "销毁战场时释放运行时水弹材质");
        }
    }
}
