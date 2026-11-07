using GameLogic;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEngine;

namespace Defense90Tests
{
    public sealed class DefenseSettingsTests
    {
        private readonly System.Collections.Generic.Dictionary<string, float> m_saved = new();
        private static readonly string[] s_legacyAudioKeys = { "MusicOn", "MusicVolume", "SfxOn", "SfxVolume" };

        [SetUp] public void SetUp()
        {
            GameEventLauncher.Init();
            m_saved.Clear();
            foreach (string key in AllKeys())
                if (PlayerPrefs.HasKey(key)) m_saved[key] = key.EndsWith("Volume") ? PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            foreach (string name in s_legacyAudioKeys)
            {
                string key = DefenseSettings.KEY_PREFIX + name;
                if (PlayerPrefs.HasKey(key)) m_saved[key] = name.EndsWith("Volume") ? PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            }
            DefenseSettings.ResetAll();
        }

        [TearDown] public void TearDown()
        {
            DefenseSettings.ResetAll();
            foreach (string key in AllKeys()) Assert.IsFalse(PlayerPrefs.HasKey(key), key);
            foreach (string name in s_legacyAudioKeys) PlayerPrefs.DeleteKey(DefenseSettings.KEY_PREFIX + name);
            foreach (var saved in m_saved)
                if (saved.Key.EndsWith("Volume")) PlayerPrefs.SetFloat(saved.Key, saved.Value);
                else PlayerPrefs.SetInt(saved.Key, (int)saved.Value);
            PlayerPrefs.Save(); DefenseSettings.Reload();
        }

        private static System.Collections.Generic.IEnumerable<string> AllKeys()
        {
            yield return "Settings.MusicMuted";
            yield return "Settings.MusicVolume";
            yield return "Settings.SoundMuted";
            yield return "Settings.SoundVolume";
            foreach (string key in new[] { "VfxTier", "ShakeOn", "VibrateOn" }) yield return DefenseSettings.KEY_PREFIX + key;
            yield return DefenseSettings.TUTORIAL_KEY;
        }

        [Test] public void DefaultsAreFullEffectsAndEverythingOn()
        {
            Assert.IsTrue(DefenseSettings.MusicOn); Assert.IsTrue(DefenseSettings.SfxOn);
            Assert.IsTrue(DefenseSettings.ShakeOn); Assert.IsTrue(DefenseSettings.VibrateOn);
            Assert.AreEqual(DefenseConfigMgr.TIER_FULL, DefenseSettings.VfxTier);
            Assert.AreEqual(0.8f, DefenseSettings.MusicVolume, 0.0001f);
            Assert.AreEqual(1f, DefenseSettings.SfxVolume, 0.0001f);
            Assert.IsFalse(DefenseSettings.TutorialDone);
        }

        [Test] public void ValuesPersistThroughPlayerPrefsAndFireChanged()
        {
            int legacyValue = PlayerPrefs.GetInt(DefenseSettings.KEY_PREFIX + "MusicOn", -1);
            var changes = new System.Collections.Generic.List<EDefenseSetting>();
            void OnChanged(EDefenseSetting setting) => changes.Add(setting);
            GameEventHelper.AddEvent.DefenseSettings.OnSettingsChanged(OnChanged);
            try
            {
                DefenseSettings.MusicOn = false; DefenseSettings.MusicVolume = 0.35f;
                DefenseSettings.SfxOn = false; DefenseSettings.SfxVolume = 0.5f;
                DefenseSettings.VfxTier = DefenseConfigMgr.TIER_SIMPLE;
                DefenseSettings.ShakeOn = false; DefenseSettings.VibrateOn = false;
                DefenseSettings.TutorialDone = true;
                DefenseSettings.ShakeOn = false; // 值没变，不应再触发
                CollectionAssert.AreEqual(new[] { EDefenseSetting.Music, EDefenseSetting.Music,
                    EDefenseSetting.Sound, EDefenseSetting.Sound, EDefenseSetting.VfxTier,
                    EDefenseSetting.Shake, EDefenseSetting.Vibrate }, changes);
            }
            finally { GameEventHelper.RemoveEvent.DefenseSettings.OnSettingsChanged(OnChanged); }

            Assert.AreEqual(1, PlayerPrefs.GetInt("Settings.MusicMuted", -1));
            Assert.AreEqual(legacyValue, PlayerPrefs.GetInt(DefenseSettings.KEY_PREFIX + "MusicOn", -1), "旧业务键不再写入");
            Assert.AreEqual(2, PlayerPrefs.GetInt(DefenseSettings.KEY_PREFIX + "VfxTier", -1));
            DefenseSettings.Reload();
            Assert.IsFalse(DefenseSettings.MusicOn); Assert.IsFalse(DefenseSettings.SfxOn);
            Assert.IsFalse(DefenseSettings.ShakeOn); Assert.IsFalse(DefenseSettings.VibrateOn);
            Assert.AreEqual(0.35f, DefenseSettings.MusicVolume, 0.0001f);
            Assert.AreEqual(0.5f, DefenseSettings.SfxVolume, 0.0001f);
            Assert.AreEqual(DefenseConfigMgr.TIER_SIMPLE, DefenseSettings.VfxTier);
            Assert.IsTrue(DefenseSettings.TutorialDone);
        }

        [Test] public void OutOfRangeValuesAreClamped()
        {
            DefenseSettings.MusicVolume = 3f; DefenseSettings.SfxVolume = -1f; DefenseSettings.VfxTier = 9;
            Assert.AreEqual(1f, DefenseSettings.MusicVolume);
            Assert.AreEqual(0f, DefenseSettings.SfxVolume);
            Assert.AreEqual(DefenseConfigMgr.TIER_SIMPLE, DefenseSettings.VfxTier);
        }

        [Test] public void LegacyAudioMigratesOnceAndFrameworkKeysRemainAuthoritative()
        {
            PlayerPrefs.SetInt(DefenseSettings.KEY_PREFIX + "MusicOn", 0);
            PlayerPrefs.SetInt(DefenseSettings.KEY_PREFIX + "SfxOn", 0);
            PlayerPrefs.SetFloat(DefenseSettings.KEY_PREFIX + "MusicVolume", .35f);
            PlayerPrefs.SetFloat(DefenseSettings.KEY_PREFIX + "SfxVolume", .55f);
            DefenseSettings.MigrateAudioPreferences();
            Assert.IsFalse(DefenseSettings.MusicOn); Assert.IsFalse(DefenseSettings.SfxOn);
            Assert.AreEqual(.35f, DefenseSettings.MusicVolume); Assert.AreEqual(.55f, DefenseSettings.SfxVolume);
            PlayerPrefs.SetInt(DefenseSettings.KEY_PREFIX + "MusicOn", 1);
            PlayerPrefs.SetFloat(DefenseSettings.KEY_PREFIX + "MusicVolume", .9f);
            DefenseSettings.MigrateAudioPreferences();
            Assert.IsFalse(DefenseSettings.MusicOn); Assert.AreEqual(.35f, DefenseSettings.MusicVolume);
            Assert.IsTrue(PlayerPrefs.HasKey(DefenseSettings.KEY_PREFIX + "MusicOn"), "迁移不删除历史用户键");
        }

        [Test] public void NumberCacheMatchesToStringAndKillAddsBang()
        {
            foreach (int value in new[] { 0, 7, 999, 1000, 4821 })
            {
                Assert.AreEqual(value.ToString(), DefenseNumbers.Get(value));
                Assert.AreEqual(value + "!", DefenseNumbers.GetKill(value));
            }
            Assert.AreSame(DefenseNumbers.Get(42), DefenseNumbers.Get(42), "0-999 必须命中缓存，不重复分配");
        }
    }
}
