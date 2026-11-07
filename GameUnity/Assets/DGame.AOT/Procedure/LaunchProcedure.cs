using DGame;
using Launcher;

namespace Procedure
{
    /// <summary>
    /// 1 - 游戏启动
    /// </summary>
    public sealed class LaunchProcedure : ProcedureBase
    {
        public override bool UseNativeDialog => true;
        private IAudioModule m_audioModule;

        public override void OnCreate(IFsm<IProcedureModule> fsm)
        {
            base.OnCreate(fsm);
            m_audioModule = ModuleSystem.GetModule<IAudioModule>();
        }

        public override void OnEnter()
        {
            // DLogger.Info("======== 1-进入游戏启动流程 ========");
            LauncherMgr.Initialize();
            InitAudioModuleSettings();
        }

        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            SwitchState<SplashProcedure>();
        }

        private void InitAudioModuleSettings()
        {
            m_audioModule.ApplySettingsFromPreferences();
            // DLogger.Info("======== 初始化音频模块完成 ========");
        }
    }
}
