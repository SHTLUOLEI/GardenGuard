using UnityEngine;

namespace DGame
{
    /// <summary>资源句柄完成后，音频数据仍可能在后台解码。</summary>
    public static class AudioClipLoading
    {
        public static AudioDataLoadState Request(AudioClip clip)
        {
            if (clip == null) return AudioDataLoadState.Failed;
            if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
                return AudioDataLoadState.Failed;
            return clip.loadState;
        }
    }
}
