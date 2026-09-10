using TheRedDoor.World;
using TMPro;
using UnityEngine;

namespace TheRedDoor.UI
{
    // Keeps the demo controls visible during the tutorial, then clears the screen for the boss fight.
    [DisallowMultipleComponent]
    public sealed class TutorialControlsUI : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private RespawnManager respawnManager;
        [SerializeField] private TMP_Text controlsText;

        [Header("Presentation")]
        [SerializeField, TextArea] private string message =
            "A / D or Arrow Keys  Move\nSpace  Jump\nLeft Shift  Air Dash\nJ  Attack";

        private bool configured;
        private bool released;

        // WorldStory takes this label over once the player has read the controls, so the same slot
        // becomes the objective line instead of a second HUD element competing with it.
        public TMP_Text ControlsText => controlsText;

        public void ReleaseControl()
        {
            released = true;
            enabled = false;
        }

        private void Awake()
        {
            if (respawnManager == null || controlsText == null ||
                respawnManager.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("TutorialControlsUI needs the scene Respawn Manager and a controls text reference.", this);
                enabled = false;
                return;
            }

            configured = true;
            controlsText.text = message;
            controlsText.raycastTarget = false;
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            RefreshVisibility();
        }

        private void OnDisable()
        {
            if (!released && controlsText != null)
                controlsText.enabled = false;
        }

        private void RefreshVisibility()
        {
            if (configured)
                controlsText.enabled = !respawnManager.HasArenaCheckpoint;
        }
    }
}
