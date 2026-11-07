using DGame;

namespace GameLogic
{
    public enum EDefenseSetting
    {
        Music,
        Sound,
        VfxTier,
        Shake,
        Vibrate,
    }

    [EventInterface(EEventGroup.GroupLogic)]
    public interface IDefenseSettings
    {
        void OnSettingsChanged(EDefenseSetting setting);
    }
}
