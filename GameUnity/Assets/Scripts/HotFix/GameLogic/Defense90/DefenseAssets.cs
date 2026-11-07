using UnityEngine;

namespace GameLogic.Defense90
{
    [CreateAssetMenu(menuName = "Defense90/表现资源")]
    public sealed class DefenseAssets : ScriptableObject
    {
        public Material PulseMaterial;

        [Header("Background Music")]
        public AudioClip MusicBattle;
        public AudioClip MusicHome;

        [Header("Jingles")]
        public AudioClip JingleVictory;
        public AudioClip JingleDefeat;

        [Header("Gameplay SFX")]
        public AudioClip SfxShoot;
        public AudioClip SfxSplash;
        public AudioClip[] SfxEnemyPop; // 3 variants
        public AudioClip SfxHeavyDown;
        public AudioClip SfxBaseHit;

        [Header("Skill SFX")]
        public AudioClip SfxSkillCharge;
        public AudioClip SfxSkillSlam;

        [Header("UI SFX")]
        public AudioClip SfxCoin;
        public AudioClip SfxPanel;
        public AudioClip SfxCombo;

        // 兼容旧字段（已废弃，只为防止编译错误）
        [System.Obsolete] public AudioClip ShotSound => SfxShoot;
        [System.Obsolete] public AudioClip PulseSound => SfxSkillCharge;
        [System.Obsolete] public AudioClip VictorySound => JingleVictory;
        [System.Obsolete] public AudioClip DefeatSound => JingleDefeat;
        [System.Obsolete] public AudioClip Music => MusicBattle;
    }
}
