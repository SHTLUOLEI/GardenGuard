using UnityEngine;

namespace GameLogic.Defense90
{
    public enum EDefenseHaptic { Short, Long }

    // 振动统一出口：受 DefenseSettings.VibrateOn 控制，并在 MIN_INTERVAL 内去重。
    public static class DefenseHaptics
    {
        public const float MIN_INTERVAL = 0.15f;
        private static float s_lastTime = float.NegativeInfinity;

        public static void Vibrate(EDefenseHaptic kind)
        {
            if (!DefenseSettings.VibrateOn) return;
            float now = Time.realtimeSinceStartup;
            if (now >= s_lastTime && now - s_lastTime < MIN_INTERVAL) return;
            s_lastTime = now;
            DGame.Utility.PlatformUtil.Vibrate(kind == EDefenseHaptic.Long);
        }
    }
}
