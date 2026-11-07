using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic.Defense90
{
    // 技能按钮：按住即瞄准，拖到战场后松手释放；拖回按钮或拖出战场松手取消。单击仍走 Button.onClick 的“点按钮→点战场”备用流程。
    public sealed class DefenseSkillDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private DefenseGame m_game;

        public void OnPointerDown(PointerEventData data) => m_game.SkillPress();
        public void OnBeginDrag(PointerEventData data) => m_game.SkillDrag(data);
        public void OnDrag(PointerEventData data) => m_game.SkillDrag(data);
        public void OnEndDrag(PointerEventData data) => m_game.SkillRelease(data);
    }
}
