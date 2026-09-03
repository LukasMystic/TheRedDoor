using UnityEngine;
using UnityEngine.InputSystem;

namespace TheRedDoor.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(PlayerInput))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float maxMoveSpeed = 8f;
        [SerializeField, Min(0f)] private float groundAcceleration = 80f;
        [SerializeField, Min(0f)] private float groundDeceleration = 100f;
        [SerializeField, Min(0f)] private float airAcceleration = 50f;
        [SerializeField, Min(0f)] private float airDeceleration = 50f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpForce = 14f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
        [SerializeField, Min(0f)] private float jumpBufferDuration = 0.12f;
        [SerializeField, Range(0.1f, 1f)] private float jumpCutMultiplier = 0.5f;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Vector2 groundCheckSize = new(0.6f, 0.12f);
        [SerializeField] private LayerMask groundLayer;

        [Header("Facing")]
        [Tooltip("Assign a child containing the sprite and, later, the attack point. The player root is used when left empty.")]
        [SerializeField] private Transform facingRoot;

        [Header("Input Actions")]
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string jumpActionName = "Jump";

        private Rigidbody2D body;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction jumpAction;
        private Vector3 facingRootInitialScale;

        private float moveInput;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float knockbackEndTime;
        private bool jumpCutRequested;
        private bool controlsEnabled = true;
        private bool isFacingRight = true;

        public bool ControlsEnabled => controlsEnabled && Time.time >= knockbackEndTime;
        public bool IsGrounded { get; private set; }
        public bool IsFacingRight => isFacingRight;
        public float HorizontalInput => moveInput;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            playerInput = GetComponent<PlayerInput>();
            facingRoot = facingRoot != null ? facingRoot : transform;
            facingRootInitialScale = facingRoot.localScale;

            if (playerInput.actions == null)
            {
                Debug.LogError("PlayerInput needs an Input Actions asset before PlayerController can read input.", this);
                enabled = false;
                return;
            }

            moveAction = playerInput.actions.FindAction($"{actionMapName}/{moveActionName}", true);
            jumpAction = playerInput.actions.FindAction($"{actionMapName}/{jumpActionName}", true);
        }

        private void OnEnable()
        {
            moveAction?.Enable();
            jumpAction?.Enable();
        }

        private void OnDisable()
        {
            moveAction?.Disable();
            jumpAction?.Disable();
        }

        private void Update()
        {
            if (!ControlsEnabled)
            {
                moveInput = 0f;
                return;
            }

            moveInput = moveAction.ReadValue<Vector2>().x;

            if (jumpAction.WasPressedThisFrame())
            {
                jumpBufferTimer = jumpBufferDuration;
            }

            if (jumpAction.WasReleasedThisFrame())
            {
                jumpCutRequested = true;
            }

            UpdateFacing();
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGrounded();
            UpdateJumpTimers();

            // Let physics carry the hit's velocity until the short stun ends.
            if (Time.time < knockbackEndTime)
                return;

            if (ControlsEnabled)
            {
                TryJump();
                ApplyJumpCut();
            }

            ApplyHorizontalMovement();
        }

        public void SetControlsEnabled(bool value)
        {
            controlsEnabled = value;

            if (!value)
            {
                moveInput = 0f;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                jumpCutRequested = false;
            }
        }

        public void ApplyKnockback(Vector2 velocity, float duration)
        {
            moveInput = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            jumpCutRequested = false;
            knockbackEndTime = Time.time + Mathf.Max(Time.fixedDeltaTime, duration);
            body.linearVelocity = velocity;
        }

        private void ApplyHorizontalMovement()
        {
            float targetSpeed = controlsEnabled ? moveInput * maxMoveSpeed : 0f;
            bool isAccelerating = Mathf.Abs(targetSpeed) > 0.01f;
            float acceleration = SelectAcceleration(isAccelerating);
            float nextSpeed = Mathf.MoveTowards(body.linearVelocity.x, targetSpeed, acceleration * Time.fixedDeltaTime);

            body.linearVelocity = new Vector2(nextSpeed, body.linearVelocity.y);
        }

        private float SelectAcceleration(bool isAccelerating)
        {
            if (IsGrounded)
            {
                return isAccelerating ? groundAcceleration : groundDeceleration;
            }

            return isAccelerating ? airAcceleration : airDeceleration;
        }

        private void UpdateJumpTimers()
        {
            coyoteTimer = IsGrounded
                ? coyoteTime
                : Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);

            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.fixedDeltaTime);
        }

        private void TryJump()
        {
            if (jumpBufferTimer <= 0f || coyoteTimer <= 0f)
            {
                return;
            }

            body.linearVelocity = new Vector2(body.linearVelocity.x, jumpForce);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            IsGrounded = false;
        }

        private void ApplyJumpCut()
        {
            if (!jumpCutRequested)
            {
                return;
            }

            if (body.linearVelocity.y > 0f)
            {
                body.linearVelocity = new Vector2(body.linearVelocity.x, body.linearVelocity.y * jumpCutMultiplier);
            }

            jumpCutRequested = false;
        }

        private bool CheckGrounded()
        {
            if (groundCheck == null)
            {
                return false;
            }

            return Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer) != null;
        }

        private void UpdateFacing()
        {
            if (Mathf.Abs(moveInput) < 0.01f)
            {
                return;
            }

            isFacingRight = moveInput > 0f;
            float xScale = Mathf.Abs(facingRootInitialScale.x) * (isFacingRight ? 1f : -1f);
            facingRoot.localScale = new Vector3(xScale, facingRootInitialScale.y, facingRootInitialScale.z);
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null)
            {
                return;
            }

            Gizmos.color = IsGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
        }

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            groundCheck = transform;
            facingRoot = transform;

            if (body != null)
            {
                body.freezeRotation = true;
            }
        }
    }
}
