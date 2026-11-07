using GameProto;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 独立的 UI 点击音效组件，适用于不继承 <see cref="BaseUIButton"/> 的 UI 节点。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonSound : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private int m_clickSoundId = (int)SysSoundID.BTN_CLICK;
        private Button m_button;
        private static float s_lastSoundTime = float.NegativeInfinity;
        public float MinInterval { get; set; }

        private void Awake()
        {
            m_button = GetComponent<Button>();
            if (m_button != null) m_button.onClick.AddListener(Play);
        }

        private void OnDestroy()
        {
            if (m_button != null) m_button.onClick.RemoveListener(Play);
        }

        public static void PlayDefault() => PlaySound((int)SysSoundID.BTN_CLICK, .06f);

        public void Play() => PlaySound(m_clickSoundId, MinInterval);

        private static void PlaySound(int soundId, float interval)
        {
            if (!GameModule.AudioModule.UISoundEnable || Time.unscaledTime - s_lastSoundTime < interval) return;
            s_lastSoundTime = Time.unscaledTime;
            SoundConfigMgr.Instance.Play(soundId, DGame.AudioType.UISound, isInPool: true);
        }

        /// <summary>
        /// 当前点击音效 ID。默认值为 1000002（<see cref="SysSoundID.BTN_CLICK"/>）。
        /// </summary>
        public int ClickSoundId
        {
            get => m_clickSoundId;
            set => m_clickSoundId = value;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (m_button != null || eventData.button != PointerEventData.InputButton.Left) return;
            var selectable = GetComponent<Selectable>();
            if (selectable == null || selectable.IsInteractable()) Play();
        }
    }
}
