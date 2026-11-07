using GameLogic;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Defense90Tests
{
    public sealed class DefenseSceneTests
    {
        private sealed class LifecycleWindow : UIWindow
        {
            public int CreateCount, DestroyCount;
            public bool FailBinding;
            protected override bool NeedTweenPop => false;
            protected override ModelType GetModelType() => ModelType.NoneType;
            protected override void ScriptGenerator()
            {
                if (FailBinding) throw new System.InvalidOperationException("binding failed");
            }
            protected override void OnCreate() => CreateCount++;
            protected override void OnDestroy() => DestroyCount++;
            public void Release() => Destroy();
        }

        private static void CreateWindow(LifecycleWindow window)
            => typeof(UIWindow).GetMethod("InternalCreate", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(window, null);

        [Test] public void UncreatedWindowClosesWithoutCallingBusinessCleanup()
        {
            var window = new LifecycleWindow();
            window.Initialize("LifecycleWindow", "MissingPrefab");
            window.Release();
            window.Release();
            Assert.AreEqual(0, window.CreateCount);
            Assert.AreEqual(0, window.DestroyCount);
        }

        [Test] public void CreatedWindowCallsBusinessCleanupExactlyOnce()
        {
            var window = new LifecycleWindow();
            CreateWindow(window);
            window.Release();
            window.Release();
            Assert.AreEqual(1, window.CreateCount);
            Assert.AreEqual(1, window.DestroyCount);
        }

        [Test] public void PartiallyCreatedWindowStillCallsBusinessCleanup()
        {
            var window = new LifecycleWindow { FailBinding = true };
            Assert.Throws<System.Reflection.TargetInvocationException>(() => CreateWindow(window));
            window.Release();
            Assert.AreEqual(0, window.CreateCount);
            Assert.AreEqual(1, window.DestroyCount);
        }

        [Test] public void FiveWindowPrefabsExistWithSafeAreaAndLayout()
        {
            foreach (string name in new[] { "DefenseHomeWindow", "DefenseWindow", "DefensePauseWindow", "DefenseResultWindow", "DefenseSettingsWindow" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/BundleAssets/UI/Defense/{name}.prefab");
                Assert.NotNull(prefab, name);
                Assert.NotNull(prefab.GetComponent<Canvas>(), name + " Canvas");
                Assert.NotNull(prefab.transform.Find("m_rectSafe"), name + " m_rectSafe");
                Assert.NotNull(prefab.GetComponent<DefenseLayout>(), name + " DefenseLayout");
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(LayerMask.NameToLayer("UI"), child.gameObject.layer, name + "/" + child.name);
                foreach (string dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab)))
                    Assert.IsFalse(dependency.EndsWith(".unity"), dependency);
            }
        }

        [Test] public void BattlePrefabHasNoLegacyScreensAndAllBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/UI/Defense/DefenseWindow.prefab");
            foreach (string legacy in new[] { "m_goMenu", "m_goPause", "m_goResult" })
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.AreNotEqual(legacy, child.name, "旧界面应已拆出");
            var serialized = new SerializedObject(prefab.GetComponent<DefenseGame>());
            var it = serialized.GetIterator(); it.NextVisible(true);
            while (it.NextVisible(false))
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                    Assert.NotNull(it.objectReferenceValue, it.name);
            Assert.IsNull(prefab.transform.Find("m_rimgArena"));
            var world = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab");
            Assert.NotNull(world);
            Assert.IsNull(world.GetComponentInChildren<Canvas>(true));
            var view = world.GetComponent<DefenseBattlefield>();
            Assert.IsTrue(view.WorldCamera.orthographic);
            Assert.NotNull(view.TurretSprite); Assert.NotNull(view.RunnerSprite); Assert.NotNull(view.TankSprite);
            Assert.NotNull(view.Skill.Timeline);
            Assert.AreEqual("Defense90/WorldWater", view.WaterMaterial.shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(view.WaterMaterial.shader));
            Assert.AreEqual("Defense90/EffectSprite", view.EffectMaterial.shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(view.EffectMaterial.shader));
        }

        [Test] public void WaterTimelineUsesRuleBoundariesAndEndsCleanly()
        {
            var rules = DefenseConfigMgr.CreateRules(LoadTables(), DefenseConfigMgr.MODE_NORMAL);
            float t = rules.TelegraphDuration, r = rules.ReleaseDuration, h = rules.HitDuration;
            Assert.AreEqual(-1, DefenseSkillTimeline.Evaluate(-1, t, r, h).x);
            Assert.AreEqual(0, DefenseSkillTimeline.Evaluate(t * .5f, t, r, h).x);
            Assert.AreEqual(1, DefenseSkillTimeline.Evaluate(t, t, r, h).x);
            Assert.AreEqual(2, DefenseSkillTimeline.Evaluate(t + r, t, r, h).x);
            Assert.AreEqual(-1, DefenseSkillTimeline.Evaluate(t + r + h, t, r, h).x);
        }

        [Test] public void AuthoredWorldTransformsDefineCoordinatesWithoutBeingOverwritten()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<DefenseBattlefield>();
                instance.transform.position = new Vector3(12, -4, 0);
                instance.transform.localScale = Vector3.one * 1.3f;
                view.Turret.transform.localPosition = new Vector3(2.1f, -.3f, 0);
                view.Turret.transform.localScale = new Vector3(2.5f, 2.1f, 1);
                view.Background.transform.localPosition = new Vector3(1, 6, 2);
                view.Background.transform.localScale = Vector3.one * 8.2f;
                view.WorldCamera.orthographicSize = 5.7f;
                view.Initialize(null);
                var round = new DefenseRound(DefenseConfigMgr.CreateRules(LoadTables(), DefenseConfigMgr.MODE_NORMAL));
                for (int i = 0; i < 3; i++) view.UpdateBounds(round);
                Assert.AreEqual(new Vector3(1, 6, 2), view.Background.transform.localPosition);
                Assert.AreEqual(Vector3.one * 8.2f, view.Background.transform.localScale);
                Assert.AreEqual(new Vector3(2.5f, 2.1f, 1), view.Turret.transform.localScale);
                Assert.AreEqual(5.7f, view.WorldCamera.orthographicSize);
                Assert.Less((view.WorldPoint(DefenseRound.BASE_POSITION) - view.Turret.transform.position).magnitude, .0001f);
                Vector2 point = new Vector2(180, 430);
                Assert.Less((view.RulePoint(view.WorldPoint(point)) - point).magnitude, .001f);
                Assert.Greater(round.SpawnY, round.VisibleTop);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test] public void PresentationAssetsAreCompleteAndCollected()
        {
            var assets = AssetDatabase.LoadAssetAtPath<DefenseAssets>("Assets/BundleAssets/Defense90/DefenseAssets.asset");
            Assert.NotNull(assets);
            Assert.NotNull(assets.PulseMaterial); Assert.NotNull(assets.ShotSound);
            Assert.NotNull(assets.PulseSound); Assert.NotNull(assets.VictorySound); Assert.NotNull(assets.DefeatSound);
            Assert.AreEqual("Defense90/OrbitalPulse", assets.PulseMaterial.shader.name);
            Assert.IsTrue(assets.PulseMaterial.shader.isSupported);
            var tables = LoadTables();
            Assert.AreEqual(90, tables.TbDefenseModeConfig.Get(DefenseConfigMgr.MODE_NORMAL).Duration);
            Assert.AreEqual(8, tables.TbDefenseModeConfig.Get(DefenseConfigMgr.MODE_STRESS).SpawnBatch);
            Assert.AreEqual(2, tables.TbDefenseEnemyConfig.dataList.Count);
            Assert.AreEqual(2, tables.TbDefenseVfxTierConfig.dataList.Count);
        }

        private static GameProto.Tables LoadTables() =>
            new GameProto.Tables(file => new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));

        [Test] public void StandardConfigurationWinsWithDeliberateSkillUseAndLosesWithoutIt()
        {
            DefenseRules rules = DefenseConfigMgr.CreateRules(LoadTables(), DefenseConfigMgr.MODE_NORMAL);
            var passive = new DefenseRound(rules); passive.Start(); passive.Tick(91);
            Assert.AreEqual(EDefenseState.Lost, passive.State, "Inactive player should experience failure.");
            for (int run = 0; run < 3; run++)
            {
                var active = new DefenseRound(rules); active.Start();
                for (int step = 0; step < 901 && active.State == EDefenseState.Playing; step++)
                {
                    if (active.Cooldown <= 0 && active.ActiveCount >= 5)
                    {
                        Vector2 best = new Vector2(320, 340); int maxCount = 0;
                        for (int i = 0; i < active.Enemies.Length; i++)
                        {
                            if (!active.Enemies[i].Active) continue;
                            Vector2 center = active.Enemies[i].Position; int count = 0;
                            for (int j = 0; j < active.Enemies.Length; j++)
                                if (active.Enemies[j].Active && (active.Enemies[j].Position - center).sqrMagnitude <= rules.SkillRadius * rules.SkillRadius)
                                    count++;
                            if (count > maxCount) { maxCount = count; best = center; }
                        }
                        active.Cast(best);
                    }
                    active.Tick(0.1f);
                }
                Assert.AreEqual(EDefenseState.Won, active.State, $"Round {run + 1}: elapsed={active.Elapsed}, score={active.Score}");
            }
        }
    }
}
