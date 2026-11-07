using System;
using System.Collections;
using System.Reflection;
using GameLogic.Defense90;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Defense90Tests
{
    public sealed class DefenseAudioLoadingTests
    {
        private const BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;
        private static Type RuntimeType(string name) => Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name == "DGame.Runtime").GetType("DGame." + name, true);

        [Test]
        public void ReapplyingMusicVolumeDoesNotRestartPausedOrPendingPlayback()
        {
            var go = new GameObject("MusicRequestRegression");
            var clip = AudioClip.Create("ReadyMusic", 44100, 1, 44100, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var agentType = RuntimeType("AudioSourceAgent");
            var audio = Activator.CreateInstance(agentType);
            agentType.GetField("m_audioSource", PRIVATE).SetValue(audio, source);
            agentType.GetField("m_transform", PRIVATE).SetValue(audio, go.transform);
            try
            {
                var optionsType = RuntimeType("AudioPlayOptions");
                var options = optionsType.GetMethod("CreateDefault").Invoke(null, null);
                optionsType.GetField("IsLoop").SetValue(options, true);
                optionsType.GetField("Volume").SetValue(options, .6f);
                agentType.GetMethod("LoadClip", PRIVATE).Invoke(audio, new object[] { clip, options });
                source.Pause();
                source.timeSamples = 4096;
                Assert.AreEqual(4096, source.timeSamples);
                agentType.GetProperty("Volume").SetValue(audio, .25f);
                Assert.AreEqual(4096, source.timeSamples, "isPlaying=false时重复设置音量不应Stop/Play同一音乐");
                Assert.AreEqual(.25f, source.volume);
                Assert.IsTrue(source.loop);
                agentType.GetMethod("Stop").Invoke(audio, new object[] { false });
                agentType.GetMethod("Update").Invoke(audio, new object[] { .1f });
                Assert.IsNull(source.clip, "停止时清除等待播放的请求和音源引用，不在后续帧复活");
            }
            finally
            {
                agentType.GetMethod("Stop").Invoke(audio, new object[] { false });
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(clip);
            }
        }

        [UnityTest]
        public IEnumerator UnloadedAudioDoesNotExposeLengthUntilDataIsReady()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/BundleAssets/Audios/Defense/BGM/Home.ogg");
            var go = new GameObject("AudioReadinessRegression");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var agentType = RuntimeType("AudioSourceAgent");
            var agent = Activator.CreateInstance(agentType);
            try
            {
                Assert.IsTrue(clip.UnloadAudioData());
                Assert.AreEqual(AudioDataLoadState.Unloaded, clip.loadState);
                source.clip = clip;
                agentType.GetField("m_audioSource", PRIVATE).SetValue(agent, source);
                Assert.AreEqual(0f, agentType.GetProperty("Length").GetValue(agent),
                    "句柄/引用存在不能代替音频数据就绪；读取时长前须检查解码状态");
            }
            finally
            {
                source.clip = null;
                Object.DestroyImmediate(go);
                clip.LoadAudioData();
            }
            while (clip.loadState == AudioDataLoadState.Loading) yield return null;
            Assert.AreEqual(AudioDataLoadState.Loaded, clip.loadState);
        }

        [UnityTest]
        public IEnumerator DisablingAutomaticCollectionDoesNotStartGlobalSweep()
        {
            var go = new GameObject("ResourceCollectionRegression");
            go.SetActive(false);
            var driverType = RuntimeType("ResourceModuleDriver");
            var driver = go.AddComponent(driverType);
            driverType.GetProperty("UseSystemUnloadUnusedAssets").SetValue(driver, false);
            driverType.GetProperty("MaxUnloadUnusedAssetsInterval").SetValue(driver, 0f);
            driverType.GetMethod("Update", PRIVATE).Invoke(driver, null);
            var operation = (AsyncOperation)driverType.GetField("m_asyncOperation", PRIVATE).GetValue(driver);
            Object.DestroyImmediate(go);
            // 即使旧实现错误发起扫描，也先等其结束，再报告失败，避免污染后续用例。
            if (operation != null) while (!operation.isDone) yield return null;
            Assert.IsNull(operation, "自动回收开关关闭时不应发起全局扫描");
        }
    }
}
