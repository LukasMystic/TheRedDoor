using UnityEngine;

namespace TheRedDoor.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController), typeof(Rigidbody2D))]
    public sealed class PlayerAnimationController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Locomotion States")]
        [SerializeField] private string layerName = "Base Layer";
        [SerializeField] private string idleStateName = "Player_Idle";
        [SerializeField] private string runStateName = "Player_Run";
        [SerializeField] private string jumpStateName = "Player_Jump";
        [SerializeField] private string fallStateName = "Player_Fall";

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float moveSpeedThreshold = 0.1f;
        [SerializeField, Min(0f)] private float upwardSpeedThreshold = 0.1f;
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.05f;

        private PlayerController controller;
        private Rigidbody2D body;
        private int idleStateHash;
        private int runStateHash;
        private int jumpStateHash;
        private int fallStateHash;
        private int currentStateHash;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            body = GetComponent<Rigidbody2D>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>();

            idleStateHash = HashStateName(idleStateName);
            runStateHash = HashStateName(runStateName);
            jumpStateHash = HashStateName(jumpStateName);
            fallStateHash = HashStateName(fallStateName);
        }

        private void Start()
        {
            if (!ValidateSetup())
                enabled = false;
        }

        private void OnEnable()
        {
            currentStateHash = 0;
        }

        private void LateUpdate()
        {
            int desiredStateHash = SelectLocomotionState();
            if (desiredStateHash == currentStateHash)
                return;

            animator.CrossFade(desiredStateHash, Mathf.Max(0f, crossFadeDuration), 0);
            currentStateHash = desiredStateHash;
        }

        private int SelectLocomotionState()
        {
            if (!controller.IsGrounded)
                return body.linearVelocity.y > upwardSpeedThreshold ? jumpStateHash : fallStateHash;

            return Mathf.Abs(body.linearVelocity.x) > moveSpeedThreshold ? runStateHash : idleStateHash;
        }

        private int HashStateName(string stateName)
        {
            return Animator.StringToHash($"{layerName}.{stateName}");
        }

        private bool ValidateSetup()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("PlayerAnimationController needs the Player Visual Animator and its controller.", this);
                return false;
            }

            if (!animator.HasState(0, idleStateHash) || !animator.HasState(0, runStateHash) ||
                !animator.HasState(0, jumpStateHash) || !animator.HasState(0, fallStateHash))
            {
                Debug.LogError(
                    "PlayerAnimator must contain Player_Idle, Player_Run, Player_Jump, and Player_Fall states on Base Layer.",
                    this);
                return false;
            }

            return true;
        }
    }
}
