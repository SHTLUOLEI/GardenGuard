using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Defense90
{
    public enum DefenseTutorialPhase { Intro, AutoFire, Skill, Countdown, Complete }

    // 说明只点一次；之后由实际击杀与技能操作推进，倒计时结束才开始正式回合。
    public sealed class DefenseTutorial : MonoBehaviour
    {
        public const string STEP_1 = "小水炮会自动攻击靠近的小怪";
        public const string STEP_2 = "按住技能，拖到怪群后松手";
        public const string TAP_HINT = "点击开始演示";

        [SerializeField] private GameObject[] m_stepBubbles;
        [SerializeField] private Text[] m_stepTexts, m_tapTexts;
        // 说明与倒计时压暗战场；实操阶段保留技能高亮并让出输入。
        [SerializeField] private GameObject m_dimBelowSkill, m_dimAboveSkill, m_skillSpot;
        [SerializeField] private Button m_nextButton;
        private Action m_autoFire, m_skill, m_countdown, m_finished;
        private float m_clearTime, m_countdownTime;
        private int m_titleSize;
        private Vector2 m_introPosition;

        public bool Active { get; private set; }
        public bool Suspended { get; set; }
        public DefenseTutorialPhase Phase { get; private set; }
        public int Step => (int)Phase;
        public int Countdown => Mathf.CeilToInt(m_countdownTime);

        public void Begin(Action autoFire, Action skill, Action countdown, Action finished)
        {
            m_autoFire = autoFire; m_skill = skill; m_countdown = countdown; m_finished = finished;
            Phase = DefenseTutorialPhase.Intro; Active = true; Suspended = false; m_clearTime = 0;
            if (m_titleSize == 0)
            {
                m_titleSize = m_stepTexts[0].fontSize;
                m_introPosition = ((RectTransform)m_stepBubbles[0].transform).anchoredPosition;
            }
            m_stepTexts[0].fontSize = m_titleSize;
            m_nextButton.onClick.RemoveListener(Next);
            m_nextButton.onClick.AddListener(Next);
            gameObject.SetActive(true);
            ShowStep();
        }

        public void Next()
        {
            if (!Active || Phase != DefenseTutorialPhase.Intro) return;
            Phase = DefenseTutorialPhase.AutoFire;
            ShowStep(); m_autoFire();
        }

        public void Tick(float delta, DefenseRound round, bool aiming)
        {
            if (!Active || Suspended) return;
            if (Phase == DefenseTutorialPhase.Countdown)
            {
                m_countdownTime = Mathf.Max(0, m_countdownTime - delta);
                m_stepTexts[0].text = Countdown.ToString();
                if (m_countdownTime <= 0)
                {
                    Phase = DefenseTutorialPhase.Complete;
                    Hide(); m_finished();
                }
                return;
            }
            if (Phase != DefenseTutorialPhase.AutoFire && Phase != DefenseTutorialPhase.Skill) return;
            if (round.State != EDefenseState.Playing) return;
            if (Phase == DefenseTutorialPhase.Skill)
                m_stepTexts[1].text = aiming ? "拖到怪群，松手释放" : STEP_2;
            bool cleared = round.ActiveCount == 0 && round.SkillAge < 0;
            m_clearTime = cleared ? m_clearTime + delta : 0;
            if (m_clearTime < .55f) return;
            m_clearTime = 0;
            if (Phase == DefenseTutorialPhase.AutoFire)
            {
                Phase = DefenseTutorialPhase.Skill;
                ShowStep(); m_skill();
            }
            else
            {
                Phase = DefenseTutorialPhase.Countdown; m_countdownTime = 3;
                ShowStep(); m_countdown();
            }
        }

        public void Hide()
        {
            Active = false; Suspended = false;
            gameObject.SetActive(false);
        }

        private void ShowStep()
        {
            bool intro = Phase == DefenseTutorialPhase.Intro, skill = Phase == DefenseTutorialPhase.Skill;
            bool countdown = Phase == DefenseTutorialPhase.Countdown;
            // 自动演示的提示移到上方，完整露出下方炮台、弹道和站定怪物。
            ((RectTransform)m_stepBubbles[0].transform).anchoredPosition = m_introPosition
                + (Phase == DefenseTutorialPhase.AutoFire ? Vector2.up * 600 : Vector2.zero);
            for (int i = 0; i < m_stepBubbles.Length; i++) m_stepBubbles[i].SetActive(i == (skill ? 1 : 0));
            m_stepTexts[0].text = countdown ? "3" : intro ? STEP_1 : "看，小水炮会自动消灭它们";
            m_stepTexts[0].fontSize = countdown ? Mathf.Max(48, m_titleSize) : m_titleSize;
            m_stepTexts[1].text = STEP_2;
            m_tapTexts[0].text = countdown ? "准备开始" : intro ? TAP_HINT : "无需操作";
            m_tapTexts[1].text = "消灭全部小怪，开始正式防守";
            m_dimBelowSkill.SetActive(false); m_dimAboveSkill.SetActive(intro || countdown);
            m_skillSpot.SetActive(skill);
            // 原 Prefab 关闭了按钮射线接收；说明阶段恢复，实际演示时放开战场交互。
            m_nextButton.gameObject.SetActive(intro || countdown);
            m_nextButton.interactable = intro;
            m_nextButton.targetGraphic.raycastTarget = intro || countdown;
        }

        private void OnDestroy() => m_nextButton.onClick.RemoveListener(Next);
    }
}
