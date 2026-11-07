using DG.Tweening;
using GameLogic.Defense90;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class DefenseResultWindow : UIWindow
    {
        public const string WIN_TITLE = "守住啦！", LOSE_TITLE = "再试一次", SCORE_LABEL = "本局得分", NEW_RECORD = "新纪录";
        public const string AGAIN = "再守一局", HOME = "返回首页";
        private const float SCORE_ROLL_DELAY = 0.25f, SCORE_ROLL_TIME = 0.8f, ROW_DELAY = 0.5f, ROW_STEP = 0.08f;
        private static readonly string[] s_statKeys = { "Time", "Kills", "Tank", "Combo", "Skill", "Damage" };
        private static readonly string[] s_statLabels = { "坚持时间", "击退数", "重甲击退", "最高连击", "单次技能最多击退", "基地受到伤害" };

        private Button m_restart, m_home;
        private Text m_title, m_scoreLabel, m_score, m_best, m_newRecordText;
        private GameObject m_newRecord, m_iconWin, m_iconLose;
        private Image m_shade;
        private RectTransform m_popup;
        private readonly Text[] m_statValues = new Text[s_statKeys.Length];
        private readonly RectTransform[] m_statRows = new RectTransform[s_statKeys.Length];
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
            m_restart = FindChildComponent<Button>(root + "m_btnRestart");
            m_home = FindChildComponent<Button>(root + "m_btnMenu");
            m_title = FindChildComponent<Text>(root + "m_textResultTitle");
            m_scoreLabel = FindChildComponent<Text>(root + "m_textResultLabel");
            m_score = FindChildComponent<Text>(root + "m_textResultScore");
            m_best = FindChildComponent<Text>(root + "m_textResultBest");
            m_iconWin = FindChild(root + "m_imgIconWin").gameObject;
            m_iconLose = FindChild(root + "m_imgIconLose").gameObject;
            m_newRecord = FindChild(root + "m_goNewRecord").gameObject;
            m_newRecordText = FindChildComponent<Text>(root + "m_goNewRecord/m_textNewRecord");
            for (int i = 0; i < s_statKeys.Length; i++)
            {
                string row = root + "m_goStats/m_goStatRow" + s_statKeys[i] + "/";
                m_statRows[i] = (RectTransform)FindChild(row.TrimEnd('/'));
                FindChildComponent<Text>(row + "m_textStatLabel" + s_statKeys[i]).text = s_statLabels[i];
                m_statValues[i] = FindChildComponent<Text>(row + "m_textStat" + s_statKeys[i]);
            }
            m_scoreLabel.text = SCORE_LABEL;
            m_newRecordText.text = NEW_RECORD;
            m_restart.GetComponentInChildren<Text>().text = AGAIN;
            m_home.GetComponentInChildren<Text>().text = HOME;
        }

        protected override void BindMemberProperty() => SetUIFit(FindChild("m_rectSafe") as RectTransform);

        protected override void RegisterEvent()
        {
            gameObject.AddComponent<DefenseUIAudio>();
            m_restart.onClick.AddListener(Restart);
            m_home.onClick.AddListener(Home);
        }

        protected override void OnRefresh()
        {
            DefenseUIAudio.PlayClick();
            m_battle = (DefenseWindow)UserData;
            DefenseGame game = m_battle.View;
            DefenseRound round = game.Round;
            bool won = round.State == EDefenseState.Won;
            m_title.text = won ? WIN_TITLE : LOSE_TITLE;
            m_iconWin.SetActive(won);
            m_iconLose.SetActive(!won);
            m_best.text = $"最佳  {game.BestScore:00000}";
            m_newRecord.SetActive(game.NewRecord);
            int elapsed = Mathf.FloorToInt(round.Elapsed);
            m_statValues[0].text = $"{elapsed / 60:00}:{elapsed % 60:00}";
            m_statValues[1].text = DefenseNumbers.Get(round.Kills);
            m_statValues[2].text = DefenseNumbers.Get(round.TankKills);
            m_statValues[3].text = DefenseNumbers.Get(round.MaxCombo);
            m_statValues[4].text = DefenseNumbers.Get(round.BestSkillKills);
            m_statValues[5].text = DefenseNumbers.Get(Mathf.RoundToInt(round.DamageTaken));
            PlayEntrance(round.Score);
        }

        // 面板弹出；分数从 0 滚动到最终值，统计行依次错峰弹出。
        private void PlayEntrance(int score)
        {
            DefenseTween.PopIn(m_shade, m_popup);
            DOTween.Kill(this);
            m_score.text = DefenseNumbers.Get(0);
            DOVirtual.Float(0, score, SCORE_ROLL_TIME, value => m_score.text = Mathf.RoundToInt(value).ToString("00000"))
                .SetDelay(SCORE_ROLL_DELAY).SetEase(Ease.OutCubic).SetId(this).SetUpdate(true);
            for (int i = 0; i < m_statRows.Length; i++)
            {
                m_statRows[i].localScale = Vector3.zero;
                m_statRows[i].DOScale(1, 0.3f).SetEase(Ease.OutBack).SetDelay(ROW_DELAY + i * ROW_STEP).SetId(this).SetUpdate(true);
            }
        }

        private void Restart() => m_battle.View.StartRound();
        private void Home() => m_battle.ReturnHome();

        // 供UI与测试直接触发，避免依赖按钮层级查找。
        public void Replay() => Restart();

        protected override void OnDestroy()
        {
            DOTween.Kill(this);
            DefenseTween.Kill(m_shade, m_popup);
            m_restart.onClick.RemoveListener(Restart);
            m_home.onClick.RemoveListener(Home);
        }
    }
}
