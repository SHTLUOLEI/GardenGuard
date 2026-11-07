using DGame;

namespace Procedure
{
    public abstract class ProcedureBase : DGame.ProcedureBase
    {
        public abstract bool UseNativeDialog { get; }

        protected readonly IResourceModule m_resourceModule = ModuleSystem.GetModule<IResourceModule>();

        protected void SetRemoteServerURL(string resDownloadPath, string fallbackResDownloadPath)
            => m_resourceModule?.SetRemoteServerURL(resDownloadPath, fallbackResDownloadPath);
        
        protected void SetPlayMode(YooAsset.EPlayMode playMode) => m_resourceModule.PlayMode = playMode;
        
        protected void SetDefaultPackageName(string packageName) => m_resourceModule.DefaultPackageName = packageName;

        /// <summary>
        /// 记录启动流程耗时，便于在小游戏控制台定位首屏慢点。
        /// </summary>
        protected new void SwitchState<TState>() where TState : ProcedureBase
        {
            UnityEngine.Debug.Log($"[LaunchTiming] {GetType().Name} -> {typeof(TState).Name} t={UnityEngine.Time.realtimeSinceStartup * 1000f:F0}ms");
            base.SwitchState<TState>();
        }

    }
}