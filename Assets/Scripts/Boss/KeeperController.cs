using System.Collections.Generic;
using TheRedDoor.Player;
using UnityEngine;

namespace TheRedDoor.Boss
{
    // First encounter pass: stationary swipe only. Animation and other attacks come later.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossHealth))]
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

        [Header("Temporary Visual Feedback")]
        [Tooltip("Whether the unflipped sprite faces right. This never flips the boss collider.")]
        [SerializeField] private bool spriteFacesRight = true;
        [SerializeField] private Color telegraphColor = new(1f, 0.65f, 0.1f, 1f);
        [SerializeField] private Color swipeColor = new(1f, 0.2f, 0.2f, 1f);

        public enum State { Idle, Telegraph, Swipe, Recovery, Defeated }
        public State CurrentState { get; private set; }
        public bool IsFacingRight => facingDirection > 0f;

        private readonly List<Collider2D> overlaps = new(8);
        private BossHealth health;
        private Color originalColor;
        private float facingDirection = 1f;
        private float stateTimeRemaining;
        private bool hitAttempted;
        private bool visualCached;

        private Vector2 HitboxCenter => (Vector2)transform.position +
            new Vector2(Mathf.Abs(hitboxOffset.x) * facingDirection, hitboxOffset.y);
        private Vector2 HitboxSize => new(
            Mathf.Max(0.01f, hitboxSize.x), Mathf.Max(0.01f, hitboxSize.y));

        private void Awake()
        {
            health = GetComponent<BossHealth>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (target == null || target.gameObject.scene != gameObject.scene || spriteRenderer == null)
            {
                Debug.LogError("KeeperController needs the scene Player as Target and the boss Sprite Renderer.", this);
                enabled = false;
                return;
            }

            originalColor = spriteRenderer.color;
            visualCached = true;
        }

        private void OnEnable()
        {
            SetState(State.Idle);
        }

        private void OnDisable()
        {
            SetState(State.Idle);
            overlaps.Clear();
        }

        private void FixedUpdate()
        {
            if (health == null || !health.isActiveAndEnabled)
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

                if (Mathf.Abs(distance.x) <= Mathf.Max(0.01f, activationRange) &&
                    Mathf.Abs(distance.y) <= Mathf.Max(0f, maxVerticalDistance))
                {
                    hitAttempted = false;
                    SetState(State.Telegraph, telegraphDuration);
                }
                return;
            }

            if (CurrentState == State.Telegraph || CurrentState == State.Recovery)
            {
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining > 0f)
                    return;

                if (CurrentState == State.Recovery)
                {
                    SetState(State.Idle);
                    return;
                }

                SetState(State.Swipe, activeDuration);
            }

            // Facing stays locked throughout warning, swipe and recovery: no last-second tracking.
            CheckSwipeHit();
            stateTimeRemaining -= Time.fixedDeltaTime;
            if (stateTimeRemaining <= 0f)
                SetState(State.Recovery, recoveryDuration);
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
            if (visualCached && spriteRenderer != null)
            {
                spriteRenderer.color = nextState == State.Telegraph ? telegraphColor :
                    nextState == State.Swipe ? swipeColor : originalColor;
            }
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
        }
    }
}
