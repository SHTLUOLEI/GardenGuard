using DGame;
using UnityEngine;
using Unity.Profiling;

namespace GameLogic.Defense90
{
    // 战斗音频规则：击杀限流、随机音调、连击升调和胜负切换；播放归框架管理。
    public sealed class DefenseAudio : MonoBehaviour
    {
        private static readonly ProfilerMarker WarmupMarker = new ProfilerMarker("Defense90.AudioWarmup");
        private const float PITCH_VARIANCE = 0.05f, COMBO_PITCH_STEP = 0.1f, COMBO_PITCH_MAX = 0.5f;
        private DefenseAssets m_assets;
        private AudioSourceAgent m_music;
        private AudioClip m_requestedMusic;
        private int m_comboLevel;
        private const float IMPACT_SOUND_INTERVAL = .05f;
        private float m_nextSplash, m_nextPop, m_nextHeavy;
        private System.Random m_random = new System.Random();

        public void Initialize(DefenseAssets assets)
        {
            StopBGM();
            m_assets = assets;
            m_comboLevel = 0;
            m_nextSplash = m_nextPop = m_nextHeavy = 0;
            using var warmupSample = WarmupMarker.Auto();
            // 小音效提前请求解码，命中时不排队补播已经过期的声音。
            foreach (var clip in new[] { assets.SfxShoot, assets.SfxSplash, assets.SfxHeavyDown,
                assets.SfxBaseHit, assets.SfxSkillCharge, assets.SfxSkillSlam, assets.SfxCoin,
                assets.SfxPanel, assets.SfxCombo, assets.JingleVictory, assets.JingleDefeat })
                if (clip != null) AudioClipLoading.Request(clip);
            if (assets.SfxEnemyPop != null)
                foreach (var clip in assets.SfxEnemyPop)
                    if (clip != null) AudioClipLoading.Request(clip);
        }

        public void PlayBGM(AudioClip clip, float volume)
        {
            if (clip == null) return;
            if (m_requestedMusic == clip && m_music != null && !m_music.IsFree)
            {
                m_music.Volume = volume;
                return;
            }
            StopBGM();
            m_requestedMusic = clip;
            var options = AudioPlayOptions.CreateDefault();
            options.IsLoop = true;
            options.Volume = volume;
            m_music = GameModule.AudioModule.PlayClip(DGame.AudioType.Music, clip, options);
        }

        public void StopBGM()
        {
            m_requestedMusic = null;
            m_music?.Stop();
            m_music = null;
        }

        public void PlaySFX(AudioClip clip, float baseVolume, bool randomPitch = true)
        {
            if (clip == null) return;
            PlayEffect(clip, baseVolume, randomPitch ? 1f + (float)(m_random.NextDouble() * 2 - 1) * PITCH_VARIANCE : 1f);
        }

        private static void PlayEffect(AudioClip clip, float volume, float pitch)
        {
            var options = AudioPlayOptions.CreateDefault();
            options.Volume = volume;
            options.PitchRange = new Vector2(pitch, pitch);
            GameModule.AudioModule.PlayClip(DGame.AudioType.Sound, clip, options);
        }

        public void Shutdown()
        {
            StopBGM();
            // 战斗是 Sound 分组的当前所有者，先停掉借用资源再释放 DefenseAssets。
            GameModule.AudioModule.Stop(DGame.AudioType.Sound, false);
            m_assets = null;
        }

        public void PlayShoot() => PlaySFX(m_assets.SfxShoot, 0.22f);
        public void PlaySplash()
        {
            float now = Time.unscaledTime;
            if (now < m_nextSplash) return;
            m_nextSplash = now + IMPACT_SOUND_INTERVAL;
            PlaySFX(m_assets.SfxSplash, .3f);
        }

        public void PlayEnemyPop()
        {
            float now = Time.unscaledTime;
            if (now < m_nextPop) return;
            m_nextPop = now + IMPACT_SOUND_INTERVAL;
            if (m_assets.SfxEnemyPop != null && m_assets.SfxEnemyPop.Length > 0)
            {
                int idx = m_random.Next(m_assets.SfxEnemyPop.Length);
                PlaySFX(m_assets.SfxEnemyPop[idx], 0.4f);
            }
        }

        public void PlayHeavyDown()
        {
            float now = Time.unscaledTime;
            if (now < m_nextHeavy) return;
            m_nextHeavy = now + IMPACT_SOUND_INTERVAL;
            PlaySFX(m_assets.SfxHeavyDown, .6f);
        }
        public void PlayBaseHit() => PlaySFX(m_assets.SfxBaseHit, 0.5f);
        public void PlaySkillCharge() => PlaySFX(m_assets.SfxSkillCharge, 0.35f);
        public void PlaySkillSlam() => PlaySFX(m_assets.SfxSkillSlam, 0.3f);
        public void PlayCoin() => PlaySFX(m_assets.SfxCoin, 0.5f);
        public void PlayPanel() => PlaySFX(m_assets.SfxPanel, 0.35f);

        public void PlayVictory()
        {
            StopBGM();
            PlayEffect(m_assets.JingleVictory, .7f, 1f);
        }

        public void PlayDefeat()
        {
            StopBGM();
            PlayEffect(m_assets.JingleDefeat, .7f, 1f);
        }

        // 连击：每次连击提升音调，最多 +0.5（5 连）。
        public void PlayCombo(int combo)
        {
            m_comboLevel = Mathf.Min(combo, (int)(COMBO_PITCH_MAX / COMBO_PITCH_STEP));
            float pitch = 1f + m_comboLevel * COMBO_PITCH_STEP;
            PlayEffect(m_assets.SfxCombo, .45f, pitch);
        }

        public void ResetCombo() => m_comboLevel = 0;
    }
}
