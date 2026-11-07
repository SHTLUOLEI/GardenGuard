namespace GameLogic.Defense90
{
    // 完整/简化两档的可见差异说明，编辑器对比面板和设置页共用；改动特效分档时同步更新。
    public static class DefenseVfxTierInfo
    {
        public readonly struct Row
        {
            public readonly string Effect, Full, Simple;
            public Row(string effect, string full, string simple) { Effect = effect; Full = full; Simple = simple; }
        }

        public static readonly Row[] Rows =
        {
            new Row("范围提示", "旋转虚线环、描边、焦散水面、气泡、中心准星", "单实线环、平涂"),
            new Row("水柱", "流动水纹分层、高光带、气泡、底部泡沫裙边、抖动边", "直边双色柱、描边"),
            new Row("命中扩散", "三层波纹水环、弹射水滴、焦散", "单环"),
            new Row("普攻水花", "双环、水滴（普通/击杀数量读表）", "单环、少量水滴（读表）"),
            new Row("普攻水弹", "彗星拖尾", "圆形水滴"),
            new Row("击杀反馈", "水泡高光鼓起后破裂飞溅、金币星芒闪光", "水泡破裂、无闪光"),
            new Row("震屏、伤害飘字", "开", "关"),
        };

        public const string SIMPLE_SUMMARY = "简化：关闭水纹、高光、气泡、焦散、拖尾、闪光、震屏和飘字，保留轮廓与范围";
    }
}
