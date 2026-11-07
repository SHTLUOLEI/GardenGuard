using GameLogic.Defense90;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    // 设置页：首页和暂停页共用，叠在 Top 层，自带模态遮罩。战斗中从暂停页打开时回合保持暂停。
    public sealed class DefenseSettingsWindow : UIWindow
    {
        public const string TITLE = "设置", MUSIC = "音乐", SFX = "音效", VFX = "特效档位", SHAKE = "震屏", VIBRATE = "振动";
        public const string VFX_FULL = "完整", VFX_SIMPLE = "简化";
        public const string TUTORIAL = "重看新手引导", TUTORIAL_RESET = "已重置，下局开始时播放", CLOSE = "关闭";

        private Toggle m_toggleMusic, m_toggleSfx, m_toggleVfxFull, m_toggleVfxSimple, m_toggleShake, m_toggleVibrate;
        private Slider m_sliderMusic, m_sliderSfx;
        private Button m_btnTutorial, m_btnClose;
        private Text m_textTutorial;
        private Image m_shade;
        private RectTransform m_popup;
        protected override UILayer windowLayer => UILayer.Top;
        protected override bool NeedTweenPop => false;
        public override bool CanEscClose => false;
        protected override ModelType GetModelType() => ModelType.NoneType;

        protected override void ScriptGenerator()
        {
            const string root = "m_rectSafe/m_panel/m_rectStage/m_rectPopup/";
            m_shade = FindChildComponent<Image>("m_imgModalShade");
            m_popup = (RectTransform)FindChild("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
            m_toggleMusic = FindChildComponent<Toggle>(root + "m_toggleMusic");
            m_sliderMusic = FindChildComponent<Slider>(root + "m_sliderMusic");
            m_toggleSfx = FindChildComponent<Toggle>(root + "m_toggleSfx");
            m_sliderSfx = FindChildComponent<Slider>(root + "m_sliderSfx");
            m_toggleVfxFull = FindChildComponent<Toggle>(root + "m_groupVfx/m_toggleVfxFull");
            m_toggleVfxSimple = FindChildComponent<Toggle>(root + "m_groupVfx/m_toggleVfxSimple");
            m_toggleShake = FindChildComponent<Toggle>(root + "m_toggleShake");
            m_toggleVibrate = FindChildComponent<Toggle>(root + "m_toggleVibrate");
            m_btnTutorial = FindChildComponent<Button>(root + "m_btnTutorial");
            m_btnClose = FindChildComponent<Button>(root + "m_btnClose");
            m_textTutorial = FindChildComponent<Text>(root + "m_btnTutorial/m_textTutorial");
            SetText(root + "m_textTitle", TITLE);
            SetText(root + "m_textMusic", MUSIC);
            SetText(root + "m_textSfx", SFX);
            SetText(root + "m_textVfx", VFX);
            SetText(root + "m_textShake", SHAKE);
            SetText(root + "m_textVibrate", VIBRATE);
            SetText(root + "m_btnClose/m_textClose", CLOSE);
            m_toggleVfxFull.GetComponentInChildren<Text>().text = VFX_FULL;
            m_toggleVfxSimple.GetComponentInChildren<Text>().text = VFX_SIMPLE;
        }

        private void SetText(string path, string text) => FindChildComponent<Text>(path).text = text;

        protected override void BindMemberProperty() => SetUIFit(FindChild("m_rectSafe") as RectTransform);

        protected override void RegisterEvent()
        {
            gameObject.AddComponent<DefenseUIAudio>();
            AddUIEvent<EDefenseSetting>(IDefenseSettings_Event.OnSettingsChanged, OnSettingsChanged);
            m_toggleMusic.onValueChanged.AddListener(OnMusicToggle);
            m_sliderMusic.onValueChanged.AddListener(OnMusicVolume);
            m_toggleSfx.onValueChanged.AddListener(OnSfxToggle);
            m_sliderSfx.onValueChanged.AddListener(OnSfxVolume);
            m_toggleVfxFull.onValueChanged.AddListener(OnVfxToggle);
            m_toggleVfxSimple.onValueChanged.AddListener(OnVfxToggle);
            m_toggleShake.onValueChanged.AddListener(OnShakeToggle);
            m_toggleVibrate.onValueChanged.AddListener(OnVibrateToggle);
            m_btnTutorial.onClick.AddListener(OnClickTutorial);
            m_btnClose.onClick.AddListener(OnClickClose);
        }

        protected override void OnRefresh()
        {
            DefenseUIAudio.PlayClick();
            RefreshControls();
            m_textTutorial.text = DefenseSettings.TutorialDone ? TUTORIAL : TUTORIAL_RESET;
            m_btnClose.interactable = true;
            DefenseTween.PopIn(m_shade, m_popup);
        }

        private void OnSettingsChanged(EDefenseSetting setting) => RefreshControls();

        private void RefreshControls()
        {
            m_toggleMusic.SetIsOnWithoutNotify(DefenseSettings.MusicOn);
            m_sliderMusic.SetValueWithoutNotify(DefenseSettings.MusicVolume);
            m_toggleSfx.SetIsOnWithoutNotify(DefenseSettings.SfxOn);
            m_sliderSfx.SetValueWithoutNotify(DefenseSettings.SfxVolume);
            bool full = DefenseSettings.VfxTier == DefenseConfigMgr.TIER_FULL;
            m_toggleVfxFull.SetIsOnWithoutNotify(full);
            m_toggleVfxSimple.SetIsOnWithoutNotify(!full);
            m_toggleShake.SetIsOnWithoutNotify(DefenseSettings.ShakeOn);
            m_toggleVibrate.SetIsOnWithoutNotify(DefenseSettings.VibrateOn);
            RefreshStates();
        }

        // 滑条可用状态跟随开关。
        private void RefreshStates()
        {
            m_sliderMusic.interactable = m_toggleMusic.isOn;
            m_sliderSfx.interactable = m_toggleSfx.isOn;
        }


        private void OnMusicToggle(bool on) => DefenseSettings.MusicOn = on;
        private void OnMusicVolume(float value) => DefenseSettings.MusicVolume = value;
        private void OnSfxToggle(bool on) => DefenseSettings.SfxOn = on;
        private void OnSfxVolume(float value) => DefenseSettings.SfxVolume = value;
        private void OnShakeToggle(bool on) => DefenseSettings.ShakeOn = on;
        private void OnVibrateToggle(bool on) => DefenseSettings.VibrateOn = on;

        private void OnVfxToggle(bool on)
        {
            if (on) DefenseSettings.VfxTier = m_toggleVfxFull.isOn ? DefenseConfigMgr.TIER_FULL : DefenseConfigMgr.TIER_SIMPLE;
        }

        private void OnClickTutorial()
        {
            DefenseSettings.TutorialDone = false;
            m_textTutorial.text = TUTORIAL_RESET;
        }

        // 收起动画结束后再关闭窗口。
        private void OnClickClose()
        {
            m_btnClose.interactable = false;
            DefenseTween.PopOut(m_shade, m_popup, () => GameModule.UIModule.CloseWindow<DefenseSettingsWindow>());
        }

        protected override void OnDestroy()
        {
            DefenseTween.Kill(m_shade, m_popup);
            m_toggleMusic.onValueChanged.RemoveListener(OnMusicToggle);
            m_sliderMusic.onValueChanged.RemoveListener(OnMusicVolume);
            m_toggleSfx.onValueChanged.RemoveListener(OnSfxToggle);
            m_sliderSfx.onValueChanged.RemoveListener(OnSfxVolume);
            m_toggleVfxFull.onValueChanged.RemoveListener(OnVfxToggle);
            m_toggleVfxSimple.onValueChanged.RemoveListener(OnVfxToggle);
            m_toggleShake.onValueChanged.RemoveListener(OnShakeToggle);
            m_toggleVibrate.onValueChanged.RemoveListener(OnVibrateToggle);
            m_btnTutorial.onClick.RemoveListener(OnClickTutorial);
            m_btnClose.onClick.RemoveListener(OnClickClose);
        }
    }
}
