using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [RequireComponent(typeof(RawImage))]
    public sealed class DefenseBackground : MonoBehaviour
    {
        [SerializeField, Range(0, 1)] private float m_verticalAlignment = 0.5f;
        private RawImage m_image;
        // uvRect 写入会标脏重建全屏网格；只在纹理或视口尺寸真正变化时重算。
        private Texture m_appliedTexture;
        private Vector2 m_appliedViewSize = new Vector2(-1, -1);

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (m_image == null) m_image = GetComponent<RawImage>();
            if (m_image.texture == null || m_image.rectTransform.rect.height <= 0) return;
            Vector2 viewSize = m_image.rectTransform.rect.size;
            if (m_appliedTexture == m_image.texture && m_appliedViewSize == viewSize) return;
            m_appliedTexture = m_image.texture;
            m_appliedViewSize = viewSize;
            float textureAspect = (float)m_image.texture.width / m_image.texture.height;
            float viewAspect = viewSize.x / viewSize.y;
            Vector2 span = viewAspect >= textureAspect
                ? new Vector2(1, textureAspect / viewAspect)
                : new Vector2(viewAspect / textureAspect, 1);
            m_image.uvRect = new Rect((1 - span.x) * 0.5f, (1 - span.y) * m_verticalAlignment, span.x, span.y);
        }
    }
}
