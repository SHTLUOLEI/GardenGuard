using DG.Tweening;
using UnityEngine;

namespace GameLogic.Defense90
{
    // 保留现有 Prefab 脚本入口，反馈实现与生命周期由通用按钮组件负责。
    public sealed class DefenseButtonPress : UIButtonScale
    {
        private void Awake() => ConfigurePress(Vector3.one * .92f, .06f, .18f, Ease.OutBack);
    }
}
