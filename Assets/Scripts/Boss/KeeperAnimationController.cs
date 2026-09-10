using UnityEngine;

namespace TheRedDoor.Boss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeeperController), typeof(BossHealth))]
    public sealed class KeeperAnimationController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Animation States")]
        [SerializeField] private string layerName = "Base Layer";
        [SerializeField] private string idleStateName = "Keeper_Idle";
        [SerializeField] private string attackOneStateName = "Keeper_Attack1";
        [SerializeField] private string chargeStateName = "Keeper_Charge";
        [SerializeField] private string groundSlamStateName = "Keeper_GroundSlam";
        [SerializeField] private string heavyStrikeStateName = "Keeper_HeavyStrike";
        [SerializeField] private string deathStateName = "Keeper_Death";

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.05f;
        [Tooltip("World units per second the walk cycle was authored for. Playback scales with the Keeper's real speed so his feet keep up with the ground instead of skating.")]
        [SerializeField, Min(0.1f)] private float walkReferenceSpeed = 8.1f;
        [SerializeField] private Vector2 walkSpeedRange = new(0.4f, 1.8f);

        private KeeperController controller;
        private int idleStateHash;
        private int attackOneStateHash;
        private int chargeStateHash;
        private int groundSlamStateHash;
        private int heavyStrikeStateHash;
        private int deathStateHash;
        private int currentStateHash;
        private SpriteRenderer visual;
        private BoxCollider2D bodyCollider;
        private Vector3 authoredVisualPosition;
        private bool wasGrounded;
        private float lastRootX;
        private bool hasLastRootX;

        private void Awake()
        {
            controller = GetComponent<KeeperController>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>();
            visual = animator != null ? animator.GetComponent<SpriteRenderer>() : null;
            bodyCollider = GetComponent<BoxCollider2D>();
            if (visual != null)
                authoredVisualPosition = visual.transform.localPosition;

            idleStateHash = HashStateName(idleStateName);
            attackOneStateHash = HashStateName(attackOneStateName);
            chargeStateHash = HashStateName(chargeStateName);
            groundSlamStateHash = HashStateName(groundSlamStateName);
            heavyStrikeStateHash = HashStateName(heavyStrikeStateName);
            deathStateHash = HashStateName(deathStateName);
        }

        private void Start()
        {
            if (!ValidateSetup())
                enabled = false;
        }

        private void OnEnable()
        {
            currentStateHash = 0;
            hasLastRootX = false;
            if (animator != null)
                animator.speed = 1f;
        }

        private void LateUpdate()
        {
            UpdateWalkPlaybackSpeed();

            int desiredStateHash = SelectAnimationState();
            if (desiredStateHash != currentStateHash)
            {
                animator.CrossFade(desiredStateHash, Mathf.Max(0f, crossFadeDuration), 0);
                currentStateHash = desiredStateHash;
            }

            // Individually trimmed death drawings have different bottoms. Align the child after
            // animation evaluation, never the root/hitbox. TransformPoint works even after the
            // arena gate disables the defeated body's collider (when collider.bounds is empty).
            if (visual == null || bodyCollider == null || visual.sprite == null)
                return;
            if (controller.CurrentState == KeeperController.State.Defeated)
            {
                Vector2 localFoot = bodyCollider.offset + Vector2.down * bodyCollider.size.y * 0.5f;
                float floorY = bodyCollider.transform.TransformPoint(localFoot).y;
                Vector3 position = visual.transform.position;
                position.y += floorY - visual.bounds.min.y;
                visual.transform.position = position;
                wasGrounded = true;
            }
            else if (wasGrounded)
            {
                visual.transform.localPosition = authoredVisualPosition;
                wasGrounded = false;
            }
        }

        // Measured from the root rather than read off the controller, so it stays right whatever moves him:
        // charge, heavy advance, or anything added later.
        private void UpdateWalkPlaybackSpeed()
        {
            float x = transform.position.x;
            float speed = 0f;
            if (hasLastRootX && Time.deltaTime > 0f)
                speed = Mathf.Abs(x - lastRootX) / Time.deltaTime;
            lastRootX = x;
            hasLastRootX = true;

            KeeperController.State state = controller.CurrentState;
            bool walking = state == KeeperController.State.Charge ||
                state == KeeperController.State.HeavyAdvance;

            animator.speed = walking
                ? Mathf.Clamp(speed / Mathf.Max(0.1f, walkReferenceSpeed),
                    Mathf.Min(walkSpeedRange.x, walkSpeedRange.y),
                    Mathf.Max(walkSpeedRange.x, walkSpeedRange.y))
                : 1f;
        }

        private int SelectAnimationState()
        {
            KeeperController.State state = controller.CurrentState;
            if (state == KeeperController.State.Defeated)
                return deathStateHash;

            if (state == KeeperController.State.Telegraph || state == KeeperController.State.Swipe)
                return attackOneStateHash;

            if (state == KeeperController.State.Charge || state == KeeperController.State.HeavyAdvance)
                return chargeStateHash;

            if (state == KeeperController.State.SlamTelegraph || state == KeeperController.State.Slam)
                return groundSlamStateHash;

            if (state == KeeperController.State.HeavyWindup || state == KeeperController.State.HeavyStrike)
                return heavyStrikeStateHash;

            // Stationary attacks have authored return poses; moving attacks return to Idle when movement stops.
            if (state == KeeperController.State.Recovery
                && (currentStateHash == attackOneStateHash
                    || currentStateHash == groundSlamStateHash
                    || currentStateHash == heavyStrikeStateHash))
            {
                return currentStateHash;
            }

            return idleStateHash;
        }

        private int HashStateName(string stateName)
        {
            return Animator.StringToHash($"{layerName}.{stateName}");
        }

        private bool ValidateSetup()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("KeeperAnimationController needs the Keeper Visual Animator and its controller.", this);
                return false;
            }

            if (!animator.HasState(0, idleStateHash)
                || !animator.HasState(0, attackOneStateHash)
                || !animator.HasState(0, chargeStateHash)
                || !animator.HasState(0, groundSlamStateHash)
                || !animator.HasState(0, heavyStrikeStateHash)
                || !animator.HasState(0, deathStateHash))
            {
                Debug.LogError(
                    "KeeperAnimator must contain the configured Idle, Attack 1, Charge, Ground Slam, and " +
                    "Heavy Strike, and Death states on Base Layer.",
                    this);
                return false;
            }

            return true;
        }
    }
}
