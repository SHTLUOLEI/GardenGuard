using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace GameLogic.Defense90
{
    // 世界空间表现；规则坐标 100 单位 = 1 米，表现时间不推进回合规则。
    public sealed class DefenseBattlefield : MonoBehaviour
    {
        public Camera WorldCamera;
        public Sprite TurretSprite, RunnerSprite, TankSprite, WhiteSprite, StarSprite;
        public SpriteRenderer Background;
        public SpriteRenderer Turret;
        public Material WaterMaterial;
        // 击杀水泡与金币闪光（Defense90/EffectSprite），运行时按形态复制成两份共享材质。
        public Material EffectMaterial;
        public DefenseSkillTimeline Timeline;
        public DefenseSkillPresentation Skill;
        public Texture2D UnitAtlas;
        public int ActiveEnemyViews { get; private set; }
        public bool HitStopped => m_stop > 0;
        public event System.Action<bool, bool> ImpactShown;
        private DefenseGame m_game;
        private SpriteRenderer[] m_enemies, m_shadows, m_health, m_impacts, m_projectiles, m_deaths;
        private readonly SpriteRenderer[] m_shots = new SpriteRenderer[16], m_coins = new SpriteRenderer[48],
            m_pops = new SpriteRenderer[48], m_sparkles = new SpriteRenderer[48];
        private readonly Vector3[] m_shotEnds = new Vector3[16], m_coinStarts = new Vector3[48];
        private readonly float[] m_shotAges = new float[16], m_coinAges = new float[48], m_impactLives = new float[64];
        private Material m_shadowMaterial, m_dropMaterial, m_shotMaterial, m_splashMaterial, m_popMaterial, m_sparkleMaterial;
        private Transform[] m_enemyTransforms, m_shadowTransforms, m_healthTransforms;
        private int[] m_enemyStates, m_enemyRanks;
        private float m_runnerWidth, m_tankWidth, m_whiteWidth, m_starWidth;
        private static readonly int ModeId = Shader.PropertyToID("_Mode"), ProgressId = Shader.PropertyToID("_Progress"),
            OpacityId = Shader.PropertyToID("_Opacity"), DetailId = Shader.PropertyToID("_Detail"),
            WaterId = Shader.PropertyToID("_Water"), FoamId = Shader.PropertyToID("_Foam"), DeepId = Shader.PropertyToID("_Deep"),
            DropletsId = Shader.PropertyToID("_Droplets"), KillDropletsId = Shader.PropertyToID("_KillDroplets"),
            ShapeId = Shader.PropertyToID("_Shape");
        private PointerEventData m_pointer;
        private readonly List<RaycastResult> m_uiHits = new List<RaycastResult>();
        private float m_time, m_stop;
        private GameProto.DefenseVfxTierConfig m_tier;
        private int m_lastShot, m_shotSlot, m_coinSlot;
        private Vector3 m_modelOrigin, m_turretHome, m_cameraShake;
        private Quaternion m_turretRotation;
        private int[] m_enemyOrder;
        private Camera m_overlay;
        private const float KILL_POP_LIFE = 0.35f;

        public static Vector3 ToWorld(Vector2 p) => new Vector3(p.x * 0.01f, p.y * 0.01f, 0);
        public Vector3 WorldPoint(Vector2 point) => transform.TransformPoint(m_modelOrigin + ToWorld(point));
        public Vector2 RulePoint(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world) - m_modelOrigin;
            return new Vector2(local.x, local.y) * 100;
        }

        public void Initialize(DefenseGame game)
        {
            m_game = game;
            m_turretHome = Turret.transform.localPosition;
            m_turretRotation = Turret.transform.localRotation;
            m_modelOrigin = m_turretHome - ToWorld(DefenseRound.BASE_POSITION);
            Skill.Column.sortingOrder = 1500;
            Skill.Splash.sortingOrder = 1510;
            m_runnerWidth = 1 / RunnerSprite.bounds.size.x; m_tankWidth = 1 / TankSprite.bounds.size.x;
            m_whiteWidth = 1 / WhiteSprite.bounds.size.x; m_starWidth = 1 / StarSprite.bounds.size.x;
        }

        public void ResetRound(DefenseRound round)
        {
            if (m_enemies == null)
            {
                m_enemies = Pool("Enemy", round.Enemies.Length, RunnerSprite, 100);
                m_shadows = Pool("Shadow", round.Enemies.Length, WhiteSprite, 0, true);
                m_health = Pool("Health", round.Enemies.Length, WhiteSprite, 1300);
                m_impacts = Pool("Splash", round.Impacts.Length, WhiteSprite, 1400, true);
                m_deaths = Pool("Death", round.Deaths.Length, RunnerSprite, 1200);
                m_projectiles = Pool("EnemyProjectile", round.Projectiles.Length, WhiteSprite, 1400, true);
                for (int i = 0; i < m_shots.Length; i++) m_shots[i] = Create("WaterShot_" + i, WhiteSprite, 1410, true);
                for (int i = 0; i < m_coins.Length; i++)
                {
                    m_coins[i] = Create("StarCoin_" + i, StarSprite, 1900);
                    m_pops[i] = Create("KillPop_" + i, WhiteSprite, 1420);
                    m_sparkles[i] = Create("CoinSparkle_" + i, WhiteSprite, 1910);
                }
                m_enemyOrder = new int[round.Enemies.Length];
                for (int i = 0; i < m_enemyOrder.Length; i++) m_enemyOrder[i] = i;
                m_enemyStates = new int[round.Enemies.Length];
                m_enemyRanks = new int[round.Enemies.Length];
                m_enemyTransforms = Transforms(m_enemies);
                m_shadowTransforms = Transforms(m_shadows);
                m_healthTransforms = Transforms(m_health);
                // 阴影和水弹的参数恒定，共用两份运行时材质，避免每敌人每帧设置属性块。
                var shadow = new Color(.12f, .22f, .15f);
                m_shadowMaterial = DropMaterial(shadow, shadow, shadow, .15f);
                m_shadowMaterial.SetFloat(DetailId, 0);
                m_dropMaterial = DropMaterial(Timeline.Water, Timeline.Foam, Timeline.Outline, 1);
                m_shotMaterial = DropMaterial(Timeline.Water, Timeline.Foam, Timeline.Outline, 1);
                // 每个水花的进度放入顶点alpha，材质参数共用，避免属性块拆开批次。
                m_splashMaterial = new Material(WaterMaterial) { hideFlags = HideFlags.DontSave };
                m_splashMaterial.SetFloat(ModeId, 2); m_splashMaterial.SetFloat(ProgressId, -1);
                m_splashMaterial.SetFloat(OpacityId, 1);
                foreach (var renderer in m_impacts) renderer.sharedMaterial = m_splashMaterial;
                foreach (var renderer in m_shadows) renderer.sharedMaterial = m_shadowMaterial;
                foreach (var renderer in m_projectiles) renderer.sharedMaterial = m_dropMaterial;
                foreach (var renderer in m_shots) renderer.sharedMaterial = m_shotMaterial;
                m_popMaterial = EffectShape(0); m_sparkleMaterial = EffectShape(1);
                foreach (var renderer in m_pops) renderer.sharedMaterial = m_popMaterial;
                foreach (var renderer in m_sparkles) renderer.sharedMaterial = m_sparkleMaterial;
                foreach (var renderer in m_health) renderer.color = new Color(.25f, .8f, .45f);
            }
            m_time = m_stop = 0; m_lastShot = m_shotSlot = m_coinSlot = 0;
            m_splashMaterial.SetColor(WaterId, Timeline.Water); m_splashMaterial.SetColor(FoamId, Timeline.Foam);
            m_splashMaterial.SetColor(DeepId, Timeline.Outline);
            ApplyTier(m_game.Tier);
            for (int i = 0; i < m_shots.Length; i++) { m_shotAges[i] = -1; m_shots[i].enabled = false; }
            for (int i = 0; i < m_coins.Length; i++) { m_coinAges[i] = -1; m_coins[i].enabled = m_pops[i].enabled = m_sparkles[i].enabled = false; }
            System.Array.Clear(m_impactLives, 0, m_impactLives.Length);
            System.Array.Clear(m_enemyStates, 0, m_enemyStates.Length);
            for (int i = 0; i < m_enemies.Length; i++)
                m_enemies[i].enabled = m_shadows[i].enabled = m_health[i].enabled = false;
        }

        private static Transform[] Transforms(SpriteRenderer[] renderers)
        {
            var result = new Transform[renderers.Length];
            for (int i = 0; i < result.Length; i++) result[i] = renderers[i].transform;
            return result;
        }

        private Material DropMaterial(Color water, Color foam, Color deep, float opacity)
        {
            var material = new Material(WaterMaterial) { hideFlags = HideFlags.DontSave };
            material.SetFloat(ModeId, 3); material.SetFloat(ProgressId, 0); material.SetFloat(OpacityId, opacity);
            material.SetColor(WaterId, water); material.SetColor(FoamId, foam); material.SetColor(DeepId, deep);
            return material;
        }

        private Material EffectShape(float shape)
        {
            var material = new Material(EffectMaterial) { hideFlags = HideFlags.DontSave };
            material.SetFloat(ShapeId, shape);
            material.SetColor(WaterId, Timeline.Water); material.SetColor(FoamId, Timeline.Foam); material.SetColor(DeepId, Timeline.Outline);
            return material;
        }

        // 档位只写共享材质：完整档水纹/高光/彗星拖尾，简化档保留轮廓；水滴数量读档位表。
        private void ApplyTier(GameProto.DefenseVfxTierConfig tier)
        {
            m_tier = tier;
            float detail = tier.ShaderDetail ? 1 : 0;
            m_splashMaterial.SetFloat(DetailId, detail);
            m_splashMaterial.SetFloat(DropletsId, tier.Fragments);
            m_splashMaterial.SetFloat(KillDropletsId, tier.KillFragments);
            m_dropMaterial.SetFloat(DetailId, detail);
            m_shotMaterial.SetFloat(DetailId, detail);
            m_shotMaterial.SetFloat(ModeId, tier.Glow ? 4 : 3);
            m_popMaterial.SetFloat(DetailId, detail);
        }

        // URP 下两个 Base 相机互相覆盖；战斗期间 UI 相机作为 Overlay 叠在世界相机上，销毁时恢复 Base 供首页使用。
        public void StackOverlay(Camera overlay)
        {
            if (overlay == null || WorldCamera == null) return;
            overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            var stack = WorldCamera.GetUniversalAdditionalCameraData().cameraStack;
            if (!stack.Contains(overlay)) stack.Add(overlay);
            m_overlay = overlay;
        }

        private void OnDestroy()
        {
            if (m_overlay != null)
            {
                m_overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
                m_overlay = null;
            }
            foreach (var material in new[] { m_shadowMaterial, m_dropMaterial, m_shotMaterial, m_splashMaterial, m_popMaterial, m_sparkleMaterial })
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }

        private SpriteRenderer Create(string name, Sprite sprite, int order, bool water = false)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sortingOrder = order; renderer.enabled = false;
            if (water) renderer.sharedMaterial = WaterMaterial;
            return renderer;
        }

        private int CompareEnemies(int a, int b)
        {
            var enemies = m_game.Round.Enemies;
            if (enemies[a].Active != enemies[b].Active) return enemies[a].Active ? -1 : 1;
            if (!enemies[a].Active) return a.CompareTo(b);
            int y = m_game.Round.Enemies[b].Position.y.CompareTo(m_game.Round.Enemies[a].Position.y);
            return y != 0 ? y : a.CompareTo(b);
        }

        private void SortEnemies(DefenseRound round)
        {
            // 复用上一帧顺序，近似有序时线性扫描；交叉/生成/消失仍在本帧校正。
            for (int i = 1; i < m_enemyOrder.Length; i++)
            {
                int index = m_enemyOrder[i], rank = i - 1;
                while (rank >= 0 && CompareEnemies(index, m_enemyOrder[rank]) < 0)
                {
                    m_enemyOrder[rank + 1] = m_enemyOrder[rank];
                    rank--;
                }
                m_enemyOrder[rank + 1] = index;
            }
            for (int rank = 0; rank < m_enemyOrder.Length; rank++)
            {
                int index = m_enemyOrder[rank], order = 100 + rank;
                if (!round.Enemies[index].Active) break;
                if (m_enemyRanks[index] == order) continue;
                m_enemies[index].sortingOrder = m_enemyRanks[index] = order;
            }
        }

        private SpriteRenderer[] Pool(string name, int count, Sprite sprite, int order, bool water = false)
        {
            var result = new SpriteRenderer[count];
            for (int i = 0; i < count; i++) result[i] = Create(name + "_" + i, sprite, order, water);
            return result;
        }

        // 场景里的相机、背景、炮台 Transform 是构图权威。这里只读取视口决定刷怪边界。
        public void UpdateBounds(DefenseRound round)
        {
            Rect rect = WorldCamera.pixelRect;
            ScreenPoint(new Vector2(rect.center.x, rect.yMax), out Vector2 top);
            round.VisibleTop = top.y;
            round.SpawnY = round.VisibleTop + Mathf.Max(round.Rules.Runner.Size, round.Rules.Tank.Size);
        }

        private bool ScreenPoint(Vector2 screen, out Vector2 point)
        {
            Ray ray = WorldCamera.ScreenPointToRay(screen);
            var plane = new Plane(transform.forward, WorldPoint(Vector2.zero));
            bool intersects = plane.Raycast(ray, out float distance);
            point = RulePoint(ray.GetPoint(distance));
            return intersects;
        }

        public bool TryGetArenaPoint(Vector2 screen, out Vector2 point)
        {
            bool intersects = ScreenPoint(screen, out point);
            return intersects && WorldCamera.pixelRect.Contains(screen) && point.x >= -40 && point.x <= 680 && point.y >= 0
                && point.y <= m_game.Round.VisibleTop;
        }

        public void Pointer(Vector2 screen, bool released)
        {
            if (!m_game.Aiming || m_game.Round.State != EDefenseState.Playing) return;
            if (EventSystem.current != null)
            {
                if (m_pointer == null) m_pointer = new PointerEventData(EventSystem.current);
                m_pointer.position = screen; m_uiHits.Clear();
                EventSystem.current.RaycastAll(m_pointer, m_uiHits);
                if (m_uiHits.Count > 0) return;
            }
            if (!TryGetArenaPoint(screen, out Vector2 point)) return;
            m_game.PreviewTarget(point);
            if (released) m_game.SelectTarget(point);
        }

        private void Update()
        {
            if (m_game == null || m_game.Round == null) return;
            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame))
            {
                Pointer(touch.position.ReadValue(), touch.press.wasReleasedThisFrame);
            }
            else if (Mouse.current != null) Pointer(Mouse.current.position.ReadValue(), Mouse.current.leftButton.wasReleasedThisFrame);
        }

        public void Shake(float amount, float time)
        {
            Vector3 offset = new Vector3(Mathf.Sin(time * 130), Mathf.Cos(time * 117) * 0.7f, 0) * amount * 0.01f;
            WorldCamera.transform.localPosition += offset - m_cameraShake;
            m_cameraShake = offset;
        }

        public void Refresh(float delta, Vector2 scoreScreen)
        {
            DefenseRound round = m_game.Round;
            if (m_tier != m_game.Tier) ApplyTier(m_game.Tier);
            bool playing = round.State == EDefenseState.Playing;
            float dt = playing ? delta : 0;
            m_stop = Mathf.Max(0, m_stop - dt);
            bool frozen = m_stop > 0;
            if (!frozen) m_time += dt;
            if (round.ShotsFired != m_lastShot)
            {
                m_lastShot = round.ShotsFired;
                int slot = m_shotSlot++ % m_shots.Length;
                m_shotEnds[slot] = WorldPoint(round.ShotPosition); m_shotAges[slot] = 0;
            }
            for (int i = 0; i < round.Impacts.Length; i++)
            {
                var hit = round.Impacts[i];
                bool fresh = hit.Life > m_impactLives[i];
                float age = DefenseRound.IMPACT_LIFE - hit.Life;
                float previousAge = DefenseRound.IMPACT_LIFE - m_impactLives[i];
                bool arrives = hit.Life > 0 && (hit.Skill ? fresh : age >= Timeline.ShotTravel && (fresh || previousAge < Timeline.ShotTravel));
                if (arrives)
                {
                    // 高频普通命中不冻结整场；技能仍保留短顿帧，规则时间不受影响。
                    if (hit.Skill) m_stop = Mathf.Max(m_stop, Timeline.HitStop);
                    ImpactShown?.Invoke(hit.Destroyed, hit.Tank);
                    if (hit.Destroyed)
                    {
                        int slot = m_coinSlot++ % m_coins.Length;
                        m_coinStarts[slot] = WorldPoint(hit.Position); m_coinAges[slot] = 0;
                    }
                }
                m_impactLives[i] = hit.Life;
                // 普攻模型仍即时结算，水花延迟至水弹抵达，规则数值保持原样。
                bool show = hit.Life > 0 && (hit.Skill || age >= Timeline.ShotTravel);
                m_impacts[i].enabled = show;
                if (show)
                {
                    float t = hit.Skill ? age / DefenseRound.IMPACT_LIFE : (age - Timeline.ShotTravel) / (DefenseRound.IMPACT_LIFE - Timeline.ShotTravel);
                    Place(m_impacts[i], WorldPoint(hit.Position), (hit.Destroyed ? 1.1f : 0.6f) * (0.6f + t), m_whiteWidth);
                    // 绿通道标记击杀，Shader 据此选用击杀水滴数，仍共用一份材质。
                    m_impacts[i].color = new Color(1, hit.Destroyed ? 0 : 1, 1, Mathf.Clamp01(t));
                }
            }
            if (!frozen)
            {
                Vector3 basePoint = WorldPoint(DefenseRound.BASE_POSITION);
                Vector3 aim = ToWorld(round.ShotPosition - DefenseRound.BASE_POSITION).normalized;
                if (round.ShotsFired == 0) aim = Vector3.up;
                Turret.enabled = true;
                Turret.transform.localPosition = m_turretHome - aim * round.ShotFlash * 0.9f;
                Turret.transform.localRotation = m_turretRotation * Quaternion.Euler(0, 0, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90);
                Turret.color = round.BaseFlash > 0 ? new Color(1, 0.65f, 0.65f) : Color.white;
                SortEnemies(round);
                ActiveEnemyViews = 0;
                Matrix4x4 worldMatrix = transform.localToWorldMatrix;
                for (int i = 0; i < round.Enemies.Length; i++)
                {
                    var enemy = round.Enemies[i];
                    int state = enemy.Active ? 1 | (enemy.Tank ? 2 : 0) | (enemy.Flash > .08f ? 4 : 0)
                        | (enemy.Health < enemy.MaxHealth ? 8 : 0) : 0;
                    int previous = m_enemyStates[i];
                    float size = (enemy.Tank ? round.Rules.Tank.Size : round.Rules.Runner.Size) * .015f;
                    if (state != previous)
                    {
                        if ((state & 1) != (previous & 1)) m_enemies[i].enabled = m_shadows[i].enabled = enemy.Active;
                        m_health[i].enabled = (state & 8) != 0;
                        if (enemy.Active)
                        {
                            if ((state & 3) != (previous & 3))
                            {
                                m_enemies[i].sprite = enemy.Tank ? TankSprite : RunnerSprite;
                                m_enemyTransforms[i].localScale = Vector3.one * size * (enemy.Tank ? m_tankWidth : m_runnerWidth);
                                m_shadowTransforms[i].localScale = new Vector3(size * .7f, size * .25f, 1);
                            }
                            if ((state & 5) != (previous & 5))
                                m_enemies[i].color = (state & 4) != 0 ? new Color(.6f, .9f, 1) : Color.white;
                        }
                        m_enemyStates[i] = state;
                    }
                    if (!enemy.Active) continue;
                    ActiveEnemyViews++;
                    Vector3 p = worldMatrix.MultiplyPoint3x4(m_modelOrigin + ToWorld(enemy.Position));
                    float stride = Mathf.Sin(m_time * (enemy.Tank ? 8 : 16) + i * 2.1f);
                    float hop = enemy.Tank ? 0 : Mathf.Max(0, stride) * 0.12f;
                    Vector3 away = (p - basePoint).normalized;
                    m_enemyTransforms[i].SetPositionAndRotation(
                        p + Vector3.up * hop + away * (Timeline.KnockDistance * enemy.Knock / DefenseRound.KNOCK_LIFE),
                        Quaternion.Euler(0, 0, Mathf.Atan2(-away.y, -away.x) * Mathf.Rad2Deg + 90 + stride * (enemy.Tank ? 7 : 3)));
                    m_shadowTransforms[i].position = p + Vector3.down * .15f;
                    if ((state & 8) != 0)
                    {
                        m_healthTransforms[i].position = p + Vector3.up * size * .45f;
                        m_healthTransforms[i].localScale = new Vector3(.4f * enemy.Health / enemy.MaxHealth, .035f, 1);
                    }
                }
                for (int i = 0; i < round.Deaths.Length; i++)
                {
                    var death = round.Deaths[i]; m_deaths[i].enabled = death.Life > 0;
                    if (death.Life <= 0) continue;
                    float t = death.Life / DefenseRound.DEATH_LIFE;
                    m_deaths[i].sprite = death.Tank ? TankSprite : RunnerSprite;
                    Place(m_deaths[i], WorldPoint(death.Position), (death.Tank ? round.Rules.Tank.Size : round.Rules.Runner.Size) * 0.015f * t,
                        death.Tank ? m_tankWidth : m_runnerWidth);
                    m_deaths[i].color = new Color(1, 1, 1, t);
                }
            }
            for (int i = 0; i < round.Projectiles.Length; i++)
            {
                m_projectiles[i].enabled = round.Projectiles[i].Active;
                if (!round.Projectiles[i].Active) continue;
                Place(m_projectiles[i], WorldPoint(round.Projectiles[i].Position), 0.16f, m_whiteWidth);
            }
            ScreenPoint(scoreScreen, out Vector2 scorePoint);
            Vector3 coinTarget = WorldPoint(scorePoint);
            for (int i = 0; i < m_shots.Length; i++)
            {
                if (m_shotAges[i] < 0) continue;
                float t = m_shotAges[i] / Timeline.ShotTravel;
                m_shots[i].enabled = t < 1;
                if (t >= 1) { m_shotAges[i] = -1; continue; }
                Vector3 start = WorldPoint(DefenseRound.BASE_POSITION);
                Place(m_shots[i], Vector3.Lerp(start, m_shotEnds[i], t), 0.33f, m_whiteWidth);
                if (m_tier.Glow)
                {
                    // 彗星拖尾：quad 的 +y 指向飞行方向并纵向拉长。
                    Vector3 travel = m_shotEnds[i] - start;
                    Transform shot = m_shots[i].transform;
                    shot.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg - 90);
                    shot.localScale = new Vector3(.33f, .6f, 1) * m_whiteWidth;
                }
                else m_shots[i].transform.rotation = Quaternion.identity;
                m_shotAges[i] += dt;
            }
            for (int i = 0; i < m_coins.Length; i++)
            {
                if (m_coinAges[i] < 0) continue;
                float t = m_coinAges[i] / 0.65f;
                m_coins[i].enabled = t < 1;
                // 击杀水泡与金币共用槽位和计时；进度写入顶点 alpha，材质保持共享。
                float pop = m_coinAges[i] / KILL_POP_LIFE;
                m_pops[i].enabled = pop < 1;
                if (pop < 1)
                {
                    Place(m_pops[i], m_coinStarts[i], 0.9f, m_whiteWidth);
                    m_pops[i].color = new Color(1, 1, 1, pop);
                }
                m_sparkles[i].enabled = t < 1 && m_tier.ShaderDetail;
                if (t >= 1) { m_coinAges[i] = -1; continue; }
                Vector3 coin = Vector3.Lerp(m_coinStarts[i], coinTarget, t * t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.7f);
                Place(m_coins[i], coin, 0.28f * (1 - t * 0.5f), m_starWidth);
                if (m_sparkles[i].enabled)
                {
                    Place(m_sparkles[i], coin, 0.55f, m_whiteWidth);
                    m_sparkles[i].color = new Color(1, 1, 1, t);
                }
                m_coins[i].transform.rotation = Quaternion.Euler(0, 0, t * 360);
                m_coinAges[i] += dt;
            }
            float radius = round.Rules.SkillRadius * 0.01f;
            if (m_game.Aiming && m_game.AimVisible) Skill.Aim(WorldPoint(m_game.AimPoint), radius, m_game.AimValid, m_time);
            else if (m_game.AimCancelAlpha > 0) Skill.Aim(WorldPoint(m_game.AimCancelPoint), radius, false, m_time, m_game.AimCancelAlpha);
            else Skill.Sample(WorldPoint(round.SkillPosition), radius, round.SkillAge, round.Rules.TelegraphDuration,
                round.Rules.ReleaseDuration, round.Rules.HitDuration, m_game.Tier.ShaderDetail);
        }

        private static void Place(SpriteRenderer renderer, Vector3 position, float size, float inverseWidth)
        {
            Transform target = renderer.transform;
            target.position = position;
            target.localScale = Vector3.one * size * inverseWidth;
        }

    }
}
