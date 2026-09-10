using TheRedDoor.Boss;
using TheRedDoor.Player;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheRedDoor.World
{
    [DisallowMultipleComponent]
    public sealed class RedDoor : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private BossHealth keeper;
        [SerializeField] private PlayerHealth player;

        [Header("Interaction")]
        [Tooltip("Maximum distance between the player and door origins, in world units.")]
        [SerializeField, Min(0.01f)] private float interactionRange = 1.5f;
        [SerializeField] private string interactActionPath = "Player/Interact";

        [Header("Ending Hook (Optional)")]
        [Tooltip("Runs once when the unlocked door opens. RedDoorUI handles the initial POC ending separately.")]
        [SerializeField] private UnityEvent onOpened = new();

        private PlayerController controller;
        private PlayerInput playerInput;
        private Rigidbody2D playerBody;
        private InputAction interactAction;

        public bool IsUnlocked => keeper != null && keeper.IsDefeated;
        public bool HasOpened { get; private set; }
        public bool CanInteract => isActiveAndEnabled && !HasOpened && IsUnlocked &&
            player != null && player.isActiveAndEnabled && !player.IsDead &&
            controller != null && controller.isActiveAndEnabled && controller.ControlsEnabled &&
            playerInput != null && playerInput.isActiveAndEnabled && playerInput.inputIsActive &&
            interactAction != null && interactAction.enabled && Time.timeScale > 0f &&
            ((Vector2)(player.transform.position - transform.position)).sqrMagnitude <=
                Mathf.Pow(Mathf.Max(0.01f, interactionRange), 2f);

        private void Awake()
        {
            if (keeper == null || player == null || keeper.gameObject.scene != gameObject.scene ||
                player.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("RedDoor needs the scene Keeper's Boss Health and the scene Player's Player Health.", this);
                enabled = false;
                return;
            }

            controller = player.GetComponent<PlayerController>();
            playerInput = player.GetComponent<PlayerInput>();
            playerBody = player.GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            // PlayerInput may create its own actions instance in OnEnable. Borrow that instance after initialization.
            if (playerInput != null && playerInput.actions != null && !string.IsNullOrEmpty(interactActionPath))
                interactAction = playerInput.actions.FindAction(interactActionPath);

            if (controller == null || playerBody == null || interactAction == null)
            {
                Debug.LogError("RedDoor needs a configured Player Controller, Rigidbody 2D and Player/Interact input action.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            // A fresh press, not Hold's performed phase: tap E works without changing the shared bindings.
            if (CanInteract && interactAction.WasPressedThisFrame())
                TryOpen();
        }

        public bool TryOpen()
        {
            if (!Application.isPlaying || !CanInteract)
                return false;

            // Latch before invoking listeners so repeated presses or reentrant events cannot reopen the door.
            HasOpened = true;
            controller.SetControlsEnabled(false);
            playerBody.linearVelocity = Vector2.zero;
            onOpened.Invoke();
            return true;
        }

        // PlayerInput owns action-map lifetime. Disabling the door must not disable the player's Interact action.
        // Opening ends this attempt; scene reload recreates both the door latch and normal player controls.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsUnlocked ? Color.green : Color.red;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, interactionRange));
        }
    }
}
