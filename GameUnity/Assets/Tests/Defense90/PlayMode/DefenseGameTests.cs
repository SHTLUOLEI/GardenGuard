#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameLogic;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Defense90Tests
{
    // 四个窗口（首页/战斗/暂停/结算）独立加载，按真实玩家操作路径回归。
    public sealed class DefenseGameTests
    {
        private sealed class CountingRandom : System.Random
        {
            public int PitchCalls;
            public override double NextDouble() { PitchCalls++; return .5; }
        }

        [UnityTest] public IEnumerator DenseImpactAudioKeepsOneVoicePerCategoryAndWindow()
        {
            yield return LoadBattle();
            var go = new GameObject("ImpactAudioTest");
            var assets = ScriptableObject.CreateInstance<DefenseAssets>();
            var clip = AudioClip.Create("ImpactTest", 4410, 1, 44100, false);
            var audio = go.AddComponent<DefenseAudio>();
            try
            {
                assets.SfxSplash = assets.SfxHeavyDown = clip;
                assets.SfxEnemyPop = new[] { clip };
                var random = new CountingRandom();
                typeof(DefenseAudio).GetField("m_random", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(audio, random);
                audio.Initialize(assets);
                for (int i = 0; i < 64; i++) { audio.PlaySplash(); audio.PlayEnemyPop(); audio.PlayHeavyDown(); }
                Assert.AreEqual(3, random.PitchCalls, "密集命中仅各播放一次水花、普通击杀、重甲击杀");
                audio.PlayShoot();
                audio.Initialize(assets); audio.PlaySplash();
                Assert.AreEqual(4, random.PitchCalls, "重新初始化后立即允许首个声音");
            }
            finally { audio.Shutdown(); Object.Destroy(go); Object.Destroy(clip); Object.Destroy(assets); }
        }

        [UnityTest] public IEnumerator FrameworkDropsUnreadyOrSaturatedEffectsAndReleasesBorrowedClips()
        {
            yield return LoadBattle();
            var module = GameModule.AudioModule;
            module.Stop(DGame.AudioType.Sound, false);
            var unloaded = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/BundleAssets/Audios/Defense/BGM/Home.ogg");
            Assert.IsTrue(unloaded.UnloadAudioData());
            var options = DGame.AudioPlayOptions.CreateDefault();
            Assert.IsNull(module.PlayClip(DGame.AudioType.Sound, unloaded, options), "未就绪的瞬时事件不创建等待请求");
            while (unloaded.loadState == AudioDataLoadState.Loading) yield return null;
            yield return null;
            foreach (var source in module.InstanceRoot.GetComponentsInChildren<AudioSource>())
                Assert.IsFalse(source.name.Contains("Sound") && source.clip == unloaded, "解码完成后不补播过期事件");
            var clip = AudioClip.Create("BorrowedEffect", 44100, 1, 44100, false);
            var agents = new List<DGame.AudioSourceAgent>();
            try
            {
                for (int i = 0; i < 256; i++)
                {
                    var agent = module.PlayClip(DGame.AudioType.Sound, clip, options);
                    if (agent == null) break;
                    agents.Add(agent);
                }
                Assert.Greater(agents.Count, 0); Assert.Less(agents.Count, 256);
                Assert.IsNull(module.PlayClip(DGame.AudioType.Sound, clip, options), "满载瞬时事件不排队");
                foreach (var agent in agents) agent.Stop();
                yield return null;
                foreach (var agent in agents) Assert.IsNull(agent.AudioSource.clip);
                var fresh = module.PlayClip(DGame.AudioType.Sound, clip, options);
                Assert.NotNull(fresh); Assert.IsTrue(fresh.AudioSource.isPlaying);
                fresh.Stop(); fresh.Update(.1f);
                Assert.IsNull(fresh.AudioSource.clip);
            }
            finally { module.Stop(DGame.AudioType.Sound, false); Object.Destroy(clip); }
        }

        [UnityTest] public IEnumerator ButtonFeedbackUsesFrameworkAndPreservesTimingAndDisabledState()
        {
            yield return LoadHome();
            var button = FindButton(GetWindow<DefenseHomeWindow>().gameObject, "m_btnStart");
            var feedback = button.GetComponent<DefenseButtonPress>();
            Assert.IsInstanceOf<UIButtonScale>(feedback);
            Assert.NotNull(button.GetComponent<UIButtonSound>());
            var pointer = new PointerEventData(EventSystem.current);
            Vector3 original = button.transform.localScale;
            button.interactable = false; feedback.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(.08f);
            Assert.Less((original - button.transform.localScale).magnitude, .001f);
            button.interactable = true; feedback.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(.08f);
            Assert.Less((original * .92f - button.transform.localScale).magnitude, .001f);
            feedback.OnPointerUp(pointer);
            yield return new WaitForSecondsRealtime(.22f);
            Assert.Less((original - button.transform.localScale).magnitude, .001f);
            feedback.OnPointerDown(pointer); button.gameObject.SetActive(false);
            Assert.Less((original - button.transform.localScale).magnitude, .001f);
            button.gameObject.SetActive(true);
        }

        [UnityTest] public IEnumerator StartupRetainsLauncherUntilHomeIsReadyAndDefersMusic()
        {
            yield return LoadHome();
            GameModule.UIModule.CloseWindow<DefenseHomeWindow>();
            yield return null;
            // 同一 Editor Play 会话只初始化一次框架；在现有 Launcher 上重走首页交接。
            var launcher = System.Type.GetType("Launcher.LauncherMgr, DGame.AOT", true);
            var loading = System.Type.GetType("Launcher.LoadUpdateUI, DGame.AOT", true);
            launcher.GetMethod("ShowUI").MakeGenericMethod(loading).Invoke(null, new object[] { null });
            var procedureType = System.Type.GetType("Procedure.StartGameProcedure, DGame.AOT", true);
            procedureType.GetMethod("OnEnter").Invoke(System.Activator.CreateInstance(procedureType), null);
            Assert.NotNull(GameObject.Find("LoadUpdateUI"), "StartGameProcedure 不能提前清空 Launcher");
            int ready = -1, hidden = -1, rendered = -1, music = -1;
            bool launcherAtReady = false, launcherAfterHidden = true;
            void Observe(string message, string stack, LogType type)
            {
                if (message.StartsWith("[Startup] stage=Home.Ready "))
                {
                    ready = Time.frameCount;
                    launcherAtReady = GameObject.Find("LoadUpdateUI") != null;
                }
                if (message.StartsWith("[Startup] stage=Home.LauncherHidden "))
                {
                    hidden = Time.frameCount;
                    launcherAfterHidden = GameObject.Find("LoadUpdateUI") != null;
                }
                if (message.StartsWith("[Startup] stage=Home.RenderOpportunity ")) rendered = Time.frameCount;
                if (message.StartsWith("[Startup] stage=Home.MusicLoadBegin ")) music = Time.frameCount;
            }
            Application.logMessageReceived += Observe;
            try
            {
                typeof(GameStart).GetMethod("StartGame", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, null);
                yield return WaitForWindow<DefenseHomeWindow>();
                float deadline = Time.realtimeSinceStartup + 10;
                while ((rendered < 0 || music < 0) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.GreaterOrEqual(ready, 0); Assert.IsTrue(launcherAtReady, "首页就绪前保留过渡界面");
                Assert.Greater(hidden, ready); Assert.IsFalse(launcherAfterHidden);
                Assert.Greater(rendered, hidden);
                Assert.GreaterOrEqual(music, hidden, "音乐请求必须让出首页创建/过渡交接帧");
                var home = GetWindow<DefenseHomeWindow>();
                Assert.IsTrue(home.IsVisible);
            }
            finally { Application.logMessageReceived -= Observe; }
        }

        [UnityTest] public IEnumerator ClosingHomeBeforeDeferredMusicDoesNotRevivePlayback()
        {
            foreach (int delay in new[] { 0, 2 })
            {
                yield return LoadHome();
                var home = GetWindow<DefenseHomeWindow>();
                var go = home.gameObject;
                for (int i = 0; i < delay; i++) yield return null;
                GameModule.UIModule.CloseWindow<DefenseHomeWindow>();
                for (int i = 0; i < 4; i++) yield return null;
                Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefenseHomeWindow>()); Assert.IsTrue(go == null);
                foreach (var source in GameModule.AudioModule.InstanceRoot.GetComponentsInChildren<AudioSource>())
                    Assert.IsFalse(source.clip != null && source.clip.name == "Home", "关闭窗口应清空加载/播放引用");
            }
            GameModule.UIModule.ShowWindowAsync<DefenseHomeWindow>();
            yield return WaitForWindow<DefenseHomeWindow>();
        }

        [UnityTest] public IEnumerator FrameSpikeKeepsPlayingAndLifecycleStillRequiresResume()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            Assert.IsFalse(game.TutorialActive);
            Material shadow = game.Battlefield.transform.Find("Shadow_0").GetComponent<SpriteRenderer>().sharedMaterial;
            Material drop = game.Battlefield.transform.Find("WaterShot_0").GetComponent<SpriteRenderer>().sharedMaterial;
            System.Threading.Thread.Sleep(650);
            yield return null;
            Assert.Greater(Time.unscaledDeltaTime, .5f, "输入必须覆盖实际的长帧");
            game.UpdateFrame();
            Assert.AreEqual(EDefenseState.Playing, game.Round.State, "长帧不弹出暂停");
            Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefensePauseWindow>());
            game.SendMessage("OnApplicationFocus", false);
            yield return WaitForWindow<DefensePauseWindow>();
            game.ResumeRound(); Assert.AreEqual(EDefenseState.Paused, game.Round.State, "失焦时不恢复");
            game.SendMessage("OnApplicationFocus", true);
            Assert.AreEqual(EDefenseState.Paused, game.Round.State, "恢复焦点后仍由玩家继续");
            GetWindow<DefensePauseWindow>().Continue();
            Assert.AreEqual(EDefenseState.Playing, game.Round.State);
            game.SendMessage("OnApplicationPause", true);
            yield return WaitForWindow<DefensePauseWindow>();
            game.SendMessage("OnApplicationPause", false);
            Assert.AreEqual(EDefenseState.Paused, game.Round.State);
            GetWindow<DefensePauseWindow>().Continue();
            FindBattle().ReturnHome();
            yield return WaitForWindow<DefenseHomeWindow>();
            yield return null;
            Assert.IsTrue(shadow == null); Assert.IsTrue(drop == null, "离开战斗释放运行时共享材质");
        }

        [UnityTest] public IEnumerator TutorialClickDemoDragKillsAndCountdownStartCleanRound()
        {
            bool hadTutorial = PlayerPrefs.HasKey(DefenseSettings.TUTORIAL_KEY);
            int savedTutorial = PlayerPrefs.GetInt(DefenseSettings.TUTORIAL_KEY);
            try
            {
                yield return LoadBattle();
                var game = FindBattle().View;
                DefenseSettings.TutorialDone = false; game.StartRound(); game.AdvanceFrame(0);
                var tutorial = game.GetComponentInChildren<DefenseTutorial>(true);
                Assert.IsTrue(tutorial.Active); Assert.AreEqual(DefenseTutorialPhase.Intro, tutorial.Phase);
                Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefensePauseWindow>());
                var next = tutorial.GetComponentInChildren<Button>();
                Assert.IsTrue(next.targetGraphic.raycastTarget, "说明按钮必须接收点击");
                game.PauseRound(); game.AdvanceFrame(1);
                Assert.AreEqual(DefenseTutorialPhase.Intro, tutorial.Phase);
                game.ResumeRound(); Assert.IsFalse(tutorial.Suspended);
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, next.transform.position),
                    pointerPressRaycast = new RaycastResult { module = game.GetComponent<GraphicRaycaster>() }
                };
                Canvas.ForceUpdateCanvases();
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                yield return Capture("tutorial-intro");
                hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
                Assert.IsTrue(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == next.gameObject,
                    $"真实射线必须首先命中继续按钮：position={pointer.position}, depth={next.targetGraphic.depth}, cull={next.targetGraphic.canvasRenderer.cull}, hits={string.Join(",", hits.ConvertAll(hit => hit.gameObject.name))}");
                ExecuteEvents.Execute(next.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(DefenseTutorialPhase.AutoFire, tutorial.Phase);
                Assert.AreEqual(3, game.Round.ActiveCount); Assert.IsTrue(game.Round.Practice);
                Vector2 stationary = game.Round.Enemies[0].Position;
                game.AdvanceFrame(.05f);
                Assert.AreEqual(stationary, game.Round.Enemies[0].Position);
                Assert.AreEqual(0, game.Round.Elapsed); Assert.AreEqual(0, game.Round.Score);
                yield return Capture("tutorial-auto");
                var bubble = tutorial.GetComponentInChildren<Text>().transform.parent as RectTransform;
                var corners = new Vector3[4]; bubble.GetWorldCorners(corners);
                Vector2 bubbleMin = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, corners[0]);
                Vector2 bubbleMax = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, corners[2]);
                var bubbleRect = Rect.MinMaxRect(bubbleMin.x, bubbleMin.y, bubbleMax.x, bubbleMax.y);
                foreach (var renderer in game.Battlefield.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!renderer.enabled || (!renderer.name.StartsWith("Enemy_") && renderer != game.Battlefield.Turret)) continue;
                    Vector2 min = game.Battlefield.WorldCamera.WorldToScreenPoint(renderer.bounds.min);
                    Vector2 max = game.Battlefield.WorldCamera.WorldToScreenPoint(renderer.bounds.max);
                    Assert.IsFalse(bubbleRect.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y)),
                        "自动演示提示不能挡住怪物或炮台：" + renderer.name);
                }
                game.PauseRound(); game.AdvanceFrame(2);
                Assert.AreEqual(DefenseTutorialPhase.AutoFire, tutorial.Phase);
                Assert.AreEqual(EDefenseState.Paused, game.Round.State);
                game.ResumeRound();
                for (int i = 0; i < 300 && tutorial.Phase == DefenseTutorialPhase.AutoFire; i++) game.AdvanceFrame(.1f);
                Assert.AreEqual(DefenseTutorialPhase.Skill, tutorial.Phase);
                Assert.AreEqual(5, game.Round.ActiveCount);
                Assert.IsFalse(next.gameObject.activeInHierarchy, "技能教学必须让出全屏点击拦截");
                game.AdvanceFrame(3);
                Assert.AreEqual(5, game.Round.ActiveCount, "技能练习不能被自动炮台抢先清掉");
                // 误放后无需等待正式冷却，可重新拖放。
                game.ToggleAim(); game.SelectTarget(Vector2.zero); game.AdvanceFrame(2);
                Assert.AreEqual(5, game.Round.ActiveCount); Assert.AreEqual(0, game.Round.Cooldown);
                var skillButton = FindButton(game.gameObject, "m_btnSkill");
                var drag = skillButton.GetComponent<DefenseSkillDrag>();
                pointer.position = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, skillButton.transform.position);
                drag.OnPointerDown(pointer);
                Vector2 target = game.Round.Enemies[0].Position;
                pointer.position = game.Battlefield.WorldCamera.WorldToScreenPoint(game.Battlefield.WorldPoint(target));
                drag.OnDrag(pointer); game.AdvanceFrame(0);
                Assert.IsTrue(game.Battlefield.Skill.Area.enabled);
                yield return Capture("tutorial-skill");
                drag.OnEndDrag(pointer);
                for (int i = 0; i < 40 && tutorial.Phase == DefenseTutorialPhase.Skill; i++) game.AdvanceFrame(.1f);
                Assert.AreEqual(DefenseTutorialPhase.Countdown, tutorial.Phase);
                Assert.AreEqual(0, game.Round.ActiveCount); Assert.AreEqual(3, tutorial.Countdown);
                Assert.IsFalse(DefenseSettings.TutorialDone);
                yield return Capture("tutorial-countdown");
                game.PauseRound(); game.AdvanceFrame(4);
                Assert.AreEqual(3, tutorial.Countdown, "倒计时阶段暂停时不得偷跑");
                game.ResumeRound(); Assert.AreEqual(EDefenseState.Paused, game.Round.State);
                game.AdvanceFrame(1.05f); Assert.AreEqual(2, tutorial.Countdown);
                game.AdvanceFrame(1); Assert.AreEqual(1, tutorial.Countdown);
                game.AdvanceFrame(1);
                Assert.IsFalse(tutorial.Active); Assert.IsTrue(DefenseSettings.TutorialDone);
                Assert.IsFalse(game.Round.Practice); Assert.AreEqual(EDefenseState.Playing, game.Round.State);
                Assert.AreEqual(0, game.Round.Elapsed); Assert.AreEqual(0, game.Round.Score);
                Assert.AreEqual(0, game.Round.ActiveCount); Assert.AreEqual(0, game.Round.Cooldown);
                FindBattle().ReturnHome(); yield return WaitForWindow<DefenseHomeWindow>();
            }
            finally
            {
                if (hadTutorial) PlayerPrefs.SetInt(DefenseSettings.TUTORIAL_KEY, savedTutorial);
                else PlayerPrefs.DeleteKey(DefenseSettings.TUTORIAL_KEY);
                PlayerPrefs.Save();
            }
        }

        [UnityTest] public IEnumerator OverlappingEnemiesKeepUniqueGroundOrderAcrossAnimationAndShake()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            var world = game.Battlefield;
            game.AdvanceFrame(1);
            for (int i = 0; i < game.Round.Enemies.Length; i++)
            {
                game.Round.Enemies[i].Active = true;
                game.Round.Enemies[i].Position = new Vector2(320, 420);
                game.Round.Enemies[i].Health = game.Round.Enemies[i].MaxHealth = 1000;
            }
            game.AdvanceFrame(0);
            var orders = new Dictionary<int, int>();
            for (int frame = 0; frame < 30; frame++)
            {
                world.Shake(4, frame * .016f);
                world.Refresh(.016f, Vector2.zero);
                var unique = new HashSet<int>();
                for (int i = 0; i < game.Round.Enemies.Length; i++)
                {
                    var renderer = world.transform.Find("Enemy_" + i).GetComponent<SpriteRenderer>();
                    Assert.IsTrue(unique.Add(renderer.sortingOrder), "重叠怪物必须有唯一排序，避免透明渲染切换前后");
                    if (frame == 0) orders[i] = renderer.sortingOrder;
                    else Assert.AreEqual(orders[i], renderer.sortingOrder, "动画和震动不能改变同脚底位置的排序");
                }
            }
            game.Round.Enemies[0].Position.y = 421;
            world.Refresh(0, Vector2.zero);
            Assert.Less(world.transform.Find("Enemy_0").GetComponent<SpriteRenderer>().sortingOrder,
                world.transform.Find("Enemy_1").GetComponent<SpriteRenderer>().sortingOrder, "脚底更高的怪物应在后面");
            FindBattle().ReturnHome();
            yield return WaitForWindow<DefenseHomeWindow>();
        }

        [UnityTest] public IEnumerator HomeMusicPlaysFollowsSettingsAndYieldsToBattle()
        {
            string onKey = DGame.Constant.Settings.MUSIC_MUTED, volumeKey = DGame.Constant.Settings.MUSIC_VOLUME;
            bool hadOn = PlayerPrefs.HasKey(onKey), hadVolume = PlayerPrefs.HasKey(volumeKey);
            int savedOn = PlayerPrefs.GetInt(onKey);
            float savedVolume = PlayerPrefs.GetFloat(volumeKey);
            try
            {
                DefenseSettings.MusicOn = true; DefenseSettings.MusicVolume = .7f;
                yield return LoadHome();
                var home = GetWindow<DefenseHomeWindow>();
                Assert.IsTrue(GameModule.ResourceModule.CheckLocationValid("Home"), "单文件音乐地址必须可解析");
                Assert.IsEmpty(home.gameObject.GetComponents<AudioSource>(), "首页不创建业务 AudioSource");
                Assert.IsNull(home.gameObject.GetComponent<DefenseAudio>(), "首页由框架音频模块播放");
                AudioSource music = null;
                float deadline = Time.realtimeSinceStartup + 5;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var source in GameModule.AudioModule.InstanceRoot.GetComponentsInChildren<AudioSource>())
                        if (source.clip != null && source.clip.name == "Home") music = source;
                    if (music != null && music.isPlaying) break;
                    yield return null;
                }
                Assert.NotNull(music, "首页应加载并播放 MusicHome");
                Assert.IsTrue(music.isPlaying); Assert.IsTrue(music.loop);
                Assert.AreEqual(.7f, GameModule.AudioModule.MusicVolume, .001f);
                Assert.AreSame(GameModule.AudioModule.AudioMixer, music.outputAudioMixerGroup.audioMixer);
                FindButton(home.gameObject, "m_btnSettings").onClick.Invoke();
                yield return WaitForWindow<DefenseSettingsWindow>();
                var popup = GetWindow<DefenseSettingsWindow>().gameObject.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
                popup.Find("m_sliderMusic").GetComponent<Slider>().value = .35f;
                Assert.AreEqual(.35f, GameModule.AudioModule.MusicVolume, .001f, "音量由框架混音器立即更新");
                music.Pause(); music.timeSamples = 4096;
                DefenseSettings.MusicVolume = .3f; DefenseSettings.MusicVolume = .3f;
                Assert.AreEqual(4096, music.timeSamples, "重复设置音量不能重启暂停或播放中的同一音乐");
                Assert.IsFalse(music.isPlaying); music.UnPause();
                popup.Find("m_toggleMusic").GetComponent<Toggle>().isOn = false;
                Assert.IsFalse(music.isPlaying);
                popup.Find("m_toggleMusic").GetComponent<Toggle>().isOn = true;
                deadline = Time.realtimeSinceStartup + 5;
                while (!music.isPlaying && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(music.isPlaying);
                GameModule.UIModule.CloseWindow<DefenseSettingsWindow>();
                FindButton(home.gameObject, "m_btnStart").onClick.Invoke();
                yield return WaitForBattle(); yield return null;
                foreach (var source in Object.FindObjectsOfType<AudioSource>())
                    Assert.IsFalse(source.isPlaying && source.clip != null && source.clip.name == "Home", "进战斗后不能叠播首页音乐");
                FindBattle().ReturnHome();
                yield return WaitForWindow<DefenseHomeWindow>();
                home = GetWindow<DefenseHomeWindow>();
                deadline = Time.realtimeSinceStartup + 5;
                bool playing = false;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var source in GameModule.AudioModule.InstanceRoot.GetComponentsInChildren<AudioSource>())
                        playing |= source.isPlaying && source.clip != null && source.clip.name == "Home";
                    if (playing) break;
                    yield return null;
                }
                Assert.IsTrue(playing, "返回首页应恢复首页音乐");
            }
            finally
            {
                DefenseSettings.MusicOn = savedOn == 0 || !hadOn;
                DefenseSettings.MusicVolume = hadVolume ? savedVolume : .8f;
                if (!hadOn) PlayerPrefs.DeleteKey(onKey);
                if (!hadVolume) PlayerPrefs.DeleteKey(volumeKey);
                PlayerPrefs.Save(); DefenseSettings.Reload();
            }
        }

        [UnityTest] public IEnumerator UiAudioFollowsSfxSliderAndBattleRestart()
        {
            bool musicOn = DefenseSettings.MusicOn, sfxOn = DefenseSettings.SfxOn;
            float sfxVolume = DefenseSettings.SfxVolume;
            float musicVolume = DefenseSettings.MusicVolume;
            try
            {
                DefenseSettings.MusicOn = false; DefenseSettings.SfxOn = true; DefenseSettings.SfxVolume = 1;
                yield return LoadHome();
                var home = GetWindow<DefenseHomeWindow>();
                Assert.NotNull(home.gameObject.GetComponent<DefenseUIAudio>());
                FindButton(home.gameObject, "m_btnSettings").onClick.Invoke();
                yield return WaitForWindow<DefenseSettingsWindow>();
                var popup = GetWindow<DefenseSettingsWindow>().gameObject.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
                yield return new WaitForSecondsRealtime(.08f);
                popup.Find("m_sliderSfx").GetComponent<Slider>().value = .25f;
                Assert.AreEqual(.25f, GameModule.AudioModule.UISoundVolume, .001f);
                bool heard = false;
                float deadline = Time.realtimeSinceStartup + 2;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var source in GameModule.AudioModule.InstanceRoot.GetComponentsInChildren<AudioSource>())
                        heard |= source.name.Contains("UISound") && source.isPlaying && source.clip != null;
                    if (heard) break;
                    yield return null;
                }
                Assert.IsTrue(heard, "界面交互应在框架 UISound 声道实际播放");
                popup.Find("m_toggleSfx").GetComponent<Toggle>().isOn = false;
                Assert.IsFalse(GameModule.AudioModule.UISoundEnable);
                popup.Find("m_toggleSfx").GetComponent<Toggle>().isOn = true;
                popup.Find("m_sliderSfx").GetComponent<Slider>().value = 0;
                Assert.IsFalse(GameModule.AudioModule.UISoundEnable, "滑到零时应完全静音");
                GameModule.UIModule.CloseWindow<DefenseSettingsWindow>();
                FindButton(home.gameObject, "m_btnStart").onClick.Invoke();
                yield return WaitForBattle();
                var game = FindBattle().View;
                Assert.NotNull(game.GetComponent<DefenseUIAudio>());
                foreach (var source in game.GetComponents<AudioSource>())
                    Assert.IsFalse(source.isPlaying, "序列化旧音源不再参与播放");
                Assert.IsFalse(GameModule.AudioModule.MusicEnable, "开局必须尊重关闭音乐的设置");
                Assert.IsFalse(GameModule.AudioModule.SoundEnable);
                DefenseSettings.MusicOn = true;
                AudioSource music = null;
                float musicDeadline = Time.realtimeSinceStartup + 5;
                while (music == null && Time.realtimeSinceStartup < musicDeadline)
                {
                    foreach (var source in GameModule.AudioModule.InstanceRoot.GetComponentsInChildren<AudioSource>())
                        if (source.isPlaying && source.clip != null && source.clip.name == "Battle") music = source;
                    yield return null;
                }
                Assert.NotNull(music, "战斗 BGM 必须通过框架分组播放");
                Assert.IsTrue(music.isPlaying);
                music.Pause(); music.timeSamples = 4096;
                DefenseSettings.MusicVolume = .45f;
                Assert.AreEqual(4096, music.timeSamples, "战斗调音量不重新播放同一 BGM");
                music.UnPause();
                game.GetComponent<DefenseAudio>().StopBGM();
                game.StartRound();
                Assert.IsTrue(music.isPlaying, "重新开始应恢复战斗 BGM");
                DefenseSettings.SfxVolume = .6f;
                Assert.AreEqual(.6f, GameModule.AudioModule.SoundVolume, .001f, "战斗音效音量应实时更新");
                Assert.AreEqual(.6f, GameModule.AudioModule.UISoundVolume, .001f);
                FindBattle().ReturnHome();
                yield return WaitForWindow<DefenseHomeWindow>();
            }
            finally
            {
                DefenseSettings.MusicOn = musicOn; DefenseSettings.MusicVolume = musicVolume;
                DefenseSettings.SfxOn = sfxOn; DefenseSettings.SfxVolume = sfxVolume;
            }
        }

        [UnityTest] public IEnumerator SkillPressWaitsForBattlefieldBeforeShowingRange()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            var world = game.Battlefield;
            var button = FindButton(FindBattle().gameObject, "m_btnSkill");
            var drag = button.GetComponent<DefenseSkillDrag>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, button.transform.position),
                pointerPressRaycast = new RaycastResult { module = button.GetComponentInParent<GraphicRaycaster>() }
            };
            drag.OnPointerDown(pointer); game.AdvanceFrame(0);
            Assert.IsTrue(game.Aiming);
            Assert.IsFalse(world.Skill.Area.enabled, "按下按钮时不应在默认中心显示范围圈");
            drag.OnBeginDrag(pointer); game.AdvanceFrame(0);
            Assert.IsFalse(world.Skill.Area.enabled, "拖拽仍在按钮内时不应显示范围圈");
            game.CancelAim(); game.AdvanceFrame(0);
            Assert.IsFalse(world.Skill.Area.enabled, "尚未显示的圈取消时也不应闪现");
            drag.OnPointerDown(pointer);
            Vector2 target = new Vector2(320, 360);
            pointer.position = world.WorldCamera.WorldToScreenPoint(world.WorldPoint(target));
            drag.OnDrag(pointer); game.AdvanceFrame(0);
            Assert.IsTrue(world.Skill.Area.enabled);
            Assert.IsTrue(game.AimValid);
            Assert.Less((world.Skill.transform.position - world.WorldPoint(target)).magnitude, .001f);
            drag.OnEndDrag(pointer); game.AdvanceFrame(0);
            Assert.IsFalse(game.Aiming);
            Assert.AreEqual(0, world.Skill.Phase);
            Assert.Less((game.Round.SkillPosition - target).magnitude, .01f);
            FindBattle().ReturnHome();
            yield return WaitForWindow<DefenseHomeWindow>();
        }

        [UnityTest] public IEnumerator WorldSpritesSkillPauseRestartAndCleanup()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            var world = game.Battlefield;
            Assert.IsNull(world.transform.parent);
            Assert.IsNull(world.GetComponentInParent<Canvas>());
            Assert.IsTrue(world.WorldCamera.orthographic);
            Assert.AreEqual("Defense90", world.gameObject.scene.name);
            Assert.AreEqual("Defense90", SceneManager.GetActiveScene().name);
            Assert.IsFalse(SceneManager.GetSceneByName("GameStart").isLoaded);
            Assert.AreEqual(1, Object.FindObjectsOfType<DefenseBattlefield>().Length);
            Vector3 authoredBackground = world.Background.transform.localPosition, authoredBackgroundScale = world.Background.transform.localScale;
            Vector3 authoredTurretScale = world.Turret.transform.localScale;
            float authoredCameraSize = world.WorldCamera.orthographicSize;
            game.AdvanceFrame(1);
            Assert.AreEqual(authoredBackground, world.Background.transform.localPosition);
            Assert.AreEqual(authoredBackgroundScale, world.Background.transform.localScale);
            Assert.AreEqual(authoredTurretScale, world.Turret.transform.localScale);
            Assert.AreEqual(authoredCameraSize, world.WorldCamera.orthographicSize);
            // 将已由规则生成的敌人摆到可见区，隔离验证表现与伤害时点。
            int first = -1;
            for (int i = 0; i < game.Round.Enemies.Length; i++)
            {
                if (!game.Round.Enemies[i].Active) continue;
                game.Round.Enemies[i].Position = new Vector2(270 + i * 30, 360);
                game.Round.Enemies[i].Health = game.Round.Enemies[i].MaxHealth = 1000;
                if (first < 0) first = i;
            }
            Assert.GreaterOrEqual(first, 0);
            game.AdvanceFrame(0);
            Assert.AreEqual(game.Round.ActiveCount, world.ActiveEnemyViews);
            var enemy = world.transform.Find("Enemy_" + first);
            Assert.NotNull(enemy.GetComponent<SpriteRenderer>());
            Assert.IsNull(enemy.GetComponent<RectTransform>());
            yield return Capture("world-battle");
            game.ToggleAim(); game.SelectTarget(new Vector2(320, 360));
            game.AdvanceFrame(.05f);
            Assert.AreEqual(0, world.Skill.Phase);
            yield return Capture("world-range");
            Assert.AreEqual(EDefenseState.Playing, game.Round.State, "range capture state; tutorial=" + game.TutorialActive);
            game.AdvanceFrame(Mathf.Max(0, game.Round.Rules.TelegraphDuration - game.Round.SkillAge) + .01f);
            Assert.AreEqual(1, world.Skill.Phase, "age=" + game.Round.SkillAge + " telegraph=" + game.Round.Rules.TelegraphDuration + " state=" + game.Round.State);
            Assert.IsTrue(world.Skill.Column.enabled);
            Assert.Greater(game.Round.SkillHits, 0);
            Assert.IsTrue(world.HitStopped);
            float clock = game.Round.Elapsed;
            game.AdvanceFrame(.001f);
            Assert.Greater(game.Round.Elapsed, clock, "表现顿帧不得改变规则时钟");
            yield return Capture("world-column");
            game.AdvanceFrame(Mathf.Max(0, game.Round.Rules.TelegraphDuration + game.Round.Rules.ReleaseDuration - game.Round.SkillAge) + .01f);
            Assert.AreEqual(2, world.Skill.Phase);
            yield return Capture("world-impact");
            game.PauseRound();
            float age = game.Round.SkillAge; clock = game.Round.Elapsed;
            Vector3 position = enemy.position;
            game.AdvanceFrame(.2f);
            Assert.AreEqual(clock, game.Round.Elapsed); Assert.AreEqual(age, game.Round.SkillAge);
            Assert.AreEqual(position, enemy.position);
            game.StartRound(); game.AdvanceFrame(0);
            Assert.AreEqual(1, Object.FindObjectsOfType<DefenseBattlefield>().Length);
            Assert.AreEqual(0, world.ActiveEnemyViews); Assert.AreEqual(-1, world.Skill.Phase);
            FindBattle().ReturnHome();
            yield return WaitForWindow<DefenseHomeWindow>();
            yield return null;
            Assert.AreEqual(0, Object.FindObjectsOfType<DefenseBattlefield>().Length);
        }

        [UnityTest] public IEnumerator SettingsNotificationsKeepAudioIsolatedAndWindowsInSync()
        {
            yield return LoadHome();
            float volume = DefenseSettings.MusicVolume;
            bool shake = DefenseSettings.ShakeOn, vibrate = DefenseSettings.VibrateOn;
            int tier = DefenseSettings.VfxTier;
            var audio = GameModule.AudioModule;
            int musicEvents = 0;
            float notifiedVolume = -1;
            void OnChanged(EDefenseSetting setting)
            {
                if (setting != EDefenseSetting.Music) return;
                musicEvents++;
                notifiedVolume = audio.MusicVolume;
            }
            GameEventHelper.AddEvent.DefenseSettings.OnSettingsChanged(OnChanged);
            try
            {
                FindButton(GetWindow<DefenseHomeWindow>().gameObject, "m_btnSettings").onClick.Invoke();
                yield return WaitForWindow<DefenseSettingsWindow>();
                var settings = GetWindow<DefenseSettingsWindow>();
                var popup = settings.gameObject.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
                float target = volume == .47f ? .61f : .47f;
                DefenseSettings.MusicVolume = target;
                Assert.AreEqual(target, notifiedVolume, .001f, "同步音频必须先于设置事件");
                Assert.AreEqual(target, popup.Find("m_sliderMusic").GetComponent<Slider>().value, .001f,
                    "外部设置变化应刷新已打开窗口");
                DefenseSettings.MusicVolume = target;
                Assert.AreEqual(1, musicEvents, "相同值与无通知刷新不能再次广播");

                audio.MusicVolume = .213f; audio.SoundVolume = .231f; audio.UISoundVolume = .241f;
                DefenseSettings.ShakeOn = !shake;
                DefenseSettings.VibrateOn = !vibrate;
                DefenseSettings.VfxTier = tier == DefenseConfigMgr.TIER_FULL
                    ? DefenseConfigMgr.TIER_SIMPLE : DefenseConfigMgr.TIER_FULL;
                Assert.AreEqual(.213f, audio.MusicVolume, .001f, "非音频设置不得重写音乐音量");
                Assert.AreEqual(.231f, audio.SoundVolume, .001f);
                Assert.AreEqual(.241f, audio.UISoundVolume, .001f);
                Assert.AreEqual(!shake, popup.Find("m_toggleShake").GetComponent<Toggle>().isOn);

                GameModule.UIModule.CloseWindow<DefenseSettingsWindow>();
                DefenseSettings.MusicVolume = target == .47f ? .61f : .47f;
                Assert.AreEqual(2, musicEvents, "关闭窗口后广播仍可正常完成");
                GameModule.UIModule.ShowWindowAsync<DefenseSettingsWindow>();
                yield return WaitForWindow<DefenseSettingsWindow>();
                popup = GetWindow<DefenseSettingsWindow>().gameObject.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
                Assert.AreEqual(DefenseSettings.MusicVolume, popup.Find("m_sliderMusic").GetComponent<Slider>().value, .001f);
            }
            finally
            {
                GameEventHelper.RemoveEvent.DefenseSettings.OnSettingsChanged(OnChanged);
                GameModule.UIModule.CloseWindow<DefenseSettingsWindow>();
                DefenseSettings.MusicVolume = volume;
                DefenseSettings.ShakeOn = shake; DefenseSettings.VibrateOn = vibrate;
                DefenseSettings.VfxTier = tier;
                audio.ApplySettingsFromPreferences();
            }
        }

        [UnityTest] public IEnumerator SettingsControlsPersistAndCloseFromHome()
        {
            string[] names = { "VfxTier", "ShakeOn", "VibrateOn" };
            var existed = new HashSet<string>();
            var values = new Dictionary<string, float>();
            var keys = new List<string>();
            foreach (string name in names) keys.Add(DefenseSettings.KEY_PREFIX + name);
            keys.AddRange(new[] { DGame.Constant.Settings.MUSIC_MUTED, DGame.Constant.Settings.MUSIC_VOLUME,
                DGame.Constant.Settings.SOUND_MUTED, DGame.Constant.Settings.SOUND_VOLUME });
            keys.Add(DefenseSettings.TUTORIAL_KEY);
            foreach (string key in keys)
            {
                if (PlayerPrefs.HasKey(key)) existed.Add(key);
                values[key] = key.EndsWith("Volume") ? PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            }
            try
            {
                // 等待正式启动流程完成资源包初始化并打开首页，避免抢先加载资源。
                yield return LoadHome();
                FindButton(GetWindow<DefenseHomeWindow>().gameObject, "m_btnSettings").onClick.Invoke();
                yield return WaitForWindow<DefenseSettingsWindow>();
                yield return new WaitForSecondsRealtime(0.4f);
                var settings = GetWindow<DefenseSettingsWindow>();
                var popup = settings.gameObject.transform.Find("m_rectSafe/m_panel/m_rectStage/m_rectPopup");
                var music = popup.Find("m_toggleMusic").GetComponent<Toggle>();
                var sfx = popup.Find("m_toggleSfx").GetComponent<Toggle>();
                var musicSlider = popup.Find("m_sliderMusic").GetComponent<Slider>();
                var sfxSlider = popup.Find("m_sliderSfx").GetComponent<Slider>();
                music.isOn = false; sfx.isOn = false;
                Assert.IsFalse(musicSlider.interactable); Assert.IsFalse(sfxSlider.interactable);
                music.isOn = true; sfx.isOn = true;
                Assert.IsTrue(musicSlider.interactable); Assert.IsTrue(sfxSlider.interactable);
                musicSlider.value = 0.35f; sfxSlider.value = 0.55f;
                Assert.AreEqual(0.35f, DefenseSettings.MusicVolume, 0.001f);
                Assert.AreEqual(0.55f, DefenseSettings.SfxVolume, 0.001f);
                var full = popup.Find("m_groupVfx/m_toggleVfxFull").GetComponent<Toggle>();
                var simple = popup.Find("m_groupVfx/m_toggleVfxSimple").GetComponent<Toggle>();
                simple.isOn = true;
                Assert.IsFalse(full.isOn);
                Assert.AreEqual(DefenseConfigMgr.TIER_SIMPLE, DefenseSettings.VfxTier);
                full.isOn = true;
                Assert.IsFalse(simple.isOn);
                Assert.AreEqual(DefenseConfigMgr.TIER_FULL, DefenseSettings.VfxTier);
                popup.Find("m_toggleShake").GetComponent<Toggle>().isOn = false;
                popup.Find("m_toggleVibrate").GetComponent<Toggle>().isOn = false;
                Assert.IsFalse(DefenseSettings.ShakeOn); Assert.IsFalse(DefenseSettings.VibrateOn);
                DefenseSettings.Reload();
                Assert.AreEqual(0.35f, DefenseSettings.MusicVolume, 0.001f);

                foreach (var control in popup.GetComponentsInChildren<Selectable>())
                {
                    var pointer = new PointerEventData(EventSystem.current)
                    { position = RectTransformUtility.WorldToScreenPoint(GameModule.UIModule.UICamera, control.transform.position) };
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointer, hits);
                    Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(control.transform), control.name + " raycast blocked");
                }
                yield return Capture("settings-repaired");
                yield return CheckLayout(new Vector2Int(720, 1080), new Rect(0, 0, 720, 1080), "settings-short");
                yield return CheckLayout(new Vector2Int(1080, 2400), new Rect(0, 0, 1080, 2400), "settings-tall");
                FindButton(settings.gameObject, "m_btnTutorial").onClick.Invoke();
                Assert.IsFalse(DefenseSettings.TutorialDone);
                FindButton(settings.gameObject, "m_btnClose").onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefenseSettingsWindow>());
                Assert.IsTrue(GameModule.UIModule.ContainsWindow<DefenseHomeWindow>());
            }
            finally
            {
                GameModule.UIModule.CloseWindow<DefenseSettingsWindow>();
                foreach (string key in keys)
                {
                    if (!existed.Contains(key)) PlayerPrefs.DeleteKey(key);
                    else if (key.EndsWith("Volume")) PlayerPrefs.SetFloat(key, values[key]);
                    else PlayerPrefs.SetInt(key, (int)values[key]);
                }
                PlayerPrefs.Save(); DefenseSettings.Reload();
            }
        }

        [UnityTest] public IEnumerator HomeStartsBattleAndPauseKeepsClock()
        {
            yield return LoadHome();
            var home = GetWindow<DefenseHomeWindow>();

            yield return Capture("home");
            FindButton(home.gameObject, "m_btnStart").onClick.Invoke();
            yield return WaitForBattle();
            var game = GetWindow<DefenseWindow>().View;
            Assert.AreEqual(EDefenseState.Playing, game.Round.State);

            for (int i = 0; i < 160; i++) game.AdvanceFrame(0.1f);
            yield return Capture("battle");

            game.PauseRound();
            yield return WaitForWindow<DefensePauseWindow>();
            float clock = game.Round.Elapsed;
            game.Round.Tick(20);
            Assert.AreEqual(clock, game.Round.Elapsed, "暂停必须冻结计时");
            yield return Capture("pause");

            GetWindow<DefensePauseWindow>().Continue();
            yield return null;
            Assert.AreEqual(EDefenseState.Playing, game.Round.State);
            Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefensePauseWindow>());
        }

        [UnityTest] public IEnumerator SkillTelegraphReleaseAndHitApplyDamage()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            var arena = game.Battlefield;
            Assert.AreSame(game.UnitAtlas, arena.UnitAtlas);
            Assert.IsNull(arena.GetComponentInParent<Canvas>());

            for (int i = 0; i < 40; i++) game.AdvanceFrame(0.1f);
            FindButton(game.gameObject, "m_btnSkill").onClick.Invoke();
            Assert.IsTrue(game.Aiming);
            Vector2 cluster = BestCluster(game.Round);
            game.AimPoint = cluster; game.SelectTarget(cluster);
            Assert.IsFalse(game.Aiming);

            game.AdvanceFrame(0.2f); yield return Capture("telegraph");
            game.AdvanceFrame(0.30f); yield return Capture("release");
            game.AdvanceFrame(0.4f); yield return Capture("hit");
            Assert.Greater(game.Round.SkillHits, 0);
        }

        [UnityTest] public IEnumerator ArenaReceivesTouchesWithoutDecorationBlocking()
        {
            yield return LoadBattle();
            var game = FindBattle().View;
            var arena = game.Battlefield;
            var pointer = new PointerEventData(EventSystem.current)
                { position = arena.WorldCamera.WorldToScreenPoint(arena.WorldPoint(new Vector2(320, 340))) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.AreEqual(0, hits.Count, "装饰美术不能挡住世界战场拾取");
            Assert.IsTrue(arena.TryGetArenaPoint(pointer.position, out Vector2 point));
            Assert.Less((point - new Vector2(320, 340)).magnitude, .01f);
            game.ToggleAim(); arena.Pointer(pointer.position, true);
            Assert.IsFalse(game.Aiming); Assert.AreEqual(1, game.Round.SkillCasts);
        }

        [UnityTest] public IEnumerator PortraitLayoutsStayInsideSafeAreaAcrossRatios()
        {
            yield return LoadHome();
            foreach (Vector2Int size in new[] { new Vector2Int(720, 1080), new Vector2Int(720, 1280),
                new Vector2Int(1080, 2400), new Vector2Int(1170, 2532) })
            {
                var safe = new Rect(0, 0, size.x, size.y);
                yield return CheckLayout(size, safe, $"home-{size.x}x{size.y}");
            }
            // 刘海屏：顶部 90px 与底部 60px 被系统占用。
            yield return CheckLayout(new Vector2Int(1080, 2400), new Rect(0, 60, 1080, 2250), "home-safe-area");
        }

        [UnityTest] public IEnumerator RoundWinsWithSkillAndSavesBestScore()
        {
            bool hadBest = PlayerPrefs.HasKey(DefenseGame.BEST_SCORE_KEY);
            int previous = PlayerPrefs.GetInt(DefenseGame.BEST_SCORE_KEY);
            yield return LoadBattle();
            var game = FindBattle().View;
            try
            {
                for (int step = 0; step < 901 && game.Round.State == EDefenseState.Playing; step++)
                {
                    if (game.Round.Cooldown <= 0 && game.Round.ActiveCount >= 5)
                    {
                        Vector2 target = BestCluster(game.Round);
                        FindButton(game.gameObject, "m_btnSkill").onClick.Invoke();
                        game.SelectTarget(target);
                    }
                    game.AdvanceFrame(0.1f);
                }
                Assert.AreEqual(EDefenseState.Won, game.Round.State);
                yield return WaitForWindow<DefenseResultWindow>();
                Assert.Greater(game.BestScore, 0);
                Assert.AreEqual(game.BestScore, PlayerPrefs.GetInt(DefenseGame.BEST_SCORE_KEY));
                yield return Capture("result");

                GetWindow<DefenseResultWindow>().Replay();
                yield return null;
                Assert.AreEqual(EDefenseState.Playing, game.Round.State);
                Assert.AreEqual(0, game.Round.Score);
                Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefenseResultWindow>());
            }
            finally
            {
                if (hadBest) PlayerPrefs.SetInt(DefenseGame.BEST_SCORE_KEY, previous);
                else PlayerPrefs.DeleteKey(DefenseGame.BEST_SCORE_KEY);
                PlayerPrefs.Save();
            }
        }

        [UnityTest] public IEnumerator ClosingBattleClosesPopupsAndReopensCleanly()
        {
            yield return LoadBattle();
            GameModule.UIModule.CloseWindow<DefenseWindow>();
            yield return null; yield return null;
            Assert.AreEqual(0, Object.FindObjectsOfType<DefenseGame>().Length);
            Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefensePauseWindow>());
            Assert.IsFalse(GameModule.UIModule.ContainsWindow<DefenseResultWindow>());
        }

        #region 适配校验

        // 框架窗口不是 UnityEngine.Object，按类型逐个取已创建的实例。
        private static List<UIWindow> FindAllWindows()
        {
            var windows = new List<UIWindow>();
            void Add<T>() where T : UIWindow, new()
            {
                if (GameModule.UIModule.ContainsWindow<T>())
                    windows.Add(GameModule.UIModule.GetWindow<T>());
            }
            Add<DefenseHomeWindow>();
            Add<DefenseWindow>();
            Add<DefensePauseWindow>();
            Add<DefenseResultWindow>();
            Add<DefenseSettingsWindow>();
            return windows;
        }

        private static RectTransform SafeRootOf(UIWindow window)
            => window?.gameObject == null ? null : window.gameObject.transform.Find("m_rectSafe") as RectTransform;

        private static IEnumerator CheckLayout(Vector2Int size, Rect safe, string name)
        {
            Camera camera = GameModule.UIModule.UICamera;
            var target = new RenderTexture(size.x, size.y, 24);
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previous = RenderTexture.active;
            float aspect = camera.aspect;
            try
            {
                camera.targetTexture = target;
                camera.aspect = (float)size.x / size.y;
                yield return null;
                // 框架负责把 m_rectSafe 收进系统安全区，测试只校验控件确实留在其中。
                foreach (var window in FindAllWindows())
                {
                    var safeRoot = SafeRootOf(window);
                    if (safeRoot != null) window.SetUIFit(safeRoot);
                }
                Canvas.ForceUpdateCanvases();
                // RenderTexture 改变相机视口后，先完成 Canvas 重排，再执行正式布局逻辑。
                foreach (var window in FindAllWindows()) window.gameObject.GetComponent<DefenseLayout>().Apply();
                Canvas.ForceUpdateCanvases();
                camera.Render();

                var canvas = safe.width > 0 ? safe : new Rect(0, 0, size.x, size.y);
                foreach (var window in FindAllWindows())
                {
                    var safeRoot = SafeRootOf(window);
                    if (safeRoot != null) AssertEveryControlInsideSafeArea(safeRoot, camera, canvas, name);
                }

                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); texture.Apply(false);
                int visible = 0;
                foreach (Color32 pixel in texture.GetPixels32())
                    if (pixel.r > 30 || pixel.g > 30 || pixel.b > 30) visible++;
                Assert.Greater(visible, size.x * size.y / 30, name + " 必须有可见内容");

                string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../GameRelease/Defense90/Cute26/Screenshots"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previous;
                target.Release(); Object.Destroy(target); Object.Destroy(texture);
            }
        }

        private static void AssertEveryControlInsideSafeArea(RectTransform safeRoot, Camera camera,
            Rect safe, string label)
        {
            var corners = new Vector3[4];
            foreach (RectTransform rect in safeRoot.GetComponentsInChildren<RectTransform>(true))
            {
                bool interactive = rect.GetComponent<Button>() != null;
                if (!interactive && rect.GetComponent<Text>() == null) continue;
                if (interactive && !rect.gameObject.activeInHierarchy) continue;
                rect.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                Assert.GreaterOrEqual(min.x, safe.xMin - 1f, $"{label}/{rect.name} 左越界");
                Assert.LessOrEqual(max.x, safe.xMax + 1f, $"{label}/{rect.name} 右越界");
                Assert.GreaterOrEqual(min.y, safe.yMin - 1f, $"{label}/{rect.name} 下越界");
                Assert.LessOrEqual(max.y, safe.yMax + 1f, $"{label}/{rect.name} 上越界");
                if (interactive)
                    Assert.GreaterOrEqual((max.y - min.y) / safe.width * 720f, 60f, $"{label}/{rect.name} 触摸区过小");
            }
        }

        #endregion

        #region 加载辅助

        private static IEnumerator LoadScene()
        {
            // 普通战斗回归按已完成教学执行，教学由独立专项覆盖。
            DefenseSettings.TutorialDone = true;
            if (GameObject.Find("UIRoot") == null)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/GameStart/GameStart.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
                yield return WaitForWindow<DefenseHomeWindow>();
            }
        }

        private static IEnumerator LoadHome()
        {
            yield return LoadScene();
            GameModule.UIModule.CloseWindow<DefenseWindow>();
            GameModule.UIModule.CloseWindow<DefenseHomeWindow>();
            yield return null;
            GameModule.UIModule.ShowWindowAsync<DefenseHomeWindow>();
            yield return WaitForWindow<DefenseHomeWindow>();
        }

        private static IEnumerator LoadBattle()
        {
            yield return LoadHome();
            FindButton(GetWindow<DefenseHomeWindow>().gameObject, "m_btnStart").onClick.Invoke();
            yield return WaitForBattle();
        }

        private static IEnumerator WaitForBattle()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while (Time.realtimeSinceStartup < deadline)
            {
                var view = Object.FindObjectOfType<DefenseGame>();
                if (view != null && view.Round != null) yield break;
                yield return null;
            }
            Assert.Fail("DefenseWindow 与 DefenseAssets 未能在 60 秒内加载。");
        }

        private static IEnumerator WaitForWindow<T>() where T : UIWindow, new()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GameModule.UIModule.ContainsWindow<T>() && GameModule.UIModule.GetWindow<T>().IsPrepared) yield break;
                yield return null;
            }
            Assert.Fail($"{typeof(T).Name} 未能在 30 秒内加载。");
        }

        // 框架窗口不是 UnityEngine.Object，只能通过 UIModule 取实例。
        private static T GetWindow<T>() where T : UIWindow, new()
        {
            Assert.IsTrue(GameModule.UIModule.ContainsWindow<T>(), $"{typeof(T).Name} 未创建");
            return (T)GameModule.UIModule.GetWindow<T>();
        }

        private static DefenseWindow FindBattle()
        {
            Assert.IsTrue(GameModule.UIModule.ContainsWindow<DefenseWindow>(), "DefenseWindow 未加载");
            return GetWindow<DefenseWindow>();
        }

        private static IEnumerator Capture(string name)
        {
            var camera = GameModule.UIModule.UICamera;
            var target = new RenderTexture(720, 1280, 24);
            var texture = new Texture2D(720, 1280, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previous = RenderTexture.active;
            float aspect = camera.aspect;
            try
            {
                camera.targetTexture = target; camera.aspect = 720f / 1280f;
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var window in FindAllWindows()) window.gameObject.GetComponent<DefenseLayout>().Apply();
                Canvas.ForceUpdateCanvases();
                var game = Object.FindObjectOfType<DefenseGame>();
                if (game != null && game.Battlefield != null)
                {
                    Camera world = game.Battlefield.WorldCamera;
                    var oldWorldTarget = world.targetTexture; float oldWorldAspect = world.aspect;
                    world.targetTexture = target; world.aspect = camera.aspect;
                    game.AdvanceFrame(0); world.Render();
                    world.targetTexture = oldWorldTarget; world.aspect = oldWorldAspect;
                }
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0); texture.Apply(false);
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../GameRelease/Defense90/Cute26/Screenshots"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previous;
                target.Release(); Object.Destroy(target); Object.Destroy(texture);
            }
        }

        private static Button FindButton(GameObject root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            Assert.Fail($"未找到按钮：{name}");
            return null;
        }

        private static Vector2 BestCluster(DefenseRound round)
        {
            Vector2 best = new Vector2(320, 340); int maxCount = 0;
            for (int i = 0; i < round.Enemies.Length; i++)
            {
                if (!round.Enemies[i].Active) continue;
                int count = 0;
                for (int j = 0; j < round.Enemies.Length; j++)
                    if (round.Enemies[j].Active && (round.Enemies[j].Position - round.Enemies[i].Position).sqrMagnitude
                        <= round.Rules.SkillRadius * round.Rules.SkillRadius) count++;
                if (count > maxCount) { maxCount = count; best = round.Enemies[i].Position; }
            }
            return best;
        }

        #endregion
    }
}
#endif
