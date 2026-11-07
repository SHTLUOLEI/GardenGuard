using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using GameLogic;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEngine;

namespace Defense90Tests
{
    public sealed class DefenseRoundTests
    {
        // 每个测试都新建一份规则，测试内修改字段后再构造 DefenseRound。
        private DefenseRules m_rules;
        [SetUp] public void SetUp()
        {
            var tables = new GameProto.Tables(file => new Luban.ByteBuf(File.ReadAllBytes("Assets/BundleAssets/Configs/Binary/" + file + ".bytes")));
            m_rules = DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_NORMAL);
        }

        [Test] public void TwoEnemyTypesHaveDifferentHealthSpeedAndDamage()
        {
            Assert.Less(m_rules.Runner.Health, m_rules.Tank.Health);
            Assert.Greater(m_rules.Runner.Speed, m_rules.Tank.Speed);
            Assert.Less(m_rules.Runner.Attack, m_rules.Tank.Attack);
        }

        [Test] public void SurvivingToDeadlineWins()
        {
            m_rules.Runner.Attack = m_rules.Tank.Attack = 0;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(91);
            Assert.AreEqual(EDefenseState.Won, round.State);
            Assert.AreEqual(90, round.Elapsed, 0.001f);
        }

        [Test] public void BaseHealthZeroLosesBeforeDeadline()
        {
            m_rules.BaseHealth = 1; m_rules.ShotRange = 1;
            m_rules.Runner.Speed = 10000;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(2);
            Assert.AreEqual(EDefenseState.Lost, round.State); Assert.AreEqual(0, round.Health);
        }

        [Test] public void PauseFreezesClockEnemiesAndSkillAndRequiresExplicitResume()
        {
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(1);
            round.Cast(new Vector2(320, 620)); round.Pause();
            float elapsed = round.Elapsed, cooldown = round.Cooldown, age = round.SkillAge;
            float impactLife = round.Impacts[0].Life;
            int spawned = round.Spawned;
            round.Tick(100);
            Assert.AreEqual(elapsed, round.Elapsed); Assert.AreEqual(cooldown, round.Cooldown);
            Assert.AreEqual(age, round.SkillAge); Assert.AreEqual(spawned, round.Spawned);
            Assert.AreEqual(impactLife, round.Impacts[0].Life);
            Assert.IsFalse(round.Cast(Vector2.zero));
            round.Resume(); round.Tick(0.1f); Assert.Greater(round.Elapsed, elapsed);
        }

        [Test] public void SkillDamagesOnlyOnceAfterTelegraphAndHonorsCooldown()
        {
            m_rules.Runner.Speed = m_rules.Tank.Speed = 0;
            m_rules.ShotRange = 1; m_rules.SpawnInterval = 100; m_rules.SpawnBatch = 1;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(0.3f);
            // 手动把敌人移到可视区域内(Y <= VisibleTop),否则技能不会打它。
            round.Enemies[0].Position = new Vector2(round.Enemies[0].Position.x, round.VisibleTop - 10);
            Vector2 point = round.Enemies[0].Position;
            Assert.IsTrue(round.Cast(point)); Assert.IsFalse(round.Cast(point));
            round.Tick(m_rules.TelegraphDuration * 0.5f); Assert.AreEqual(1, round.ActiveCount);
            round.Tick(m_rules.TelegraphDuration); Assert.AreEqual(0, round.ActiveCount);
            Assert.AreEqual(1, round.SkillHits); Assert.AreEqual(1, round.SkillKills);
            int score = round.Score;
            round.Tick(1); Assert.AreEqual(score, round.Score); Assert.IsFalse(round.Cast(point));
            round.Tick(m_rules.SkillCooldown); Assert.IsTrue(round.Cast(point));
        }

        [Test] public void EnemiesSpawnAtSpawnYAboveScreenAndEnterTheArena()
        {
            m_rules.SpawnBatch = 1;
            var round = new DefenseRound(m_rules) { SpawnY = 1400, VisibleTop = 1300 };
            round.Start(); round.Tick(0.3f);
            int index = Array.FindIndex(round.Enemies, e => e.Active);
            Assert.GreaterOrEqual(index, 0, "应已刷出敌人");
            Assert.That(round.Enemies[index].Position.y, Is.InRange(1380f, 1430f), "起始高度应在 SpawnY 到 SpawnY+30 之间");
            Assert.AreEqual(-1, round.FindTarget(), "刷在可视区域之外的怪不能被攻击");
            for (int i = 0; i < 400 && round.Enemies[index].Position.y > DefenseRound.ARENA_SIZE.y; i++) round.Tick(0.1f);
            Assert.LessOrEqual(round.Enemies[index].Position.y, DefenseRound.ARENA_SIZE.y, "敌人应移动进入战场");
        }

        [Test] public void OffscreenEnemiesAreIgnoredByShotAndSkill()
        {
            m_rules.SpawnBatch = 1;
            var round = new DefenseRound(m_rules) { SpawnY = 2000, VisibleTop = 1000 };
            round.Start(); round.Tick(0.3f);
            int index = Array.FindIndex(round.Enemies, e => e.Active);
            round.Enemies[index].Position = new Vector2(320, 1500);
            round.Cast(new Vector2(320, 1500)); round.Tick(1f);
            Assert.AreEqual(0, round.SkillHits, "技能不作用于可视区域之外的怪");
            Assert.AreEqual(0, round.ShotsFired);
        }

        [Test] public void KilledEnemyLeavesDeathAnimationSlot()
        {
            m_rules.ShotDamage = 100000;
            var round = new DefenseRound(m_rules); round.Start();
            for (int i = 0; i < 3000 && round.Kills == 0; i++) round.Tick(1f / 30f);
            Assert.Greater(round.Kills, 0);
            Assert.IsTrue(Array.Exists(round.Deaths, d => d.Life > 0), "击杀当帧应留下死亡动画");
        }

        [Test] public void OutsideRadiusSurvivesAndTargetCoordinatesClamp()
        {
            m_rules.Runner.Speed = 0; m_rules.ShotRange = 1; m_rules.SpawnInterval = 100; m_rules.SpawnBatch = 1;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(0.3f);
            Assert.IsTrue(round.Cast(new Vector2(-100, -100)));
            Assert.AreEqual(Vector2.zero, round.SkillPosition);
            round.Tick(0.6f); Assert.AreEqual(1, round.ActiveCount);
        }

        [Test] public void RestartClearsEveryRoundScopedStateAcrossThreeRounds()
        {
            m_rules.Duration = 2; m_rules.Runner.Attack = m_rules.Tank.Attack = 0;
            var round = new DefenseRound(m_rules);
            for (int i = 0; i < 3; i++)
            {
                round.Start(); Assert.AreEqual(0, round.Score); Assert.AreEqual(0, round.Spawned);
                Assert.AreEqual(0, round.Cooldown); Assert.AreEqual(-1, round.SkillAge);
                Assert.AreEqual(0, round.ShotsFired); Assert.AreEqual(0, round.SkillKills);
                foreach (DefenseImpact impact in round.Impacts) Assert.AreEqual(0, impact.Life);
                Assert.AreEqual(m_rules.BaseHealth, round.Health);
                round.Tick(3); Assert.AreEqual(EDefenseState.Won, round.State);
            }
        }

        [Test] public void EndedRoundRejectsFurtherTimeAndSkill()
        {
            m_rules.Duration = 1;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(2);
            int score = round.Score; round.Tick(100);
            Assert.AreEqual(1, round.Elapsed); Assert.AreEqual(score, round.Score);
            Assert.IsFalse(round.Cast(Vector2.one));
        }

        [Test] public void StressNeverExceedsFixedCapacity()
        {
            m_rules.Capacity = 32; m_rules.SpawnBatch = 12; m_rules.SpawnInterval = 0.05f;
            m_rules.Runner.Speed = m_rules.Tank.Speed = 0; m_rules.ShotRange = 1;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(10);
            Assert.AreEqual(32, round.ActiveCount); Assert.AreEqual(32, round.Spawned);
        }

        [Test] public void SameSeedAndInputsProduceSameRound()
        {
            var a = new DefenseRound(m_rules); var b = new DefenseRound(m_rules);
            a.Start(); b.Start(); a.Tick(30); b.Tick(30);
            Assert.AreEqual(a.Health, b.Health); Assert.AreEqual(a.Score, b.Score);
            for (int i = 0; i < a.Enemies.Length; i++) Assert.AreEqual(a.Enemies[i].Position, b.Enemies[i].Position);
        }

        [Test] public void OptimizedTargetMatchesBaselineForEverySnapshotAndTie()
        {
            var round = new DefenseRound(m_rules); round.Start();
            for (int i = 0; i < 500; i++)
            {
                round.Tick(0.1f); Assert.AreEqual(round.FindTargetAllocatingBaseline(), round.FindTarget());
            }
            for (int i = 0; i < round.Enemies.Length; i++) round.Enemies[i].Active = false;
            round.Enemies[0] = new DefenseEnemy { Active = true, Position = new Vector2(100, 100) };
            round.Enemies[1] = round.Enemies[0];
            Assert.AreEqual(1, round.FindTarget()); Assert.AreEqual(1, round.FindTargetAllocatingBaseline());
        }

        [Test] public void TargetSearchBenchmarkWritesComparableMeasurements()
        {
            m_rules.Capacity = 512; m_rules.SpawnBatch = 8; m_rules.SpawnInterval = 0.05f;
            m_rules.Runner.Speed = m_rules.Tank.Speed = 0; m_rules.ShotRange = 1000;
            var round = new DefenseRound(m_rules); round.Start(); round.Tick(20);
            // Fill an identical stable stress snapshot for both variants.
            for (int i = 0; i < round.Enemies.Length; i++)
                round.Enemies[i] = new DefenseEnemy { Active = true, Position = new Vector2(20 + i % 30 * 20, 180 + i / 30 * 20) };
            Func<int> baseline = round.FindTargetAllocatingBaseline, modified = round.FindTarget;
            for (int i = 0; i < 100; i++) { baseline(); modified(); }
            var text = new StringBuilder("Defense90 target selection microbenchmark\nUnity Editor only, not device/frame-time evidence.\n");
            text.AppendLine($"Unity={Application.unityVersion}; CPU={SystemInfo.processorType}; snapshot=512; iterations=3000; warmup=100");
            Measure("BASELINE", baseline, text); Measure("MODIFIED", modified, text);
            text.AppendLine("Input: same fixed 512 enemies, same range and nearest/tie semantics; no quality/resolution/content changes.");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../GameRelease/Defense90"));
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "benchmark.txt"), text.ToString());
            TestContext.Out.Write(text.ToString());
        }

        private static void Measure(string name, Func<int> find, StringBuilder result)
        {
            const int iterations = 3000;
            var times = new double[iterations];
            var watch = new Stopwatch();
            int checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                watch.Restart(); checksum += find(); watch.Stop();
                times[i] = watch.ElapsedTicks * 1000000.0 / Stopwatch.Frequency;
            }
            double sum = 0; for (int i = 0; i < times.Length; i++) sum += times[i];
            Array.Sort(times);
            result.AppendLine($"{name}: mean_us={sum / iterations:F3}; p95_us={times[(int)(iterations * 0.95)]:F3}; checksum={checksum}; result=PASS");
            result.AppendLine("GC.Alloc is not measured by this synchronous microbenchmark; use device Profiler for allocation/frame metrics.");
        }
    }
}
