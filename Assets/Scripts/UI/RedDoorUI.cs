using TheRedDoor.Controls;
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
        [Tooltip("Used while a controller is in the player's hands. {interact} is filled in from the pad.")]
        [SerializeField] private string gamepadPromptMessage = "Press {interact} to Open";
        [SerializeField, Min(0f)] private float fadeDuration = 0.5f;
        [SerializeField, TextArea] private string endingMessage = "THE RED DOOR\n\nEnd of Demo";

        private bool configured;
        private int hintsVersion = -1;

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
            RefreshWording();
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

            RefreshWording();
            interactionPrompt.enabled = door.CanInteract;
            endingOverlay.interactable = false;
            endingOverlay.blocksRaycasts = door.HasOpened && !GameFlowUI.OwnsEnding;

            if (!door.HasOpened || GameFlowUI.OwnsEnding)
            {
                endingOverlay.alpha = 0f;
                endingText.enabled = false;
                return;
            }

            endingOverlay.alpha = fadeDuration <= 0f ? 1f :
                Mathf.MoveTowards(endingOverlay.alpha, 1f, Time.unscaledDeltaTime / fadeDuration);
            endingText.enabled = endingOverlay.alpha >= 1f;
        }

        // The door is the one prompt that names a single button, so it has to follow the device the
        // player is actually holding rather than assuming a keyboard.
        private void RefreshWording()
        {
            if (interactionPrompt == null || hintsVersion == InputDeviceHints.Version)
                return;
            hintsVersion = InputDeviceHints.Version;
            interactionPrompt.text = InputDeviceHints.UsingGamepad
                ? InputDeviceHints.Format(gamepadPromptMessage)
                : promptMessage;
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
