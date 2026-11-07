using Cysharp.Threading.Tasks;
using DGame;
using GameLogic.Defense90;
using UnityEngine;
using UnityEngine.UI;
using PlayerPrefsUtil = DGame.Utility.PlayerPrefsUtil;

namespace GameLogic
{
    public sealed class DefenseHomeWindow : UIWindow
    {
        public const string MODE_NORMAL_TEXT = "标准模式", MODE_STRESS_TEXT = "压力模式";
        private Button m_start, m_mode, m_settings;
        private Text m_modeText, m_best;
        private bool m_stress;
        private AudioSourceAgent m_music;
        private bool m_musicReady;
        public override bool FullScreen => true;
        public override bool CanEscClose => false;
        protected override ModelType GetModelType() => ModelType.NoneType;

        protected override void ScriptGenerator()
        {
            const string root = "m_rectSafe/m_rectStage/";
            m_start = FindChildComponent<Button>(root + "m_btnStart");
            m_mode = FindChildComponent<Button>(root + "m_btnMode");
            m_settings = FindChildComponent<Button>(root + "m_btnSettings");
            m_modeText = FindChildComponent<Text>(root + "m_btnMode/m_textMode");
            m_best = FindChildComponent<Text>(root + "m_textBest");
        }

        // 安全区按框架约定处理：m_rectSafe 收进刘海与手势区，背景不参与收缩。
        protected override void BindMemberProperty() => SetUIFit(FindChild("m_rectSafe") as RectTransform);

        protected override void RegisterEvent()
        {
            m_start.onClick.AddListener(StartBattle);
            m_mode.onClick.AddListener(ToggleMode);
            m_settings.onClick.AddListener(OpenSettings);
            AddUIEvent<EDefenseSetting>(IDefenseSettings_Event.OnSettingsChanged, OnSettingsChanged);
        }

        protected override void OnCreate()
        {
            // 标题资源带有放大缩放，限制到设计容器宽度，避免短屏适配后越界。
            var title = FindChildComponent<Text>("m_rectSafe/m_rectStage/m_textTitle").rectTransform;
            float titleScale = Mathf.Min(title.localScale.x, 680f / title.rect.width);
            title.localScale = Vector3.one * titleScale;
            gameObject.AddComponent<DefenseUIAudio>();
            LoadMusicAsync().Forget();
        }

        private async UniTask LoadMusicAsync()
        {
            // 即使资源命中缓存，也先让出首页创建帧和下一帧的渲染机会。
            if (await UniTask.DelayFrame(2, PlayerLoopTiming.LastPostLateUpdate, LifetimeToken).SuppressCancellationThrow()) return;
            Debug.Log($"[Startup] stage=Home.MusicLoadBegin t={Time.realtimeSinceStartup:F3}s frame={Time.frameCount}");
            m_musicReady = true;
            RefreshMusic();
        }

        private void OnSettingsChanged(EDefenseSetting setting)
        {
            if (setting == EDefenseSetting.Music) RefreshMusic();
        }

        private void RefreshMusic()
        {
            if (!m_musicReady) return;
            var audio = GameModule.AudioModule;
            bool enabled = DefenseSettings.MusicOn && DefenseSettings.MusicVolume > 0;
            if (enabled)
            {
                // 保留正在加载/播放的代理；设置音量不重新请求音乐。
                if (m_music == null) m_music = audio.Play(DGame.AudioType.Music, "Home", isLoop: true, isAsync: true, isInPool: true);
            }
            else
            {
                m_music?.Stop();
                m_music = null;
            }
        }

        protected override void OnRefresh()
        {
            m_stress = UserData is bool stress && stress;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            m_modeText.text = m_stress ? MODE_STRESS_TEXT : MODE_NORMAL_TEXT;
            m_best.text = $"最佳  {PlayerPrefsUtil.GetInt(DefenseGame.BEST_SCORE_KEY, 0):00000}";
        }

        private void ToggleMode() { m_stress = !m_stress; RefreshLabels(); }
        private void OpenSettings() => GameModule.UIModule.ShowWindowAsync<DefenseSettingsWindow>();

        private void StartBattle()
        {
            m_start.interactable = false;
            GameModule.UIModule.ShowWindowAsync<DefenseWindow>(m_stress);
            Close();
        }

        protected override void OnDestroy()
        {
            m_music?.Stop();
            m_music = null;
            m_start.onClick.RemoveListener(StartBattle);
            m_mode.onClick.RemoveListener(ToggleMode);
            m_settings.onClick.RemoveListener(OpenSettings);
        }
    }
}
