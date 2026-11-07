namespace Procedure
{
    /// <summary>
    /// 11-开始游戏
    /// </summary>
    public sealed class StartGameProcedure : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        public override void OnEnter()
        {
            // Launcher 由业务入口在首页就绪后交接，异步加载期间保留过渡画面。
        }
    }
}
