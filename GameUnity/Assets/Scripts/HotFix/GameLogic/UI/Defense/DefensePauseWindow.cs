using GameLogic.Defense90;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    // 暂停页：小结 + 继续/重新开始/设置/返回首页。继续必须由玩家点击，切后台返回不会自动继续。
    public sealed class DefensePauseWindow : UIWindow
    {
        public const string TITLE = "已暂停", RESUME = "继续防守", RESTART = "重新开始", SETTINGS = "设置", HOME = "返回首页";
        private Button m_resume, m_restart, m_settings, m_home;
        private Text m_title, m_time, m_score;
        private Image m_shade;
        private RectTransform m_popup;
        private DefenseWindow m_battle;
        protected override UILayer windowLayer => UILayer.Top;
        protected override bool NeedTweenPop => false;
        public override bool CanEscClose => false;
        protected override ModelType GetModelType() => ModelType.NoneType;

        protected override void ScriptGenerator()
        {
            const string root = "m_rectSafe/m_panel/m_rectStage/m_rectPopup/";
            m_shade = FindChildComponent<Image>("m_imgModalShade");
            m_popup = (RectTransform)FindChild("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
            m_resume = FindChildComponent<Button>(root + "m_btnResume");
            m_restart = FindChildComponent<Button>(root + "m_btnRestart");
            m_settings = FindChildComponent<Button>(root + "m_btnSettings");
            m_home = FindChildComponent<Button>(root + "m_btnHome");
            m_title = FindChildComponent<Text>(root + "m_textPauseTitle");
            m_time = FindChildComponent<Text>(root + "m_textPauseTime");
            m_score = FindChildComponent<Text>(root + "m_textPauseScore");
            m_resume.GetComponentInChildren<Text>().text = RESUME;
            m_restart.GetComponentInChildren<Text>().text = RESTART;
            m_settings.GetComponentInChildren<Text>().text = SETTINGS;
            m_home.GetComponentInChildren<Text>().text = HOME;
            m_title.text = TITLE;
        }

        protected override void BindMemberProperty() => SetUIFit(FindChild("m_rectSafe") as RectTransform);

        protected override void RegisterEvent()
        {
            gameObject.AddComponent<DefenseUIAudio>();
            m_resume.onClick.AddListener(Resume);
            m_restart.onClick.AddListener(Restart);
            m_settings.onClick.AddListener(OpenSettings);
            m_home.onClick.AddListener(Home);
        }

        protected override void OnRefresh()
        {
            DefenseUIAudio.PlayClick();
            m_battle = (DefenseWindow)UserData;
            DefenseRound round = m_battle.View.Round;
            int elapsed = Mathf.FloorToInt(round.Elapsed), total = Mathf.RoundToInt(round.Rules.Duration);
            m_time.text = $"已守护  {elapsed / 60:00}:{elapsed % 60:00} / {total / 60:00}:{total % 60:00}";
            m_score.text = $"得分  {round.Score:00000}";
            m_resume.interactable = true;
            DefenseTween.PopIn(m_shade, m_popup);
        }

        // 收起动画结束后再真正继续回合。
        private void Resume()
        {
            m_resume.interactable = false;
            DefenseTween.PopOut(m_shade, m_popup, () => m_battle.View.ResumeRound());
        }

        private void Restart() => m_battle.View.StartRound();
        private void OpenSettings() => GameModule.UIModule.ShowWindowAsync<DefenseSettingsWindow>();
        private void Home() => m_battle.ReturnHome();

        // 供UI与测试直接触发，避免依赖按钮层级查找。
        public void Continue() => m_battle.View.ResumeRound();

        protected override void OnDestroy()
        {
            DefenseTween.Kill(m_shade, m_popup);
            m_resume.onClick.RemoveListener(Resume);
            m_restart.onClick.RemoveListener(Restart);
            m_settings.onClick.RemoveListener(OpenSettings);
            m_home.onClick.RemoveListener(Home);
        }
    }
}
