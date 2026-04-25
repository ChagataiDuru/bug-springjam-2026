using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        float InteractionDuration { get; }
        bool CanInteract(GameObject interactor);
        void OnInteractionComplete(GameObject interactor);
    }
}
