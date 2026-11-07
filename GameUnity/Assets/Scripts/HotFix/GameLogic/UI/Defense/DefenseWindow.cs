using Cysharp.Threading.Tasks;
using DGame;
using GameLogic.Defense90;

namespace GameLogic
{
    public sealed class DefenseWindow : UIWindow
    {
        private DefenseGame m_view;
        private DefenseAssets m_assets;
        public DefenseGame View => m_view;
        public override bool FullScreen => true;
        public override bool CanEscClose => false;
        protected override ModelType GetModelType() => ModelType.NoneType;

        protected override void ScriptGenerator() => m_view = gameObject.GetComponent<DefenseGame>();
        protected override void BindMemberProperty()
        {
            SetUIFit(FindChild("m_rectSafe") as UnityEngine.RectTransform);
            m_view.SetLoading();
        }
        protected override void RegisterEvent()
        {
            m_view.BindEvents();
            m_view.StateChanged += OnRoundStateChanged;
        }
        protected override void OnCreate()
        {
            gameObject.AddComponent<DefenseUIAudio>();
            LoadPresentationAsync().Forget();
        }

        private async UniTask LoadPresentationAsync()
        {
            // One resource owner holds the presentation asset and its dependencies.
            var assets = await GameModule.ResourceModule.LoadAssetAsync<DefenseAssets>("DefenseAssets", LifetimeToken);
            // Cached loads may finish after cancellation; return that reference immediately.
            if (IsDestroyed)
            {
                if (assets != null) GameModule.ResourceModule.UnloadAsset(assets);
                return;
            }
            m_assets = assets;
            if (m_assets == null)
            {
                DLogger.Error("DefenseAssets 加载失败。");
                Close();
                return;
            }
            bool stress = UserData is bool stressMode && stressMode;
            var scene = await GameModule.SceneModule.LoadSceneAsync(stress ? "Defense90Stress" : "Defense90");
            DefenseBattlefield world = null;
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                {
                    world = root.GetComponent<DefenseBattlefield>();
                    if (world != null) break;
                }
            if (IsDestroyed)
            {
                if (world != null) UnityEngine.Object.Destroy(world.gameObject);
                return;
            }
            if (world == null)
            {
                DLogger.Error("Defense90 场景缺少 DefenseBattlefield。");
                Close();
                return;
            }
            GameModule.SceneModule.ActivateScene(stress ? "Defense90Stress" : "Defense90");
            m_view.Initialize(m_assets, world, stress);
        }

        private void OnRoundStateChanged(EDefenseState state)
        {
            if (state == EDefenseState.Paused)
            {
                // 新手引导期间回合同样是 Paused，但由引导遮罩接管，不弹普通暂停页。
                if (!m_view.TutorialActive) GameModule.UIModule.ShowWindowAsync<DefensePauseWindow>(this);
            }
            else if (state == EDefenseState.Won || state == EDefenseState.Lost)
                GameModule.UIModule.ShowWindowAsync<DefenseResultWindow>(this);
            else if (state == EDefenseState.Playing)
            {
                GameModule.UIModule.CloseWindow<DefensePauseWindow>();
                GameModule.UIModule.CloseWindow<DefenseResultWindow>();
            }
        }

        public void ReturnHome()
        {
            GameModule.UIModule.ShowWindowAsync<DefenseHomeWindow>(m_view.StressMode);
            Close();
        }

        protected override void OnUpdate() => m_view.UpdateFrame();
        protected override void OnHidden() => m_view.PauseRound();

        protected override void OnDestroy()
        {
            m_view.StateChanged -= OnRoundStateChanged;
            GameModule.UIModule.CloseWindow<DefensePauseWindow>();
            GameModule.UIModule.CloseWindow<DefenseResultWindow>();
            m_view.Shutdown();
            if (m_assets != null) GameModule.ResourceModule.UnloadAsset(m_assets);
            m_assets = null;
        }
    }
}
