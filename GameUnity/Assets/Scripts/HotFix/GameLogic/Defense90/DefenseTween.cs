using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Defense90
{
    // 面板弹出/收起：遮罩淡入 + 面板从 0.85 缩放弹出（OutBack），关闭反向。窗口销毁前必须调用 Kill。
    public static class DefenseTween
    {
        public const float FADE_TIME = 0.2f, POP_TIME = 0.28f, OUT_TIME = 0.15f, FROM_SCALE = 0.85f;

        public static void PopIn(Image shade, RectTransform panel)
        {
            Kill(shade, panel);
            Color color = shade.color;
            float alpha = color.a;
            color.a = 0; shade.color = color;
            shade.DOFade(alpha, FADE_TIME).SetUpdate(true);
            panel.localScale = Vector3.one * FROM_SCALE;
            panel.DOScale(1, POP_TIME).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public static void PopOut(Image shade, RectTransform panel, Action done)
        {
            Kill(shade, panel);
            shade.DOFade(0, OUT_TIME).SetUpdate(true);
            panel.DOScale(FROM_SCALE, OUT_TIME).SetEase(Ease.InQuad).SetUpdate(true).OnComplete(() => done?.Invoke());
        }

        public static void Kill(params Component[] targets)
        {
            foreach (Component target in targets)
                if (target != null) { target.transform.DOKill(); if (target is Graphic graphic) graphic.DOKill(); }
        }

        // 一次性的弹跳提示（分数、连击、计时等）。
        public static void Punch(Transform target, float strength = 0.2f, float duration = 0.25f)
        {
            target.DOKill(true);
            target.DOPunchScale(Vector3.one * strength, duration, 6).SetUpdate(true);
        }
    }

}
