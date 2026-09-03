using System.Collections;
using UnityEngine;
using UnityEngine.Android;

namespace Platformer.Mechanics
{
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractionComponent : MonoBehaviour
    {
        //interaction range
        public float Range = 1f;
        ContactFilter2D m_filter;

        void Awake()
        {
            m_filter = new();
            m_filter.ClearDepth();
            m_filter.ClearNormalAngle();
            m_filter.SetLayerMask(LayerMask.NameToLayer("Interaction"));
        }

        public InteractableObject GetBestInteractable()
        {
            PlayerController pc = gameObject.GetComponent<PlayerController>();
            Collider2D[] interactables = Physics2D.OverlapCircleAll(pc.Bounds.center, Range, LayerMask.GetMask("Interaction"));
            InteractableObject res = null;
            int maxPriority = -1;

            foreach(Collider2D col in interactables)
            {
                InteractableObject io = col.gameObject.GetComponent<InteractableObject>();
                if(io != null && io.Priority > maxPriority)
                {
                    res = io;
                    maxPriority = io.Priority;
                }
            }

            return res;
        }
        
    }
}