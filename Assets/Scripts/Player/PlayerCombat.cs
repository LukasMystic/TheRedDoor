using System.Collections.Generic;
using TheRedDoor.Boss;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheRedDoor.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput), typeof(PlayerController))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Hit Detection")]
        [SerializeField] private Transform attackPoint;
        [Tooltip("Radius in world units; unaffected by the Visual child's scale.")]
        [SerializeField, Min(0.01f)] private float attackRange = 0.6f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private LayerMask targetLayers;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float windupDuration = 0.12f;
        [SerializeField, Min(0.01f)] private float activeDuration = 0.12f;
        [Tooltip("Minimum time between attack starts. An active swing must also finish before the next can start.")]
        [SerializeField, Min(0f)] private float attackCooldown = 0.4f;

        [Header("Input")]
        [SerializeField] private string attackActionPath = "Player/Attack";

        [Header("Animation Hooks (Optional)")]
        [Tooltip("Can trigger the real attack animation once an Animator is configured.")]
        [SerializeField] private UnityEvent onAttackStarted = new();
        [SerializeField] private UnityEvent onAttackFinished = new();

        private enum AttackState { Ready, Windup, Active }

        private readonly List<Collider2D> overlaps = new(8);
        private readonly HashSet<BossHealth> hitTargets = new();
        private PlayerController controller;
        private InputAction attackAction;
        private AttackState state;
        private float stateTimeRemaining;
        private float nextAttackTime;

        public bool IsAttacking => state != AttackState.Ready;
        public bool IsHitboxActive => state == AttackState.Active;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            InputActionAsset actions = GetComponent<PlayerInput>().actions;
            attackAction = actions != null ? actions.FindAction(attackActionPath) : null;

            if (attackPoint == null || attackAction == null)
            {
                Debug.LogError("PlayerCombat needs an Attack Point and a matching action in Player Input's Actions asset.", this);
                enabled = false;
                return;
            }

            if (targetLayers.value == 0)
                Debug.LogWarning("PlayerCombat Target Layers is Nothing; attacks will not hit any targets until a layer is selected.", this);
        }

        private void OnEnable()
        {
            attackAction?.Enable();
        }

        private void OnDisable()
        {
            attackAction?.Disable();
            FinishAttack();
        }

        private void Update()
        {
            if (!controller.isActiveAndEnabled || !controller.ControlsEnabled)
            {
                FinishAttack();
                return;
            }

            if (attackAction.WasPressedThisFrame())
                TryAttack();
        }

        public bool TryAttack()
        {
            if (!isActiveAndEnabled || controller == null || !controller.isActiveAndEnabled ||
                !controller.ControlsEnabled || attackPoint == null || Time.timeScale <= 0f ||
                IsAttacking || Time.time < nextAttackTime)
                return false;

            state = AttackState.Windup;
            stateTimeRemaining = Mathf.Max(0f, windupDuration);
            nextAttackTime = Time.time + Mathf.Max(0f, attackCooldown);
            hitTargets.Clear();
            onAttackStarted.Invoke();
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsAttacking)
                return;

            if (!controller.isActiveAndEnabled || !controller.ControlsEnabled || attackPoint == null)
            {
                FinishAttack();
                return;
            }

            if (state == AttackState.Windup)
            {
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining > 0f)
                    return;

                state = AttackState.Active;
                stateTimeRemaining = Mathf.Max(Time.fixedDeltaTime, activeDuration);
            }

            CheckHits();
            stateTimeRemaining -= Time.fixedDeltaTime;
            if (stateTimeRemaining <= 0f)
                FinishAttack();
        }

        private void CheckHits()
        {
            ContactFilter2D filter = new() { useTriggers = true };
            filter.SetLayerMask(targetLayers);
            Physics2D.OverlapCircle(attackPoint.position, Mathf.Max(0.01f, attackRange), filter, overlaps);

            // A target may have several colliders or remain in range for multiple physics steps.
            for (int i = 0; i < overlaps.Count; i++)
            {
                if (!IsHitboxActive || !controller.ControlsEnabled)
                    break;

                Collider2D hit = overlaps[i];
                if (hit == null || hit.transform.IsChildOf(transform))
                    continue;

                BossHealth health = hit.GetComponentInParent<BossHealth>();
                if (health != null && health.isActiveAndEnabled && !health.IsDefeated && hitTargets.Add(health))
                    health.TakeDamage(Mathf.Max(1, damage));
            }
        }

        private void FinishAttack()
        {
            bool wasAttacking = IsAttacking;
            state = AttackState.Ready;
            hitTargets.Clear();
            overlaps.Clear();
            if (wasAttacking)
                onAttackFinished.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (attackPoint == null)
                return;

            Gizmos.color = IsHitboxActive ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(attackPoint.position, Mathf.Max(0.01f, attackRange));
        }
    }
}
