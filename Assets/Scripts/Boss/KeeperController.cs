using System.Collections.Generic;
using TheRedDoor.Player;
using UnityEngine;

namespace TheRedDoor.Boss
{
    // Two readable attacks on a flat arena floor. Animation and later attacks stay separate.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossHealth), typeof(Rigidbody2D))]
    public sealed class KeeperController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHealth target;
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("When To Attack")]
        [Tooltip("Horizontal distance between character origins that starts a warning, in world units.")]
        [SerializeField, Min(0.01f)] private float activationRange = 2.3f;
        [SerializeField, Min(0f)] private float maxVerticalDistance = 1.5f;

        [Header("Swipe Hitbox")]
        [SerializeField, Min(1)] private int damage = 1;
        [Tooltip("Hitbox center relative to the boss origin, in WORLD units. X is forward and mirrors with facing.")]
        [SerializeField] private Vector2 hitboxOffset = new(0.9f, 0f);
        [Tooltip("Width and height in WORLD units, independent of the boss Transform scale.")]
        [SerializeField] private Vector2 hitboxSize = new(1.4f, 1.1f);

        [Header("Timing")]
        [SerializeField, Min(0.01f)] private float telegraphDuration = 0.6f;
        [SerializeField, Min(0.01f)] private float activeDuration = 0.12f;
        [Tooltip("Safe counterattack window after each swipe. Also controls attack frequency.")]
        [SerializeField, Min(0.01f)] private float recoveryDuration = 1.2f;

        [Header("Charge")]
        [Tooltip("Outside swipe range the Keeper charges. Within swipe range it alternates swipe and charge.")]
        [SerializeField, Min(0.01f)] private float chargeActivationRange = 6f;
        [SerializeField, Min(0.01f)] private float chargeTelegraphDuration = 0.85f;
        [SerializeField, Min(0.01f)] private float chargeSpeed = 9f;
        [SerializeField, Min(0.01f)] private float chargeDuration = 0.65f;
        [SerializeField, Min(0.01f)] private float chargeRecoveryDuration = 1.4f;
        [SerializeField, Min(1)] private int chargeDamage = 1;

        [Header("Flat Arena Limits")]
        [Tooltip("World X limits for the boss ROOT, leaving room for its collider inside the floor edges.")]
        [SerializeField] private Vector2 arenaXLimits = new(-6.5f, 6.5f);
        [Tooltip("Small world-space gap maintained before solid obstacles or the player.")]
        [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;

        [Header("Temporary Visual Feedback")]
        [Tooltip("Whether the unflipped sprite faces right. This never flips the boss collider.")]
        [SerializeField] private bool spriteFacesRight = true;
        [SerializeField] private Color telegraphColor = new(1f, 0.65f, 0.1f, 1f);
        [SerializeField] private Color swipeColor = new(1f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color chargeTelegraphColor = new(0.3f, 0.75f, 1f, 1f);
        [SerializeField] private Color chargeColor = new(0.75f, 0.3f, 1f, 1f);

        public enum State { Idle, Telegraph, Swipe, Recovery, Defeated, ChargeTelegraph, Charge }
        public State CurrentState { get; private set; }
        public bool IsFacingRight => facingDirection > 0f;

        private readonly List<Collider2D> overlaps = new(8);
        private readonly List<RaycastHit2D> chargeHits = new(8);
        private BossHealth health;
        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private Color originalColor;
        private float facingDirection = 1f;
        private float stateTimeRemaining;
        private bool hitAttempted;
        private bool visualCached;
        private bool preferCharge;
        private bool finishChargeAfterStep;

        private Vector2 HitboxCenter => (Vector2)transform.position +
            new Vector2(Mathf.Abs(hitboxOffset.x) * facingDirection, hitboxOffset.y);
        private Vector2 HitboxSize => new(
            Mathf.Max(0.01f, hitboxSize.x), Mathf.Max(0.01f, hitboxSize.y));

        private void Awake()
        {
            health = GetComponent<BossHealth>();
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (target == null || target.gameObject.scene != gameObject.scene || spriteRenderer == null)
            {
                Debug.LogError("KeeperController needs the scene Player as Target and the boss Sprite Renderer.", this);
                enabled = false;
                return;
            }

            if (body == null || body.bodyType != RigidbodyType2D.Kinematic ||
                bodyCollider == null || bodyCollider.isTrigger || !bodyCollider.enabled || !body.simulated)
            {
                Debug.LogError("KeeperController needs a simulated Kinematic Rigidbody 2D and an enabled, non-trigger collider on the boss root.", this);
                enabled = false;
                return;
            }

            if (arenaXLimits.x >= arenaXLimits.y || body.position.x < arenaXLimits.x || body.position.x > arenaXLimits.y)
            {
                Debug.LogError("KeeperController Arena X Limits must be ordered left to right and contain the boss root. Keep both inside the flat floor edges.", this);
                enabled = false;
                return;
            }

            originalColor = spriteRenderer.color;
            visualCached = true;
        }

        private void OnEnable()
        {
            if (health != null)
                health.Defeated.AddListener(HandleDefeat);
            if (target != null)
                target.Died.AddListener(HandleTargetDeath);
            preferCharge = false;
            SetState(State.Idle);
        }

        private void OnDisable()
        {
            if (health != null)
                health.Defeated.RemoveListener(HandleDefeat);
            if (target != null)
                target.Died.RemoveListener(HandleTargetDeath);
            SetState(State.Idle);
            overlaps.Clear();
            chargeHits.Clear();
        }

        private void HandleDefeat()
        {
            SetState(State.Defeated);
        }

        private void HandleTargetDeath()
        {
            SetState(State.Idle);
        }

        private void FixedUpdate()
        {
            if (body == null || !body.simulated || bodyCollider == null || !bodyCollider.enabled ||
                health == null || !health.isActiveAndEnabled)
            {
                SetState(State.Idle);
                return;
            }

            if (health.IsDefeated)
            {
                SetState(State.Defeated);
                return;
            }

            if (target == null || !target.isActiveAndEnabled || target.IsDead)
            {
                SetState(State.Idle);
                return;
            }

            // A health reset may revive this object; a scene reload also starts fresh in Idle.
            if (CurrentState == State.Defeated)
                SetState(State.Idle);

            if (CurrentState == State.Idle)
            {
                Vector2 distance = target.transform.position - transform.position;
                if (Mathf.Abs(distance.x) > 0.01f)
                    facingDirection = Mathf.Sign(distance.x);
                if (spriteRenderer != null)
                    spriteRenderer.flipX = IsFacingRight != spriteFacesRight;

                float horizontalDistance = Mathf.Abs(distance.x);
                float swipeRange = Mathf.Max(0.01f, activationRange);
                if (horizontalDistance <= Mathf.Max(swipeRange, chargeActivationRange) &&
                    Mathf.Abs(distance.y) <= Mathf.Max(0f, maxVerticalDistance))
                {
                    hitAttempted = false;
                    bool useCharge = horizontalDistance > swipeRange || preferCharge;
                    preferCharge = !useCharge;
                    SetState(useCharge ? State.ChargeTelegraph : State.Telegraph,
                        useCharge ? chargeTelegraphDuration : telegraphDuration);
                }
                return;
            }

            if (CurrentState == State.Telegraph || CurrentState == State.ChargeTelegraph || CurrentState == State.Recovery)
            {
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining > 0f)
                    return;

                if (CurrentState == State.Recovery)
                {
                    SetState(State.Idle);
                    return;
                }

                bool isCharge = CurrentState == State.ChargeTelegraph;
                SetState(isCharge ? State.Charge : State.Swipe, isCharge ? chargeDuration : activeDuration);
            }

            if (CurrentState == State.Charge)
            {
                UpdateCharge();
                return;
            }

            // Facing stays locked throughout warning, swipe and recovery: no last-second tracking.
            CheckSwipeHit();
            if (CurrentState != State.Swipe)
                return;
            stateTimeRemaining -= Time.fixedDeltaTime;
            if (stateTimeRemaining <= 0f)
                SetState(State.Recovery, recoveryDuration);
        }

        private void UpdateCharge()
        {
            if (finishChargeAfterStep || stateTimeRemaining <= 0f)
            {
                SetState(State.Recovery, chargeRecoveryDuration);
                return;
            }

            Vector2 direction = new(facingDirection, 0f);
            float limit = IsFacingRight ? arenaXLimits.y : arenaXLimits.x;
            float distanceToLimit = Mathf.Max(0f, (limit - body.position.x) * facingDirection);
            float distance = Mathf.Min(Mathf.Max(0.01f, chargeSpeed) *
                Mathf.Min(Time.fixedDeltaTime, stateTimeRemaining), distanceToLimit);

            if (distance <= 0f)
            {
                SetState(State.Recovery, chargeRecoveryDuration);
                return;
            }

            float skin = Mathf.Max(0.001f, collisionSkin);
            ContactFilter2D filter = new() { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            bodyCollider.Cast(direction, filter, chargeHits, distance + skin);

            RaycastHit2D nearest = default;
            float nearestDistance = float.PositiveInfinity;
            Bounds bossBounds = bodyCollider.bounds;
            for (int i = 0; i < chargeHits.Count; i++)
            {
                RaycastHit2D hit = chargeHits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody == body)
                    continue;

                Bounds otherBounds = hit.collider.bounds;
                // The flat floor can be touching at the cast origin; it is not a wall in front.
                if (otherBounds.max.y <= bossBounds.min.y + skin)
                    continue;
                if (IsFacingRight ? otherBounds.max.x < bossBounds.center.x : otherBounds.min.x > bossBounds.center.x)
                    continue;
                if (hit.distance < nearestDistance)
                {
                    nearest = hit;
                    nearestDistance = hit.distance;
                }
            }

            if (nearest.collider != null)
            {
                distance = Mathf.Min(distance, Mathf.Max(0f, nearestDistance - skin));
                finishChargeAfterStep = true;
                if (!hitAttempted && nearest.collider.GetComponentInParent<PlayerHealth>() == target)
                {
                    // One contact attempt, even if a well-timed player dash rejects the damage.
                    hitAttempted = true;
                    target.TakeDamage(Mathf.Max(1, chargeDamage), transform.position);
                    // Death or a damage listener may cancel the encounter immediately.
                    if (!isActiveAndEnabled || CurrentState != State.Charge)
                        return;
                }
            }

            body.MovePosition(body.position + direction * distance);
            stateTimeRemaining -= Time.fixedDeltaTime;
            finishChargeAfterStep |= distance >= distanceToLimit;
            // Recovery starts next physics step, after this final bounded movement has happened.
        }

        private void CheckSwipeHit()
        {
            if (CurrentState != State.Swipe || hitAttempted)
                return;

            // Explicit target matching means no new Player layer or project-wide physics changes.
            ContactFilter2D filter = new() { useTriggers = false };
            Physics2D.OverlapBox(HitboxCenter, HitboxSize, 0f, filter, overlaps);
            for (int i = 0; i < overlaps.Count; i++)
            {
                Collider2D hit = overlaps[i];
                if (hit == null || hit.GetComponentInParent<PlayerHealth>() != target)
                    continue;

                // Consume the swing even if invulnerability blocks it. Extra colliders cannot double-hit.
                hitAttempted = true;
                target.TakeDamage(Mathf.Max(1, damage), transform.position);
                break;
            }
        }

        private void SetState(State nextState, float duration = 0f)
        {
            CurrentState = nextState;
            stateTimeRemaining = Mathf.Max(Time.fixedDeltaTime, duration);
            if (nextState != State.Charge)
            {
                StopMovement();
                finishChargeAfterStep = false;
            }
            if (visualCached && spriteRenderer != null)
            {
                spriteRenderer.color = nextState switch
                {
                    State.Telegraph => telegraphColor,
                    State.Swipe => swipeColor,
                    State.ChargeTelegraph => chargeTelegraphColor,
                    State.Charge => chargeColor,
                    _ => originalColor
                };
            }
        }

        private void StopMovement()
        {
            if (body == null || body.bodyType != RigidbodyType2D.Kinematic)
                return;

            body.linearVelocity = Vector2.zero;
            if (body.simulated)
                body.MovePosition(body.position); // Also cancels movement queued earlier in this physics step.
        }

        private void OnDrawGizmosSelected()
        {
            // Before Play, preview the side nearest the assigned player as well.
            float previewDirection = facingDirection;
            if (!Application.isPlaying && target != null)
                previewDirection = target.transform.position.x < transform.position.x ? -1f : 1f;
            Vector2 center = (Vector2)transform.position +
                new Vector2(Mathf.Abs(hitboxOffset.x) * previewDirection, hitboxOffset.y);
            Gizmos.color = CurrentState == State.Swipe ? swipeColor : telegraphColor;
            Gizmos.DrawWireCube(center, HitboxSize);
            Gizmos.color = chargeTelegraphColor;
            Vector3 left = new(arenaXLimits.x, transform.position.y, transform.position.z);
            Vector3 right = new(arenaXLimits.y, transform.position.y, transform.position.z);
            Gizmos.DrawLine(left, right);
            Gizmos.DrawLine(left + Vector3.up, left + Vector3.down);
            Gizmos.DrawLine(right + Vector3.up, right + Vector3.down);
        }
    }
}
