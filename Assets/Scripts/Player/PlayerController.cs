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

        [Header("Dash")]
        [SerializeField, Min(0.01f)] private float dashSpeed = 18f;
        [SerializeField, Min(0.01f)] private float dashDuration = 0.15f;
        [Tooltip("Wait after a dash ends before a fresh press can start another.")]
        [SerializeField, Min(0f)] private float dashCooldown = 0.6f;
        [SerializeField] private bool invulnerableDuringDash = true;

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
        [Tooltip("The existing Sprint action is bound to Left Shift. No input asset rename is needed.")]
        [SerializeField] private string dashActionName = "Sprint";

        private Rigidbody2D body;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction dashAction;
        private Vector3 facingRootInitialScale;

        private float moveInput;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float knockbackEndTime;
        private float dashTimeRemaining;
        private float nextDashTime;
        private float dashDirection;
        private float gravityBeforeDash;
        private bool jumpCutRequested;
        private bool controlsEnabled = true;
        private bool isFacingRight = true;

        public bool ControlsEnabled => controlsEnabled && !IsDashing && Time.time >= knockbackEndTime;
        // Distinguishes death/ending locks from temporary dash and knockback restrictions.
        public bool IsControlLocked => !controlsEnabled;
        public bool IsGrounded { get; private set; }
        public bool IsDashing { get; private set; }
        public bool HasDashInvulnerability => isActiveAndEnabled && IsDashing && invulnerableDuringDash;
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

        private void Start()
        {
            // Borrow PlayerInput's initialized action instance; it owns the Sprint action's lifetime.
            dashAction = playerInput.actions.FindAction($"{actionMapName}/{dashActionName}");
            if (dashAction == null)
                Debug.LogWarning("PlayerController cannot dash: check Dash Action Name in the assigned Input Actions asset.", this);
        }

        private void OnEnable()
        {
            moveAction?.Enable();
            jumpAction?.Enable();
        }

        private void OnDisable()
        {
            EndDash();
            ClearInput();
            moveAction?.Disable();
            jumpAction?.Disable();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f)
                return;

            if (!playerInput.isActiveAndEnabled || !playerInput.inputIsActive ||
                (IsDashing && !dashAction.enabled))
            {
                EndDash();
                ClearInput();
                return;
            }

            if (!ControlsEnabled)
            {
                ClearInput();
                return;
            }

            moveInput = moveAction.ReadValue<Vector2>().x;
            UpdateFacing();

            // Holding Shift does not repeat, and presses during cooldown are not buffered.
            if (dashAction != null && dashAction.WasPressedThisFrame() && TryDash())
                return;

            if (jumpAction.WasPressedThisFrame())
            {
                jumpBufferTimer = jumpBufferDuration;
            }

            if (jumpAction.WasReleasedThisFrame())
            {
                jumpCutRequested = true;
            }
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGrounded();

            if (IsDashing)
            {
                if (dashTimeRemaining > 0f)
                {
                    body.linearVelocity = new Vector2(dashDirection * Mathf.Max(0.01f, dashSpeed), 0f);
                    dashTimeRemaining -= Time.fixedDeltaTime;
                    return;
                }

                EndDash();
            }

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
                EndDash();
                ClearInput();
            }
        }

        public void ApplyKnockback(Vector2 velocity, float duration)
        {
            EndDash();
            ClearInput();
            knockbackEndTime = Time.time + Mathf.Max(Time.fixedDeltaTime, duration);
            body.linearVelocity = velocity;
        }

        public bool TryDash()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !ControlsEnabled ||
                body == null || Time.timeScale <= 0f || Time.time < nextDashTime ||
                playerInput == null || !playerInput.isActiveAndEnabled || !playerInput.inputIsActive ||
                dashAction == null || !dashAction.enabled)
                return false;

            dashDirection = isFacingRight ? 1f : -1f;
            gravityBeforeDash = body.gravityScale;
            dashTimeRemaining = Mathf.Max(Time.fixedDeltaTime, dashDuration);
            IsDashing = true;
            ClearInput();
            body.gravityScale = 0f;
            body.linearVelocity = new Vector2(dashDirection * Mathf.Max(0.01f, dashSpeed), 0f);
            return true;
        }

        private void EndDash()
        {
            if (!IsDashing)
                return;

            IsDashing = false;
            dashTimeRemaining = 0f;
            nextDashTime = Time.time + Mathf.Max(0f, dashCooldown);

            if (body != null)
            {
                body.gravityScale = gravityBeforeDash;
                // Do not carry dash speed into normal movement, or restore an old upward jump velocity.
                body.linearVelocity = new Vector2(
                    Mathf.Clamp(body.linearVelocity.x, -maxMoveSpeed, maxMoveSpeed), body.linearVelocity.y);
            }
        }

        private void ClearInput()
        {
            moveInput = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            jumpCutRequested = false;
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
