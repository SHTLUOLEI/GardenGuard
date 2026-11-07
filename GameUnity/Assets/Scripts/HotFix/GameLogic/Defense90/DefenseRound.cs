using System;
using System.Linq;
using GameProto;
using UnityEngine;

namespace GameLogic.Defense90
{
    public enum EDefenseState { Ready, Playing, Paused, Won, Lost }

    // 一局所需的全部数值，由 Luban 的模式/敌人/技能三张表组合而成；测试可直接改字段构造极端局面。
    public sealed class DefenseRules
    {
        public const float MELEE_RANGE = 52;

        public struct EnemySpec
        {
            public string Name;
            public float Health, Speed, Attack, AttackInterval, AttackRange, Size;
            public int Score;
            public bool Ranged => AttackRange > MELEE_RANGE + 1;
        }

        public float Duration, BaseHealth, SpawnInterval, SpawnIntervalEndScale, ShotInterval, ShotDamage, ShotRange;
        public float SkillCooldown, SkillRadius, SkillDamage, TelegraphDuration, ReleaseDuration, HitDuration;
        public float SkillIntensity, SkillShake;
        public Color SkillColor;
        public string SkillName;
        public int SpawnBatch, Capacity, Seed, TankEvery;
        public EnemySpec Runner, Tank;

        public DefenseRules(DefenseModeConfig mode, DefenseEnemyConfig runner, DefenseEnemyConfig tank, DefenseSkillConfig skill)
        {
            Duration = Mathf.Max(1, mode.Duration);
            BaseHealth = Mathf.Max(1, mode.BaseHealth);
            SpawnInterval = Mathf.Max(0.05f, mode.SpawnInterval);
            SpawnIntervalEndScale = Mathf.Clamp(mode.SpawnIntervalEndScale, 0.1f, 1);
            SpawnBatch = Mathf.Clamp(mode.SpawnBatch, 1, 12);
            Capacity = Mathf.Clamp(mode.Capacity, 16, 1024);
            Seed = mode.Seed;
            TankEvery = Mathf.Max(1, mode.TankEvery);
            ShotInterval = Mathf.Max(0.02f, mode.ShotInterval);
            ShotDamage = Mathf.Max(1, mode.ShotDamage);
            ShotRange = Mathf.Max(1, mode.ShotRange);
            Runner = Spec(runner); Tank = Spec(tank);
            SkillName = skill.Name;
            SkillCooldown = Mathf.Max(0, skill.Cooldown);
            SkillRadius = Mathf.Max(1, skill.Radius);
            SkillDamage = Mathf.Max(1, skill.Damage);
            TelegraphDuration = Mathf.Max(0.01f, skill.TelegraphDuration);
            ReleaseDuration = Mathf.Max(0.01f, skill.ReleaseDuration);
            HitDuration = Mathf.Max(0.01f, skill.HitDuration);
            SkillColor = skill.Color;
            SkillIntensity = Mathf.Max(0, skill.Intensity);
            SkillShake = Mathf.Max(0, skill.Shake);
        }

        private static EnemySpec Spec(DefenseEnemyConfig config) => new EnemySpec
        {
            Name = config.Name, Health = Mathf.Max(1, config.Health), Speed = Mathf.Max(0, config.Speed),
            Attack = Mathf.Max(0, config.Attack), AttackInterval = Mathf.Max(0.05f, config.AttackInterval),
            AttackRange = Mathf.Max(MELEE_RANGE, config.AttackRange), Size = Mathf.Max(8, config.Size), Score = config.Score,
        };
    }

    public struct DefenseEnemy
    {
        public bool Active, Tank;
        public Vector2 Position;
        public float Health, MaxHealth, AttackTimer, Flash, Windup, Age, Knock;
    }

    public struct DefenseImpact
    {
        public Vector2 Position;
        public float Life;
        public bool Destroyed, Tank, Skill;
    }

    // 击杀后的压扁消失动画，仅供表现层使用。
    public struct DefenseDeath
    {
        public Vector2 Position;
        public float Life;
        public bool Tank;
    }

    // 重甲的远程水弹：飞到基地才结算伤害，玩家能看到并用技能提前清掉射手。
    public struct DefenseProjectile
    {
        public bool Active;
        public Vector2 Origin, Position;
        public float Damage;
    }

    public struct DefenseFloatText
    {
        public Vector2 Position;
        public int Value;
        public float Life;
        public bool Kill, Skill;
    }

    // Fixed slots keep enemy state stable during damage, rendering and target selection.
    public sealed class DefenseRound
    {
        public static readonly Vector2 BASE_POSITION = new Vector2(320, 65);
        public static readonly Vector2 ARENA_SIZE = new Vector2(640, 680);
        public const float IMPACT_LIFE = 0.55f, TEXT_LIFE = 0.8f, PROJECTILE_SPEED = 240, COMBO_WINDOW = 1.6f, DEATH_LIFE = 0.15f, KNOCK_LIFE = 0.1f;
        public readonly DefenseRules Rules;
        public readonly DefenseEnemy[] Enemies;
        public readonly DefenseImpact[] Impacts = new DefenseImpact[64];
        public readonly DefenseProjectile[] Projectiles = new DefenseProjectile[48];
        public readonly DefenseFloatText[] Texts = new DefenseFloatText[48];
        public readonly DefenseDeath[] Deaths = new DefenseDeath[48];
        // 刷怪线与可视区域上沿（战场坐标），由表现层按屏幕顶边换算；敌人只在可视区域内被攻击。
        public float SpawnY = ARENA_SIZE.y + 80, VisibleTop = ARENA_SIZE.y;
        public EDefenseState State { get; private set; }
        public float Elapsed { get; private set; }
        public float Health { get; private set; }
        public int Score { get; private set; }
        public int ActiveCount { get; private set; }
        public int Spawned { get; private set; }
        public float Cooldown { get; private set; }
        public float SkillAge { get; private set; } = -1;
        public Vector2 SkillPosition { get; private set; }
        public Vector2 ShotPosition { get; private set; }
        public float ShotFlash { get; private set; }
        public float BaseFlash { get; private set; }
        public int ShotsFired { get; private set; }
        public int SkillHits { get; private set; }
        public int SkillKills { get; private set; }
        // 本局统计，结算页展示。
        public int Kills { get; private set; }
        public int TankKills { get; private set; }
        public int SkillCasts { get; private set; }
        public int BestSkillKills { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public float DamageTaken { get; private set; }
        // 表现层用计数变化触发一次性反馈（音效、震屏、停顿），避免在规则里依赖表现。
        public int KillEvents { get; private set; }
        public int BaseHits { get; private set; }
        private int m_impactSlot, m_textSlot, m_deathSlot;
        private System.Random m_random;
        private float m_spawnTimer, m_shotTimer, m_comboTimer;
        private bool m_skillHit;
        public bool Practice { get; private set; }
        private bool m_practiceAutoFire;

        public DefenseRound(DefenseRules rules)
        {
            Rules = rules;
            Enemies = new DefenseEnemy[rules.Capacity];
            State = EDefenseState.Ready;
            Health = rules.BaseHealth;
        }

        public void Start()
        {
            Practice = false;
            Array.Clear(Enemies, 0, Enemies.Length);
            Array.Clear(Impacts, 0, Impacts.Length);
            Array.Clear(Projectiles, 0, Projectiles.Length);
            Array.Clear(Texts, 0, Texts.Length);
            Array.Clear(Deaths, 0, Deaths.Length); m_deathSlot = 0;
            m_impactSlot = m_textSlot = ShotsFired = SkillHits = SkillKills = 0;
            Kills = TankKills = SkillCasts = BestSkillKills = Combo = MaxCombo = KillEvents = BaseHits = 0;
            DamageTaken = 0; m_comboTimer = 0;
            m_random = new System.Random(Rules.Seed);
            Elapsed = Score = ActiveCount = Spawned = 0;
            Health = Rules.BaseHealth; Cooldown = 0; SkillAge = -1;
            m_spawnTimer = 0.2f; m_shotTimer = 0; ShotFlash = BaseFlash = 0;
            m_skillHit = false; State = EDefenseState.Playing;
        }

        public void StartPractice(Vector2[] positions, bool autoFire)
        {
            Start(); Practice = true; m_practiceAutoFire = autoFire;
            m_shotTimer = .8f;
            for (int i = 0; i < positions.Length && i < Enemies.Length; i++)
            {
                float health = Mathf.Min(Rules.Runner.Health, autoFire ? Rules.ShotDamage : Rules.SkillDamage);
                Enemies[i] = new DefenseEnemy { Active = true, Position = positions[i], Health = health, MaxHealth = health };
                ActiveCount++; Spawned++;
            }
        }

        public void Pause() { if (State == EDefenseState.Playing) State = EDefenseState.Paused; }
        public void Resume() { if (State == EDefenseState.Paused) State = EDefenseState.Playing; }

        public bool Cast(Vector2 point)
        {
            if (State != EDefenseState.Playing || Cooldown > 0 || SkillAge >= 0) return false;
            SkillPosition = new Vector2(Mathf.Clamp(point.x, 0, ARENA_SIZE.x), Mathf.Clamp(point.y, 0, Mathf.Max(ARENA_SIZE.y, VisibleTop)));
            SkillAge = 0; m_skillHit = false; Cooldown = Practice ? 0 : Rules.SkillCooldown;
            SkillHits = SkillKills = 0; SkillCasts++;
            return true;
        }

        public void Tick(float delta)
        {
            if (State != EDefenseState.Playing || delta <= 0) return;
            // Small deterministic steps prevent burst-spawn and frame-dependent combat.
            while (delta > 0 && State == EDefenseState.Playing)
            {
                float step = Mathf.Min(delta, 1f / 30f);
                Advance(step);
                delta -= step;
            }
        }

        private void Advance(float delta)
        {
            if (!Practice) Elapsed = Mathf.Min(Rules.Duration, Elapsed + delta);
            Cooldown = Mathf.Max(0, Cooldown - delta);
            ShotFlash = Mathf.Max(0, ShotFlash - delta);
            BaseFlash = Mathf.Max(0, BaseFlash - delta);
            m_comboTimer -= delta;
            if (m_comboTimer <= 0) Combo = 0;
            for (int i = 0; i < Impacts.Length; i++) Impacts[i].Life = Mathf.Max(0, Impacts[i].Life - delta);
            for (int i = 0; i < Texts.Length; i++) Texts[i].Life = Mathf.Max(0, Texts[i].Life - delta);
            for (int i = 0; i < Deaths.Length; i++) Deaths[i].Life = Mathf.Max(0, Deaths[i].Life - delta);
            m_spawnTimer -= delta;
            if (!Practice && m_spawnTimer <= 0)
            {
                // Pressure increases without changing the selected preset's enemy stats.
                m_spawnTimer += Rules.SpawnInterval * Mathf.Lerp(1, Rules.SpawnIntervalEndScale, Elapsed / Rules.Duration);
                for (int i = 0; i < Rules.SpawnBatch; i++) Spawn();
            }
            for (int i = 0; i < Enemies.Length; i++)
            {
                if (!Enemies[i].Active) continue;
                ref DefenseEnemy enemy = ref Enemies[i];
                DefenseRules.EnemySpec spec = enemy.Tank ? Rules.Tank : Rules.Runner;
                enemy.Flash = Mathf.Max(0, enemy.Flash - delta);
                enemy.Windup = Mathf.Max(0, enemy.Windup - delta);
                enemy.Age += delta; enemy.Knock = Mathf.Max(0, enemy.Knock - delta);
                if (Practice) continue;
                Vector2 difference = BASE_POSITION - enemy.Position;
                if (difference.sqrMagnitude > spec.AttackRange * spec.AttackRange)
                    enemy.Position = Vector2.MoveTowards(enemy.Position, BASE_POSITION, spec.Speed * delta);
                else
                {
                    enemy.AttackTimer -= delta;
                    if (enemy.AttackTimer <= 0)
                    {
                        enemy.AttackTimer += spec.AttackInterval;
                        enemy.Windup = 0.25f;
                        if (spec.Ranged) Fire(enemy.Position, spec.Attack);
                        else HitBase(spec.Attack);
                    }
                }
            }
            for (int i = 0; i < Projectiles.Length; i++)
            {
                if (!Projectiles[i].Active) continue;
                ref DefenseProjectile shot = ref Projectiles[i];
                shot.Position = Vector2.MoveTowards(shot.Position, BASE_POSITION, PROJECTILE_SPEED * delta);
                if ((shot.Position - BASE_POSITION).sqrMagnitude > 24 * 24) continue;
                shot.Active = false;
                HitBase(shot.Damage);
            }
            m_shotTimer -= delta;
            if ((!Practice || m_practiceAutoFire) && m_shotTimer <= 0)
            {
                int target = FindTarget();
                if (target >= 0)
                {
                    ShotPosition = Enemies[target].Position; ShotFlash = 0.09f;
                    ShotsFired++;
                    Damage(target, Rules.ShotDamage, false);
                }
                m_shotTimer += Rules.ShotInterval;
            }
            if (SkillAge >= 0)
            {
                SkillAge += delta;
                if (!m_skillHit && SkillAge >= Rules.TelegraphDuration)
                {
                    m_skillHit = true;
                    float radiusSquared = Rules.SkillRadius * Rules.SkillRadius;
                    for (int i = 0; i < Enemies.Length; i++)
                        if (Enemies[i].Active && Enemies[i].Position.y <= VisibleTop && (Enemies[i].Position - SkillPosition).sqrMagnitude <= radiusSquared)
                        {
                            SkillHits++;
                            Damage(i, Rules.SkillDamage, true);
                            if (!Enemies[i].Active) SkillKills++;
                        }
                    // 技能波同时冲散范围内的水弹。
                    for (int i = 0; i < Projectiles.Length; i++)
                        if (Projectiles[i].Active && (Projectiles[i].Position - SkillPosition).sqrMagnitude <= radiusSquared)
                            Projectiles[i].Active = false;
                    BestSkillKills = Mathf.Max(BestSkillKills, SkillKills);
                }
                if (SkillAge >= Rules.TelegraphDuration + Rules.ReleaseDuration + Rules.HitDuration) SkillAge = -1;
            }
            if (!Practice)
            {
                if (Health <= 0) State = EDefenseState.Lost;
                else if (Elapsed >= Rules.Duration) State = EDefenseState.Won;
            }
        }

        public int FindTarget()
        {
            int target = -1;
            float nearest = Rules.ShotRange * Rules.ShotRange;
            for (int i = 0; i < Enemies.Length; i++)
            {
                if (!Enemies[i].Active || Enemies[i].Position.y > VisibleTop) continue;
                float distance = (Enemies[i].Position - BASE_POSITION).sqrMagnitude;
                if (distance <= nearest) { nearest = distance; target = i; }
            }
            return target;
        }

#if UNITY_EDITOR
        // Reproducible pre-optimization algorithm, never included in a player.
        public int FindTargetAllocatingBaseline()
        {
            float range = Rules.ShotRange * Rules.ShotRange;
            return Enumerable.Range(0, Enemies.Length)
                .Where(i => Enemies[i].Active && Enemies[i].Position.y <= VisibleTop && (Enemies[i].Position - BASE_POSITION).sqrMagnitude <= range)
                .OrderBy(i => (Enemies[i].Position - BASE_POSITION).sqrMagnitude)
                .ThenByDescending(i => i).DefaultIfEmpty(-1).First();
        }
#endif

        private void Spawn()
        {
            if (ActiveCount == Enemies.Length) return;
            for (int i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Active) continue;
                bool tank = Spawned % Rules.TankEvery == Rules.TankEvery - 1;
                float health = tank ? Rules.Tank.Health : Rules.Runner.Health;
                Enemies[i] = new DefenseEnemy { Active = true, Tank = tank,
                    Position = new Vector2(38 + (float)m_random.NextDouble() * 564, SpawnY + (float)m_random.NextDouble() * 30),
                    Health = health, MaxHealth = health };
                ActiveCount++; Spawned++; return;
            }
        }

        private void Fire(Vector2 origin, float damage)
        {
            for (int i = 0; i < Projectiles.Length; i++)
            {
                if (Projectiles[i].Active) continue;
                Projectiles[i] = new DefenseProjectile { Active = true, Origin = origin, Position = origin, Damage = damage };
                return;
            }
            HitBase(damage);
        }

        private void HitBase(float damage)
        {
            Health = Mathf.Max(0, Health - damage);
            DamageTaken += damage; BaseFlash = 0.18f; BaseHits++;
        }

        private void Damage(int index, float damage, bool skill)
        {
            ref DefenseEnemy enemy = ref Enemies[index];
            enemy.Health -= damage; enemy.Flash = 0.16f; enemy.Knock = KNOCK_LIFE;
            bool destroyed = enemy.Health <= 0;
            Impacts[m_impactSlot] = new DefenseImpact { Position = enemy.Position, Life = IMPACT_LIFE,
                Destroyed = destroyed, Tank = enemy.Tank, Skill = skill };
            m_impactSlot = (m_impactSlot + 1) % Impacts.Length;
            Texts[m_textSlot] = new DefenseFloatText { Position = enemy.Position, Value = Mathf.RoundToInt(damage),
                Life = TEXT_LIFE, Kill = destroyed, Skill = skill };
            m_textSlot = (m_textSlot + 1) % Texts.Length;
            if (!destroyed) return;
            enemy.Active = false; ActiveCount--;
            Deaths[m_deathSlot] = new DefenseDeath { Position = enemy.Position, Life = DEATH_LIFE, Tank = enemy.Tank };
            m_deathSlot = (m_deathSlot + 1) % Deaths.Length;
            Kills++; KillEvents++;
            if (enemy.Tank) TankKills++;
            Combo = m_comboTimer > 0 ? Combo + 1 : 1;
            MaxCombo = Mathf.Max(MaxCombo, Combo);
            m_comboTimer = COMBO_WINDOW;
            if (!Practice) Score += enemy.Tank ? Rules.Tank.Score : Rules.Runner.Score;
        }
    }
}
