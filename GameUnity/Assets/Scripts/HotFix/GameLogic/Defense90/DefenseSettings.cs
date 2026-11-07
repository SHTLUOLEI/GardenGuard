using DGame;
using UnityEngine;
using PlayerPrefsUtil = DGame.Utility.PlayerPrefsUtil;

namespace GameLogic.Defense90
{
    // 玩家设置的存储与音频同步入口；提交后通过项目事件通知消费者。
    public static class DefenseSettings
    {
        public const string KEY_PREFIX = "Defense90.Settings.";
        public const string TUTORIAL_KEY = "Defense90.TutorialDone";
        private const string MUSIC_ON = Constant.Settings.MUSIC_MUTED, MUSIC_VOLUME = Constant.Settings.MUSIC_VOLUME;
        private const string SFX_ON = Constant.Settings.SOUND_MUTED, SFX_VOLUME = Constant.Settings.SOUND_VOLUME;
        private const string VFX_TIER = KEY_PREFIX + "VfxTier", SHAKE_ON = KEY_PREFIX + "ShakeOn";
        private const string VIBRATE_ON = KEY_PREFIX + "VibrateOn";
        private static readonly string[] s_keys =
            { MUSIC_ON, MUSIC_VOLUME, SFX_ON, SFX_VOLUME, VFX_TIER, SHAKE_ON, VIBRATE_ON };

        private static IAudioModule s_audioModule;

        public static void Initialize()
        {
            MigrateAudioPreferences();
            s_audioModule = GameModule.AudioModule;
            ApplyAudioSettings();
        }

        public static void Shutdown() => s_audioModule = null;

        private static void ApplyAudioSettings() => s_audioModule?.ApplySettingsFromPreferences();

        // 只在框架键尚不存在时搬入旧设置，之后框架键是唯一读取来源；保留历史键以免删除用户数据。
        public static void MigrateAudioPreferences()
        {
            MigrateSwitch(KEY_PREFIX + "MusicOn", MUSIC_ON);
            MigrateSwitch(KEY_PREFIX + "SfxOn", SFX_ON);
            MigrateVolume(KEY_PREFIX + "MusicVolume", MUSIC_VOLUME);
            MigrateVolume(KEY_PREFIX + "SfxVolume", SFX_VOLUME);
            PlayerPrefsUtil.Save();
        }

        private static void MigrateSwitch(string legacyKey, string targetKey)
        {
            if (!PlayerPrefsUtil.HasKey(targetKey) && PlayerPrefsUtil.HasKey(legacyKey))
                PlayerPrefsUtil.SetInt(targetKey, PlayerPrefsUtil.GetInt(legacyKey) == 1 ? 0 : 1, save: false);
        }

        private static void MigrateVolume(string legacyKey, string targetKey)
        {
            if (!PlayerPrefsUtil.HasKey(targetKey) && PlayerPrefsUtil.HasKey(legacyKey))
                PlayerPrefsUtil.SetFloat(targetKey, Mathf.Clamp01(PlayerPrefsUtil.GetFloat(legacyKey)), save: false);
        }
        private static bool s_loaded;
        private static bool s_shakeOn, s_vibrateOn;
        private static int s_vfxTier;

        public static bool MusicOn
        {
            get => PlayerPrefsUtil.GetInt(MUSIC_ON, 0) == 0;
            set { if (MusicOn != value) Save(MUSIC_ON, !value, EDefenseSetting.Music); }
        }

        public static bool SfxOn
        {
            get => PlayerPrefsUtil.GetInt(SFX_ON, 0) == 0;
            set { if (SfxOn != value) Save(SFX_ON, !value, EDefenseSetting.Sound); }
        }

        public static bool ShakeOn
        {
            get { Load(); return s_shakeOn; }
            set { Load(); if (s_shakeOn != value) { s_shakeOn = value; Save(SHAKE_ON, value, EDefenseSetting.Shake); } }
        }

        public static bool VibrateOn
        {
            get { Load(); return s_vibrateOn; }
            set { Load(); if (s_vibrateOn != value) { s_vibrateOn = value; Save(VIBRATE_ON, value, EDefenseSetting.Vibrate); } }
        }

        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefsUtil.GetFloat(MUSIC_VOLUME, .8f));
            set
            {
                value = Mathf.Clamp01(value);
                if (MusicVolume != value) Save(MUSIC_VOLUME, value, EDefenseSetting.Music);
            }
        }

        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefsUtil.GetFloat(SFX_VOLUME, 1f));
            set
            {
                value = Mathf.Clamp01(value);
                if (SfxVolume != value) Save(SFX_VOLUME, value, EDefenseSetting.Sound);
            }
        }

        // 1 完整 / 2 简化，对应 DefenseVfxTierConfig 的 ID。
        public static int VfxTier
        {
            get { Load(); return s_vfxTier; }
            set
            {
                value = Mathf.Clamp(value, DefenseConfigMgr.TIER_FULL, DefenseConfigMgr.TIER_SIMPLE);
                Load();
                if (s_vfxTier == value) return;
                s_vfxTier = value; PlayerPrefsUtil.SetInt(VFX_TIER, value, save: false); Commit(EDefenseSetting.VfxTier);
            }
        }

        public static bool TutorialDone
        {
            get => PlayerPrefsUtil.GetInt(TUTORIAL_KEY, 0) == 1;
            set => PlayerPrefsUtil.SetInt(TUTORIAL_KEY, value ? 1 : 0);
        }

        // 清除全部设置键（含引导标记）并回到默认值，供测试使用。
        public static void ResetAll()
        {
            foreach (string key in s_keys) PlayerPrefsUtil.DeleteKey(key);
            PlayerPrefsUtil.DeleteKey(TUTORIAL_KEY);
            PlayerPrefsUtil.Save();
            s_loaded = false;
        }

        // 丢弃内存缓存，下次访问重新读取 PlayerPrefs。
        public static void Reload() => s_loaded = false;

        private static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            s_shakeOn = PlayerPrefsUtil.GetInt(SHAKE_ON, 1) == 1;
            s_vibrateOn = PlayerPrefsUtil.GetInt(VIBRATE_ON, 1) == 1;
            s_vfxTier = Mathf.Clamp(PlayerPrefsUtil.GetInt(VFX_TIER, DefenseConfigMgr.TIER_FULL),
                DefenseConfigMgr.TIER_FULL, DefenseConfigMgr.TIER_SIMPLE);
        }

        private static void Save(string key, bool value, EDefenseSetting setting)
        {
            PlayerPrefsUtil.SetInt(key, value ? 1 : 0, save: false);
            Commit(setting);
        }

        private static void Save(string key, float value, EDefenseSetting setting)
        {
            PlayerPrefsUtil.SetFloat(key, value, save: false);
            Commit(setting);
        }

        private static void Commit(EDefenseSetting setting)
        {
            PlayerPrefsUtil.Save();
            // 消费者可能恢复播放，必须先应用音量与静音状态。
            if (setting == EDefenseSetting.Music || setting == EDefenseSetting.Sound) ApplyAudioSettings();
            // 启动前允许保存偏好；事件代理由 GameStart 初始化后才存在。
            GameEvent.Get<IDefenseSettings>()?.OnSettingsChanged(setting);
        }
    }
}
