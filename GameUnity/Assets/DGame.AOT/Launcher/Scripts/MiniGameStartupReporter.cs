using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
#endif

namespace Launcher
{
    /// <summary>
    /// 抖音小游戏启动场景上报。放在 AOT 程序集，避免热更代码调用被裁剪的 SDK 方法。
    /// </summary>
    public static class MiniGameStartupReporter
    {
        /// <summary>
        /// 抖音预置场景：游戏可交互（TTI）。
        /// </summary>
        private const int SCENE_ID_INTERACTIVE = 7001;

        private static bool m_interactiveReported;

        /// <summary>
        /// 首页可操作时调用，每次启动只上报一次。
        /// </summary>
        public static void ReportInteractive()
        {
            if (m_interactiveReported)
            {
                return;
            }
            m_interactiveReported = true;
            Debug.Log($"[LaunchTiming] ReportScene {SCENE_ID_INTERACTIVE} t={Time.realtimeSinceStartup * 1000f:F0}ms");

#if UNITY_WEBGL && !UNITY_EDITOR
            if (!CanIUse.ReportScene)
            {
                Debug.LogWarning("[LaunchTiming] 当前基础库不支持 TT.ReportScene");
                return;
            }

            var param = new JsonData
            {
                ["sceneId"] = SCENE_ID_INTERACTIVE
            };
            TT.ReportScene(param,
                _ => Debug.Log("[LaunchTiming] ReportScene success"),
                (code, msg) => Debug.LogWarning($"[LaunchTiming] ReportScene failed: {code} {msg}"),
                null);
#endif
        }
    }
}
