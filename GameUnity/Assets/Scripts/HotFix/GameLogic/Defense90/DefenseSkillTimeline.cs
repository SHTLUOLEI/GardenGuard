using UnityEngine;

namespace GameLogic.Defense90
{
    [CreateAssetMenu(menuName = "Defense90/水系表现时间轴")]
    public sealed class DefenseSkillTimeline : ScriptableObject
    {
        [Min(0.1f)] public float ColumnHeight = 3.5f;
        [Range(0.1f, 1)] public float ColumnWidth = 0.45f;
        [Range(0, 0.15f)] public float HitStop = 0.045f;
        [Range(0, 0.5f)] public float KnockDistance = 0.22f;
        [Min(0.02f)] public float ShotTravel = 0.09f;
        public Color Water = new Color(0.31f, 0.765f, 0.91f, 1);
        public Color Foam = new Color(1, 0.99f, 0.96f, 1);
        [Tooltip("卡通描边与深水色")] public Color Outline = new Color(0.118f, 0.42f, 0.52f, 1);
        [Tooltip("完整档技能命中扩散的弹射水滴数；简化档不弹射")]
        [Range(0, 24)] public int SkillDroplets = 12;

        // 时长来自 Rules/Luban；这里只决定阶段内的表现，不复制伤害或冷却。
        public static Vector2 Evaluate(float age, float telegraph, float release, float hit)
        {
            if (age < 0) return new Vector2(-1, 0);
            // 浮点求和的末位误差不应让时间轴在边界多停留一帧。
            const float epsilon = 0.000001f;
            if (age + epsilon < telegraph) return new Vector2(0, Mathf.Clamp01(age / telegraph));
            if (age + epsilon < telegraph + release) return new Vector2(1, Mathf.Clamp01((age - telegraph) / release));
            if (age + epsilon < telegraph + release + hit) return new Vector2(2, Mathf.Clamp01((age - telegraph - release) / hit));
            return new Vector2(-1, 0);
        }
    }
}
