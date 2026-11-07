using UnityEngine;

namespace GameLogic.Defense90
{
    // 场景和技能编辑器共用；显式 age 驱动，拖时间轴不会依赖 Time.time。
    public sealed class DefenseSkillPresentation : MonoBehaviour
    {
        public DefenseSkillTimeline Timeline;
        public SpriteRenderer Area, Column, Splash;
        private MaterialPropertyBlock m_properties;
        public int Phase { get; private set; } = -1;

        public void Sample(Vector3 center, float radius, float age, float telegraph, float release, float hit, bool detail)
        {
            Vector2 stage = DefenseSkillTimeline.Evaluate(age, telegraph, release, hit);
            Phase = (int)stage.x;
            transform.position = center;
            Area.enabled = Phase >= 0;
            Column.enabled = Phase == 1;
            Splash.enabled = Phase >= 1;
            if (Phase < 0) return;
            float t = stage.y;
            Area.transform.localPosition = Vector3.zero;
            Area.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
            Set(Area, 0, Phase == 0 ? t : 1, Phase == 0 ? 1 : 1 - t, detail);
            float height = Timeline.ColumnHeight * (1 - 0.8f * t);
            Column.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            Column.transform.localScale = new Vector3(radius * Timeline.ColumnWidth, height, 1);
            Set(Column, 1, t, 1 - t * 0.65f, detail);
            float spread = Phase == 1 ? Mathf.Lerp(0.3f, 1, t) : Mathf.Lerp(1, 1.25f, t);
            Splash.transform.localScale = Vector3.one * radius * 2 * spread;
            Set(Splash, 2, Phase == 1 ? t * 0.5f : 0.5f + t * 0.5f, Phase == 1 ? 1 : 1 - t, detail);
        }

        public void Aim(Vector3 center, float radius, bool valid, float time, float alpha = 1)
        {
            Phase = -1; transform.position = center;
            Area.enabled = true; Column.enabled = Splash.enabled = false;
            Area.transform.localScale = Vector3.one * radius * 2;
            Set(Area, 0, Mathf.Repeat(time * 0.6f, 1), alpha, true, valid);
        }

        private void Set(SpriteRenderer renderer, int mode, float progress, float alpha, bool detail, bool valid = true)
        {
            if (m_properties == null) m_properties = new MaterialPropertyBlock();
            m_properties.Clear();
            m_properties.SetFloat("_Mode", mode);
            m_properties.SetFloat("_Progress", progress);
            m_properties.SetFloat("_Opacity", alpha);
            m_properties.SetFloat("_Detail", detail ? 1 : 0);
            m_properties.SetColor("_Water", valid ? Timeline.Water : Color.gray);
            m_properties.SetColor("_Foam", Timeline.Foam);
            m_properties.SetColor("_Deep", Timeline.Outline);
            // 只有完整档的命中扩散弹射水滴；简化档保持单环。
            m_properties.SetFloat("_Droplets", detail && mode == 2 ? Timeline.SkillDroplets : 0);
            renderer.SetPropertyBlock(m_properties);
        }
    }
}
