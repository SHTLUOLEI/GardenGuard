using DG.Tweening;
using GameProto;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PlayerPrefsUtil = DGame.Utility.PlayerPrefsUtil;

namespace GameLogic.Defense90
{
    public sealed class DefenseGame : MonoBehaviour
    {
        private static readonly ProfilerMarker BoundsMarker = new ProfilerMarker("Defense90.Bounds");
        private static readonly ProfilerMarker RulesMarker = new ProfilerMarker("Defense90.Rules");
        private static readonly ProfilerMarker HudMarker = new ProfilerMarker("Defense90.Hud");
        private static readonly ProfilerMarker BattlefieldMarker = new ProfilerMarker("Defense90.Battlefield");
        private static readonly ProfilerMarker FloatTextMarker = new ProfilerMarker("Defense90.FloatText");

        public const string BEST_SCORE_KEY = "Defense90.BestScore.v1";
        public const string COMBO_FORMAT = "连击 x{0}";
        public const string SKILL_COOLING = "冷却中";
        public const string SKILL_AIMING = "松手释放";
        private const int COMBO_MIN = 3, WARN_SECONDS = 10;
        private const float LOW_HEALTH = 0.3f, COMBO_BOUNCE_TIME = 0.28f;
        private const float GHOST_HOLD = 0.3f, CANCEL_FADE = 0.3f, ARENA_MARGIN = 40;

        public Texture2D UnitAtlas => Battlefield.UnitAtlas;
        public DefenseBattlefield Battlefield { get; private set; }
        private DefenseAssets m_assets;
        public DefenseRound Round { get; private set; }
        public bool Aiming { get; private set; }
        public bool AimVisible { get; private set; }
        // 瞄准点是否有效：拖回按钮或拖出战场时为 false，圈变灰，松手即取消。
        public bool AimValid { get; private set; } = true;
        public float AimCancelAlpha { get; private set; }
        public Vector2 AimCancelPoint { get; private set; }
        // 当前特效档位行，来自 DefenseSettings.VfxTier；设置变化时自动刷新。
        public DefenseVfxTierConfig Tier { get; private set; }
        public int BestScore { get; private set; }
        // 本局结算前的最高分与是否刷新纪录，结算页据此显示“新纪录”。
        public int PreviousBest { get; private set; }
        public bool NewRecord { get; private set; }
        public bool StressMode { get; private set; }
        // 说明和倒计时暂停，实际演示使用练习回合；全程不弹普通暂停页。
        public bool TutorialActive => m_tutorial.Active;
        public event System.Action<EDefenseState> StateChanged;
        public Vector2 AimPoint { get; set; } = new Vector2(320, 420);
        [SerializeField] private RectTransform m_root, m_healthFill, m_healthGhost, m_healthBar, m_heartIcon, m_progressFill, m_timerBadge;
        [SerializeField] private Image m_skillRing;
        [SerializeField] private Color m_ringReadyColor = Color.yellow, m_ringAimColor = Color.cyan;
        [SerializeField] private Text m_timerText, m_scoreText, m_healthText, m_skillText;
        [SerializeField] private Button m_skillButton;
        [SerializeField] private AudioSource m_audio, m_music;
        private DefenseAudio m_audioMgr;
        // 仅压测模式创建，用于第 3 题测量取证。
        private DefensePerfMonitor m_perf;
        [SerializeField] private Button m_pauseButton;
        [SerializeField] private Text m_comboText;
        [SerializeField] private Image m_vignette, m_skillCooldownFill;
        [SerializeField] private Text m_skillCooldownText;
        [SerializeField] private DefenseFloatTextView m_floatText;
        [SerializeField] private DefenseTutorial m_tutorial;
        [SerializeField] private Color m_healthNormalColor = Color.green, m_healthLowColor = Color.red, m_timerWarnColor = Color.red;
        [SerializeField] private float m_comboBounceScale = 0.4f;
        private Image m_healthFillImage;
        private Color m_timerBaseColor;
        private float m_uiTimer, m_vignetteAlpha, m_comboBounce, m_ghostRatio, m_ghostHold, m_lastHealth, m_scoreShown;
        private bool m_settled, m_hadFocus = true, m_ignoreClick, m_wasReady;
        private bool m_eventsBound;
        private int m_lastShot, m_lastBaseHits, m_lastCombo, m_lastCooldownSeconds = -1, m_lastScore, m_scoreInt = -1, m_lastRemaining = -1;
        private float m_lastSkillAge = -1;
        private int m_healthShown = -1;
        private float m_baseHealthShown = -1;
        private float m_scoreBounce;
        private Vector3 m_scoreHomeScale = Vector3.one;

        // All layout and components are authored in DefenseWindow.prefab.
        public void SetLoading() => m_root.gameObject.SetActive(false);

        public void Initialize(DefenseAssets assets, DefenseBattlefield battlefield, bool stressMode)
        {
            m_assets = assets;
            StressMode = stressMode;
            BestScore = PlayerPrefsUtil.GetInt(BEST_SCORE_KEY, 0);
            m_vignetteAlpha = m_vignette.color.a;
            m_timerBaseColor = m_timerText.color;
            m_healthFillImage = m_healthFill.GetComponent<Image>();
            m_scoreHomeScale = m_scoreText.transform.localScale;
            m_floatText.Warmup();
            Battlefield = battlefield;
            Battlefield.Initialize(this);
            Battlefield.StackOverlay(GameModule.UIModule.UICamera);
            Battlefield.ImpactShown += OnImpactShown;
            m_root.gameObject.SetActive(true);

            // 初始化音效管理器
            if (m_audioMgr == null) m_audioMgr = gameObject.AddComponent<DefenseAudio>();
            // 保留序列化字段以兼容现有 Prefab，旧音源停止参与播放。
            m_audio.Stop(); m_audio.clip = null;
            m_music.Stop(); m_music.clip = null;
            m_audioMgr.Initialize(assets);
            if (StressMode && m_perf == null) m_perf = new DefensePerfMonitor(m_root, m_timerText.font);

            ApplySettings();
            StartRound();
        }

        public void BindEvents()
        {
            if (m_eventsBound) return;
            m_pauseButton.onClick.AddListener(PauseRound);
            m_skillButton.onClick.AddListener(OnSkillClick);
            GameEventHelper.AddEvent.DefenseSettings.OnSettingsChanged(OnSettingsChanged);
            m_eventsBound = true;
        }

        public void Shutdown()
        {
            if (m_eventsBound)
            {
                GameEventHelper.RemoveEvent.DefenseSettings.OnSettingsChanged(OnSettingsChanged);
                m_pauseButton.onClick.RemoveListener(PauseRound);
                m_skillButton.onClick.RemoveListener(OnSkillClick);
                m_eventsBound = false;
            }
            DefenseTween.Kill(m_healthBar, m_heartIcon, m_timerBadge, m_scoreText, m_comboText, m_skillButton, m_skillRing);
            if (m_assets == null) return;
            m_tutorial.Hide();
            if (m_audioMgr != null) m_audioMgr.Shutdown();
            if (Battlefield != null)
            {
                Battlefield.ImpactShown -= OnImpactShown;
                Destroy(Battlefield.gameObject);
            }
            Battlefield = null;
            m_assets = null;
            Round = null;
        }

        private void OnSettingsChanged(EDefenseSetting setting)
        {
            if (setting == EDefenseSetting.VfxTier)
                Tier = DefenseConfigMgr.Instance.GetTier(DefenseSettings.VfxTier);
            else if (setting == EDefenseSetting.Music)
                RefreshMusic();
        }

        // 回合初始化只选择档位和曲目；音频参数由 DefenseSettings 同步。
        private void ApplySettings()
        {
            Tier = DefenseConfigMgr.Instance.GetTier(DefenseSettings.VfxTier);
            RefreshMusic();
        }

        private void RefreshMusic()
        {
            if (m_audioMgr != null && m_assets != null && m_assets.MusicBattle != null)
            {
                if (DefenseSettings.MusicOn && DefenseSettings.MusicVolume > 0 && (Round == null || Round.State != EDefenseState.Won && Round.State != EDefenseState.Lost))
                    m_audioMgr.PlayBGM(m_assets.MusicBattle, 1f);
                else m_audioMgr.StopBGM();
            }
        }

        public void StartRound()
        {
            m_tutorial.Hide();
            Round = new DefenseRound(DefenseConfigMgr.Instance.CreateRules(StressMode ? DefenseConfigMgr.MODE_STRESS : DefenseConfigMgr.MODE_NORMAL));
            UpdateStage();
            Round.Start();
            ApplySettings();
            Battlefield.ResetRound(Round);
            m_perf?.Reset();
            m_settled = false; Aiming = false; AimVisible = false; AimValid = true; AimCancelAlpha = 0; NewRecord = false; m_uiTimer = 0;
            m_lastShot = m_lastBaseHits = m_lastCombo = m_lastScore = 0; m_lastSkillAge = -1; m_lastCooldownSeconds = -1;
            m_scoreShown = 0; m_scoreInt = -1; m_lastRemaining = -1; m_ghostRatio = 1; m_ghostHold = 0; m_lastHealth = Round.Health; m_wasReady = false;
            m_scoreBounce = 0; m_scoreText.transform.localScale = m_scoreHomeScale;
            m_comboBounce = 0; m_comboText.gameObject.SetActive(false);
            RefreshInterface();
            if (!DefenseSettings.TutorialDone) { BeginTutorial(); return; }
            StateChanged?.Invoke(Round.State);
        }

        public void PauseRound()
        {
            if (TutorialActive) m_tutorial.Suspended = true;
            if (Round == null || Round.State != EDefenseState.Playing) return;
            Round.Pause(); Aiming = AimVisible = false; RefreshInterface();
            StateChanged?.Invoke(Round.State);
        }

        public void ResumeRound()
        {
            if (!m_hadFocus) return;
            if (TutorialActive)
            {
                m_tutorial.Suspended = false;
                if (m_tutorial.Phase != DefenseTutorialPhase.AutoFire && m_tutorial.Phase != DefenseTutorialPhase.Skill) return;
            }
            Round.Resume(); RefreshInterface();
            // 重置技能就绪标记，避免恢复时误触发音效
            bool ready = m_skillButton.interactable && !Aiming;
            m_wasReady = ready;
            StateChanged?.Invoke(Round.State);
        }

        // 正式计时之前完成站定怪演示与实际技能教学。
        private void BeginTutorial()
        {
            Round.Pause(); Aiming = false;
            m_tutorial.Begin(() => BeginPractice(true), () => BeginPractice(false), BeginCountdown, EndTutorial);
            RefreshInterface();
            StateChanged?.Invoke(Round.State);
        }

        private void EndTutorial()
        {
            DefenseSettings.TutorialDone = true;
            StartRound();
        }

        private void BeginPractice(bool autoFire)
        {
            Round = new DefenseRound(Round.Rules);
            UpdateStage();
            float y = autoFire ? DefenseRound.BASE_POSITION.y + Mathf.Min(180, Round.Rules.ShotRange * .55f)
                : Mathf.Clamp(Round.VisibleTop * .45f, 240, 500);
            Vector2 center = new Vector2(320, y);
            Vector2[] positions = autoFire
                ? new[] { center + new Vector2(-65, 0), center + new Vector2(0, 45), center + new Vector2(65, 0) }
                : new[] { center, center + new Vector2(-35, 25), center + new Vector2(35, 25), center + new Vector2(-30, -25), center + new Vector2(30, -25) };
            Round.StartPractice(positions, autoFire);
            Aiming = AimVisible = false; AimCancelAlpha = 0; m_lastShot = m_lastCombo = m_lastScore = 0; m_lastSkillAge = -1;
            Battlefield.ResetRound(Round); RefreshInterface();
            StateChanged?.Invoke(Round.State);
        }

        private void BeginCountdown()
        {
            Round.Pause(); Aiming = AimVisible = false; AimCancelAlpha = 0;
            RefreshInterface();
        }

        // 备用流程：点按钮进入/退出瞄准，再点战场落点。按住拖拽已在 SkillPress 中处理，此时 onClick 忽略一次。
        private void OnSkillClick()
        {
            if (m_ignoreClick) { m_ignoreClick = false; return; }
            ToggleAim();
        }

        public void ToggleAim()
        {
            if (TutorialActive && m_tutorial.Phase != DefenseTutorialPhase.Skill) return;
            if (Round.State != EDefenseState.Playing || Round.Cooldown > 0 || Round.SkillAge >= 0) return;
            if (Aiming) { CancelAim(); return; }
            Aiming = true; AimValid = false; AimVisible = false; AimCancelAlpha = 0;
            RefreshInterface();
        }

        // 按下技能按钮：冷却中抖动提示；否则进入瞄准（已在瞄准时再按一次取消）。
        public void SkillPress()
        {
            m_ignoreClick = true;
            if (TutorialActive && m_tutorial.Phase != DefenseTutorialPhase.Skill) return;
            if (Round == null || Round.State != EDefenseState.Playing) return;
            if (Round.Cooldown > 0 || Round.SkillAge >= 0)
            {
                m_skillButton.transform.DOComplete();
                m_skillButton.transform.DOShakePosition(0.25f, 8, 24).SetUpdate(true);
                return;
            }
            if (Aiming) { CancelAim(); return; }
            Aiming = true; AimValid = false; AimVisible = false; AimCancelAlpha = 0;
            RefreshInterface();
        }

        // 拖拽中：范围圈跟随手指；落在按钮上或战场外则标记无效。
        public void SkillDrag(PointerEventData data)
        {
            if (!Aiming || Round == null) return;
            bool overButton = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)m_skillButton.transform, data.position, data.pressEventCamera);
            bool inside = Battlefield.TryGetArenaPoint(data.position, out Vector2 point);
            AimPoint = point;
            AimValid = inside && !overButton && point.x >= -ARENA_MARGIN && point.x <= DefenseRound.ARENA_SIZE.x + ARENA_MARGIN
                && point.y >= 0 && point.y <= Mathf.Max(DefenseRound.ARENA_SIZE.y, Round.VisibleTop);
            // 尚未进入战场时没有可预览的落点；进入后保留拖出变灰、松手取消反馈。
            AimVisible |= AimValid;
        }

        public void PreviewTarget(Vector2 point)
        {
            AimPoint = point;
            AimValid = true;
            AimVisible = true;
        }

        // 松手：有效则释放，否则取消。
        public void SkillRelease(PointerEventData data)
        {
            m_ignoreClick = false;
            if (!Aiming) return;
            SkillDrag(data);
            if (AimValid) SelectTarget(AimPoint); else CancelAim();
        }

        public void CancelAim()
        {
            if (!Aiming) return;
            Aiming = false; AimCancelPoint = AimPoint; AimCancelAlpha = AimVisible ? 1 : 0; AimVisible = false;
            RefreshInterface();
        }

        public void SelectTarget(Vector2 point)
        {
            if (Aiming && Round.Cast(point))
            {
                Aiming = false;
                // 技能状态 1：进入冷却（释放瞬间）- 按钮压缩弹回
                DefenseTween.Punch(m_skillButton.transform, 0.15f, 0.3f);
                if (m_audioMgr != null) m_audioMgr.PlaySkillCharge();
                RefreshInterface();
            }
        }

        public void UpdateFrame()
        {
            if (Round == null) return;
            // 帧尖峰只限制追帧量；暂停由按钮或真实的焦点/后台事件触发。
            AdvanceFrame(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            m_perf?.Tick(Time.unscaledDeltaTime, Round);
        }

        // Shared by the UIWindow update and deterministic gameplay checks.
        public void AdvanceFrame(float delta)
        {
            if (Round == null) return;
            m_perf?.Mark(0);
            using (BoundsMarker.Auto()) UpdateStage();
            m_perf?.Mark(1);
            using (RulesMarker.Auto()) Round.Tick(delta);
            m_perf?.Mark(2);
            using (HudMarker.Auto())
            {
                if (TutorialActive) m_tutorial.Tick(delta, Round, Aiming);
                if (!m_settled && (Round.State == EDefenseState.Won || Round.State == EDefenseState.Lost)) Settle();
                m_uiTimer -= delta;
                if (m_uiTimer <= 0) { m_uiTimer = 0.1f; RefreshInterface(); }
                UpdateFeedback();
                UpdateHud(delta);
            }
            m_perf?.Mark(3);
            using (BattlefieldMarker.Auto())
                Battlefield.Refresh(delta, RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, m_scoreText.transform.position));
            m_perf?.Mark(4);
            using (FloatTextMarker.Auto())
                m_floatText.Refresh(Round, Tier.DamageText, Battlefield, GameModule.UIModule.UICamera);
            m_perf?.Mark(5);
        }

        // 把屏幕顶边换算成战场坐标：敌人刷在屏幕之外，只有进入可视区域才会被攻击。
        private void UpdateStage()
        {
            Battlefield.UpdateBounds(Round);
        }

        private void UpdateFeedback()
        {
            if (Round.ShotsFired != m_lastShot)
            {
                m_lastShot = Round.ShotsFired;
                if (m_audioMgr != null) m_audioMgr.PlayShoot();
            }
            if (Round.BaseHits != m_lastBaseHits)
            {
                m_lastBaseHits = Round.BaseHits;
                if (m_audioMgr != null) m_audioMgr.PlayBaseHit();
                DefenseHaptics.Vibrate(EDefenseHaptic.Short);
            }
            if (Round.SkillAge >= Round.Rules.TelegraphDuration && m_lastSkillAge < Round.Rules.TelegraphDuration)
            {
                if (m_audioMgr != null) m_audioMgr.PlaySkillSlam();
                if (Round.SkillHits > 0) DefenseHaptics.Vibrate(EDefenseHaptic.Long);
            }
            m_lastSkillAge = Round.SkillAge;
            float shake = 0;
            if (Tier.Shake && DefenseSettings.ShakeOn && Round.State == EDefenseState.Playing)
            {
                shake = Round.BaseFlash * 12;
                float release = Round.SkillAge - Round.Rules.TelegraphDuration;
                if (Round.SkillAge >= 0 && release >= 0 && release < 0.22f) shake += (1 - release / 0.22f) * Round.Rules.SkillShake;
            }
            Battlefield.Shake(shake, Round.Elapsed);
        }

        private void OnImpactShown(bool killed, bool tank)
        {
            m_audioMgr.PlaySplash();
            if (killed)
            {
                if (tank) m_audioMgr.PlayHeavyDown(); else m_audioMgr.PlayEnemyPop();
            }
        }

        // 每帧 HUD 动效：分数滚动、血条与残影、连击弹跳、低血量暗角、技能冷却遮罩与就绪外圈。
        private void UpdateHud(float delta)
        {
            if (AimCancelAlpha > 0) AimCancelAlpha = Mathf.Max(0, AimCancelAlpha - delta / CANCEL_FADE);

            // 分数滚动和弹跳
            if (Round.Score != m_lastScore)
            {
                if (Round.Score > m_lastScore)
                {
                    m_scoreBounce = 1;
                    if (m_audioMgr != null) m_audioMgr.PlayCoin();
                }
                m_lastScore = Round.Score;
            }
            // 高频击杀复用一个动效时钟，不反复创建、终止Punch Tween及其路径数组。
            if (m_scoreBounce > 0)
            {
                m_scoreBounce = Mathf.Max(0, m_scoreBounce - delta / .25f);
                m_scoreText.transform.localScale = m_scoreHomeScale *
                    (1 + .18f * m_scoreBounce * Mathf.Sin((1 - m_scoreBounce) * Mathf.PI * 6));
            }
            m_scoreShown = Mathf.Lerp(m_scoreShown, Round.Score, 1 - Mathf.Exp(-9 * delta));
            if (Mathf.Abs(Round.Score - m_scoreShown) < 0.5f) m_scoreShown = Round.Score;
            int shownScore = Mathf.RoundToInt(m_scoreShown);
            if (shownScore != m_scoreInt) { m_scoreInt = shownScore; m_scoreText.text = shownScore.ToString("00000"); }

            float ratio = Round.Health / Round.Rules.BaseHealth;
            if (Round.Health < m_lastHealth)
            {
                // 受伤：主填充立即下降，残影停 0.3s 后追上；血条抖动、爱心跳一下。
                m_ghostHold = GHOST_HOLD;
                m_healthBar.DOKill(true); m_healthBar.DOShakeAnchorPos(0.25f, 6, 20).SetUpdate(true);
                DefenseTween.Punch(m_heartIcon, 0.3f, 0.3f);
            }
            m_lastHealth = Round.Health;
            if (ratio > m_ghostRatio) m_ghostRatio = ratio;
            else if (m_ghostHold > 0) m_ghostHold -= delta;
            else m_ghostRatio = Mathf.Lerp(m_ghostRatio, ratio, 1 - Mathf.Exp(-4 * delta));
            SetBar(m_healthFill, ratio);
            SetBar(m_healthGhost, m_ghostRatio);
            SetBar(m_progressFill, Round.Elapsed / Round.Rules.Duration);

            if (Round.Combo != m_lastCombo)
            {
                m_lastCombo = Round.Combo;
                bool show = m_lastCombo >= COMBO_MIN;
                m_comboText.gameObject.SetActive(show);
                if (show)
                {
                    m_comboText.text = string.Format(COMBO_FORMAT, m_lastCombo);
                    m_comboBounce = 1;
                    if (m_audioMgr != null) m_audioMgr.PlayCombo(m_lastCombo);
                }
            }
            if (m_comboBounce > 0)
            {
                m_comboBounce = Mathf.Max(0, m_comboBounce - delta / COMBO_BOUNCE_TIME);
                m_comboText.rectTransform.localScale = Vector3.one * (1 + m_comboBounceScale * Mathf.Sin(m_comboBounce * Mathf.PI));
            }

            bool low = Round.State != EDefenseState.Ready && ratio < LOW_HEALTH;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
            Color vignette = m_vignette.color; vignette.a = low ? m_vignetteAlpha * (0.35f + 0.65f * pulse) : 0;
            m_vignette.color = vignette;
            m_healthFillImage.color = low ? Color.Lerp(m_healthLowColor, m_healthNormalColor, pulse * 0.6f) : m_healthNormalColor;

            float cooldown = Round.Cooldown;
            m_skillCooldownFill.fillAmount = cooldown > 0 && Round.Rules.SkillCooldown > 0 ? cooldown / Round.Rules.SkillCooldown : 0;
            int seconds = cooldown > 0 ? Mathf.CeilToInt(cooldown) : 0;
            if (seconds != m_lastCooldownSeconds)
            {
                m_lastCooldownSeconds = seconds;
                m_skillCooldownText.text = seconds > 0 ? DefenseNumbers.Get(seconds) : string.Empty;
            }

            // 技能状态 3：冷却结束（就绪瞬间）- 按钮弹起 + 提示音 + 外圈光波扩散
            bool ready = m_skillButton.interactable && !Aiming;
            if (ready && !m_wasReady)
            {
                DefenseTween.Punch(m_skillButton.transform, 0.25f, 0.3f);
                DefenseTween.Punch(m_skillRing.transform, 0.25f, 0.3f);
                if (m_audioMgr != null) m_audioMgr.PlayCombo(1);
            }
            m_wasReady = ready;
            float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            Color ring = Aiming ? m_ringAimColor : m_ringReadyColor;
            ring.a = Aiming ? ring.a : ready ? ring.a * (0.45f + 0.55f * breath) : 0;
            m_skillRing.color = ring;
        }

        // 条的宽度以所在槽（父级）为准；最小宽度等于高度，保证两端始终是圆头。
        private static void SetBar(RectTransform fill, float ratio)
        {
            float height = fill.sizeDelta.y;
            float parentWidth = ((RectTransform)fill.parent).rect.width;
            float width = ratio <= 0 ? 0 : Mathf.Max(height, Mathf.Clamp01(ratio) * parentWidth);
            float difference = Mathf.Abs(fill.sizeDelta.x - width);
            // 不足槽宽 0.1% 的变化累计后再提交；空/满端点仍精确抵达。
            if (difference == 0 || (ratio > 0 && ratio < 1 && difference < parentWidth * .001f)) return;
            fill.sizeDelta = new Vector2(width, height);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Round != null) PauseRound();
            else if (!paused && TutorialActive) ResumeRound();
        }

        private void OnApplicationFocus(bool focus)
        {
            m_hadFocus = focus;
            if (focus && TutorialActive) ResumeRound();
            if (!focus && Round != null) PauseRound();
        }

        private void Settle()
        {
            m_settled = true; Aiming = false;
            if (m_audioMgr != null)
            {
                if (Round.State == EDefenseState.Won) m_audioMgr.PlayVictory();
                else m_audioMgr.PlayDefeat();
            }
            // 先记下旧最高分，再更新 BestScore。
            PreviousBest = BestScore;
            if (Round.Score > BestScore)
            {
                NewRecord = true;
                BestScore = Round.Score;
                PlayerPrefsUtil.SetInt(BEST_SCORE_KEY, BestScore);
            }
            RefreshInterface();
            StateChanged?.Invoke(Round.State);
        }

        private void RefreshInterface()
        {
            if (m_root == null) return;
            int remaining = Mathf.CeilToInt(Round.Rules.Duration - Round.Elapsed);
            if (remaining != m_lastRemaining)
            {
                // 最后 10 秒数字变红，每秒脉冲放大一次。
                bool warn = remaining <= WARN_SECONDS && remaining > 0;
                if (warn && m_lastRemaining >= 0) DefenseTween.Punch(m_timerBadge, 0.15f, 0.3f);
                m_lastRemaining = remaining;
                m_timerText.text = $"{remaining / 60:00}:{remaining % 60:00}";
                m_timerText.color = warn ? m_timerWarnColor : m_timerBaseColor;
            }
            int health = Mathf.CeilToInt(Round.Health);
            if (health != m_healthShown || Round.Rules.BaseHealth != m_baseHealthShown)
            {
                m_healthShown = health;
                m_baseHealthShown = Round.Rules.BaseHealth;
                m_healthText.text = $"{health}/{m_baseHealthShown:0}";
            }
            m_pauseButton.interactable = !TutorialActive;
            m_skillButton.interactable = Round.State == EDefenseState.Playing && Round.Cooldown <= 0 && Round.SkillAge < 0
                && (!TutorialActive || m_tutorial.Phase == DefenseTutorialPhase.Skill);
            m_skillText.text = Round.Cooldown > 0 ? SKILL_COOLING : Aiming ? SKILL_AIMING : Round.Rules.SkillName;
        }

        private void OnDestroy() => Shutdown();
    }
}
