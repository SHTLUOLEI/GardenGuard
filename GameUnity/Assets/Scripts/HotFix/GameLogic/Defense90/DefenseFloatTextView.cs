using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Defense90
{
    // 飘字对象池：固定数量的 Text 在 prefab 中预先创建，按 Round.Texts 每帧更新，不新建对象、不拼接字符串。
    public sealed class DefenseFloatTextView : MonoBehaviour
    {
        [SerializeField] private Text[] m_items;
        [SerializeField] private Color m_normalColor = Color.white, m_skillColor = Color.cyan;
        [SerializeField] private int m_normalSize = 22, m_skillSize = 34;
        [SerializeField] private float m_riseDistance = 46;
        // 0 表示该 Text 当前隐藏，其余为 (数值+1)*4+标记，用来判断是否需要重写文字和字号。
        private int[] m_keys;

        // 在开局初始化，避免第一次击杀时构建2000个字符串和动态字体字形。
        public void Warmup()
        {
            DefenseNumbers.Warmup();
            m_keys = new int[m_items.Length];
            if (m_items.Length == 0) return;
            Text item = m_items[0];
            if (item.font != null && item.font.dynamic)
            {
                item.font.RequestCharactersInTexture("0123456789!", m_normalSize, item.fontStyle);
                item.font.RequestCharactersInTexture("0123456789!", m_skillSize, item.fontStyle);
            }
        }

        public void Refresh(DefenseRound round, bool visible, DefenseBattlefield battlefield, Camera uiCamera)
        {
            int count = m_items.Length;
            if (m_keys == null) m_keys = new int[count];
            DefenseFloatText[] texts = round.Texts;
            for (int slot = 0; slot < count; slot++)
            {
                // Round.Texts 是环形缓冲，条目多于 Text 数量时同槽位保留最新的一条，即复用最老的。
                int index = -1; float life = 0;
                if (visible)
                    for (int i = slot; i < texts.Length; i += count)
                        if (texts[i].Life > life) { life = texts[i].Life; index = i; }
                Text item = m_items[slot];
                if (index < 0)
                {
                    if (m_keys[slot] != 0) { m_keys[slot] = 0; item.gameObject.SetActive(false); }
                    continue;
                }
                DefenseFloatText data = texts[index];
                int key = (data.Value + 1) * 4 + (data.Kill ? 1 : 0) + (data.Skill ? 2 : 0);
                if (m_keys[slot] == 0) item.gameObject.SetActive(true);
                if (m_keys[slot] != key)
                {
                    m_keys[slot] = key;
                    item.text = data.Kill ? DefenseNumbers.GetKill(data.Value) : DefenseNumbers.Get(data.Value);
                    item.fontSize = data.Skill ? m_skillSize : m_normalSize;
                }
                float age = 1 - life / DefenseRound.TEXT_LIFE;
                Color color = data.Skill ? m_skillColor : m_normalColor;
                color.a *= Mathf.Clamp01(life / (DefenseRound.TEXT_LIFE * 0.5f));
                item.color = color;
                Vector3 world = battlefield.WorldPoint(data.Position + new Vector2(0, m_riseDistance * age));
                Vector2 screen = battlefield.WorldCamera.WorldToScreenPoint(world);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)item.transform.parent, screen, uiCamera, out Vector2 local);
                item.rectTransform.localPosition = local;
            }
        }
    }

    // 0-999 的数字字符串缓存，避免界面每帧分配。
    public static class DefenseNumbers
    {
        private const int CACHE_SIZE = 1000;
        private static string[] s_plain, s_kill;

        public static void Warmup()
        {
            if (s_plain == null) Build();
        }

        public static string Get(int value)
        {
            if (value < 0 || value >= CACHE_SIZE) return value.ToString();
            if (s_plain == null) Build();
            return s_plain[value];
        }

        // 击杀伤害带感叹号。
        public static string GetKill(int value)
        {
            if (value < 0 || value >= CACHE_SIZE) return value + "!";
            if (s_kill == null) Build();
            return s_kill[value];
        }

        private static void Build()
        {
            s_plain = new string[CACHE_SIZE]; s_kill = new string[CACHE_SIZE];
            for (int i = 0; i < CACHE_SIZE; i++) { s_plain[i] = i.ToString(); s_kill[i] = s_plain[i] + "!"; }
        }
    }
}
