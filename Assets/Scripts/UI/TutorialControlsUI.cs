using TheRedDoor.Controls;
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
        [Tooltip("Shown while a controller is the last thing the player touched. The tokens are " +
            "filled in from the pad itself, so an Xbox pad reads A and a DualSense reads Cross.")]
        [SerializeField, TextArea] private string gamepadMessage =
            "{move}  Move\n{jump}  Jump\n{dash}  Air Dash\n{attack}  Attack";

        private bool configured;
        private bool released;
        private int hintsVersion = -1;

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
            controlsText.raycastTarget = false;
            RefreshWording();
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            RefreshWording();
            RefreshVisibility();
        }

        // Re-words only when the answer actually changed, so picking up a pad mid-run re-labels the
        // list and putting it down puts the keys back.
        private void RefreshWording()
        {
            if (!configured || hintsVersion == InputDeviceHints.Version)
                return;
            hintsVersion = InputDeviceHints.Version;
            controlsText.text = InputDeviceHints.UsingGamepad
                ? InputDeviceHints.Format(gamepadMessage)
                : message;
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
