using UnityEngine;

namespace GameLogic.Defense90
{
    // 首页只需要背景音乐；战斗表现资源统一由 DefenseAssets 承担，避免首页拉起战斗 BGM 与大图。
    [CreateAssetMenu(menuName = "Defense90/首页资源")]
    public sealed class DefenseHomeAssets : ScriptableObject
    {
        public AudioClip MusicHome;
    }
}
