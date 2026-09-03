using TheRedDoor.World;
using TMPro;
using UnityEngine;

namespace TheRedDoor.UI
{
    // Put this on an always-active UI root. The overlay needs a full-screen black Image.
    [DisallowMultipleComponent]
    public sealed class RedDoorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RedDoor door;
        [SerializeField] private TMP_Text interactionPrompt;
        [Tooltip("Canvas Group on a full-screen black panel, with Ending Text beneath it in the Hierarchy.")]
        [SerializeField] private CanvasGroup endingOverlay;
        [SerializeField] private TMP_Text endingText;

        [Header("Presentation")]
        [SerializeField] private string promptMessage = "Press E to Open";
        [SerializeField, Min(0f)] private float fadeDuration = 0.5f;
        [SerializeField, TextArea] private string endingMessage = "THE RED DOOR\n\nEnd of proof of concept";

        private bool configured;

        private void Awake()
        {
            if (door == null || interactionPrompt == null || endingOverlay == null || endingText == null)
            {
                Debug.LogError("RedDoorUI needs Door, Interaction Prompt, Ending Overlay and Ending Text references.", this);
                enabled = false;
                return;
            }

            if (interactionPrompt == endingText || interactionPrompt.transform.IsChildOf(endingOverlay.transform) ||
                !endingText.transform.IsChildOf(endingOverlay.transform))
            {
                Debug.LogError("Keep Interaction Prompt outside Ending Overlay, and Ending Text inside it.", this);
                enabled = false;
                return;
            }

            configured = true;
            interactionPrompt.text = promptMessage;
            endingText.text = endingMessage;
            interactionPrompt.raycastTarget = false;
            endingText.raycastTarget = false;
            HidePresentation();
        }

        private void LateUpdate()
        {
            if (!configured || door == null)
            {
                HidePresentation();
                return;
            }

            interactionPrompt.enabled = door.CanInteract;
            endingOverlay.interactable = false;
            endingOverlay.blocksRaycasts = door.HasOpened;

            if (!door.HasOpened)
            {
                endingOverlay.alpha = 0f;
                endingText.enabled = false;
                return;
            }

            endingOverlay.alpha = fadeDuration <= 0f ? 1f :
                Mathf.MoveTowards(endingOverlay.alpha, 1f, Time.unscaledDeltaTime / fadeDuration);
            endingText.enabled = endingOverlay.alpha >= 1f;
        }

        private void OnDisable()
        {
            HidePresentation();
        }

        private void HidePresentation()
        {
            if (interactionPrompt != null)
                interactionPrompt.enabled = false;
            if (endingText != null)
                endingText.enabled = false;
            if (endingOverlay != null)
            {
                endingOverlay.alpha = 0f;
                endingOverlay.interactable = false;
                endingOverlay.blocksRaycasts = false;
            }
        }
    }
}
