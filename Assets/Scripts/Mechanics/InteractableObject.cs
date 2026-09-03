using System.Collections;
using UnityEngine;

namespace Platformer.Mechanics
{
    [RequireComponent(typeof(Collider2D))]
    public class InteractableObject : MonoBehaviour
    {
        public int Priority = 1;

        public virtual void OnInteract()
        {
            Debug.LogWarningFormat("Default Interactable Object {0} Activated! Override the OnInteract function in your InteractableObject child clkass!", gameObject.name);
        }
    }
}