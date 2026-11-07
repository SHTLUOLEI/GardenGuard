using GameLogic.Defense90;
using GameProto;

namespace GameLogic
{
    public class DefenseConfigMgr : Singleton<DefenseConfigMgr>
    {
        public const int MODE_NORMAL = 1, MODE_STRESS = 2;
        public const int TIER_FULL = 1, TIER_SIMPLE = 2;

        public DefenseModeConfig GetMode(int modeId) => TbDefenseModeConfig.GetOrDefault(modeId);
        public DefenseVfxTierConfig GetTier(int tierId) => TbDefenseVfxTierConfig.GetOrDefault(tierId);

        public DefenseRules CreateRules(int modeId) => CreateRules(ConfigSystem.Instance.Tables, modeId);

        // 测试可传入直接读取 bytes 的 Tables，不依赖资源模块。
        public static DefenseRules CreateRules(Tables tables, int modeId)
        {
            DefenseModeConfig mode = tables.TbDefenseModeConfig.Get(modeId);
            return new DefenseRules(mode, tables.TbDefenseEnemyConfig.Get(mode.RunnerId),
                tables.TbDefenseEnemyConfig.Get(mode.TankId), tables.TbDefenseSkillConfig.Get(mode.SkillId));
        }
    }
}
