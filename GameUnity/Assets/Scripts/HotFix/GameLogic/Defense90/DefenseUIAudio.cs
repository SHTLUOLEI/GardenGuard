using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Defense90
{
    // 交互声音由框架音频池播放，关闭窗口不会截断按钮尾音。
    public sealed class DefenseUIAudio : MonoBehaviour
    {
        private Toggle[] m_toggles;
        private Slider[] m_sliders;

        private void Awake()
        {
            m_toggles = GetComponentsInChildren<Toggle>(true);
            m_sliders = GetComponentsInChildren<Slider>(true);
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (button.GetComponent<BaseUIButton>() != null) continue;
                var sound = button.GetComponent<UIButtonSound>();
                if (sound == null) sound = button.gameObject.AddComponent<UIButtonSound>();
                sound.MinInterval = .06f;
            }
            foreach (var toggle in m_toggles) toggle.onValueChanged.AddListener(OnToggle);
            foreach (var slider in m_sliders) slider.onValueChanged.AddListener(OnSlider);
        }

        public static void PlayClick() => UIButtonSound.PlayDefault();

        private static void OnToggle(bool value) => PlayClick();
        private static void OnSlider(float value) => PlayClick();

        private void OnDestroy()
        {
            foreach (var toggle in m_toggles) if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggle);
            foreach (var slider in m_sliders) if (slider != null) slider.onValueChanged.RemoveListener(OnSlider);
        }
    }
}
