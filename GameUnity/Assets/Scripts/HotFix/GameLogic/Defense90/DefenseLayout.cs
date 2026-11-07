using UnityEngine;

namespace GameLogic.Defense90
{
    // 各容器按 720x1280 设计尺寸统一等比缩放进安全区，锚点决定贴顶、贴底或居中：长屏多出的高度留在容器之间。
    public sealed class DefenseLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform m_area;
        [SerializeField] private RectTransform[] m_contents;
        [SerializeField] private Vector2 m_designSize = new Vector2(720f, 1280f);

        private void LateUpdate() => Apply();

        public void Apply()
        {
            Rect area = m_area.rect;
            if (area.width <= 0f || area.height <= 0f) return;
            float scale = Mathf.Min(area.width / m_designSize.x, area.height / m_designSize.y);
            foreach (RectTransform content in m_contents)
                content.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
