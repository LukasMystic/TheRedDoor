using System.Collections.Generic;
using TheRedDoor.Player;
using TheRedDoor.World;
using UnityEngine;

namespace TheRedDoor.Boss
{
    // Readable phased attacks on a flat arena floor. Animation stays separate.
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
        [Tooltip("Keep attacking from anywhere inside the arena. Turning this off leaves the corners past charge range safe to stand in.")]
        [SerializeField] private bool pursueAcrossArena = true;

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
        [Tooltip("Give a charge enough time to run the full arena width, so a charge started from one end can actually reach the other. Contact with the player or a wall still ends it early.")]
        [SerializeField] private bool chargeReachesArenaEdge = true;
        [SerializeField, Min(0.01f)] private float chargeRecoveryDuration = 1.4f;
        [SerializeField, Min(1)] private int chargeDamage = 1;

        [Header("Ground Slam")]
        [Tooltip("Assign a GroundShockwave prefab to enable slams. Empty preserves swipe/charge only.")]
        [SerializeField] private GroundShockwave shockwavePrefab;
        [Tooltip("Number of swipe/charge attacks before a slam. Starts counting again after each slam.")]
        [SerializeField, Min(1)] private int attacksBetweenSlams = 2;
        [SerializeField, Min(0.01f)] private float slamTelegraphDuration = 0.9f;
        [SerializeField, Min(0.01f)] private float slamActiveDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float slamRecoveryDuration = 1.6f;

        [Header("Camera Shake")]
        [Tooltip("Camera shaken by this boss. Leave empty to use the Main Camera's CameraFollow2D.")]
        [SerializeField] private CameraFollow2D shakeCamera;
        [Tooltip("Peak slam shake offset in world units, before the camera's own Shake Scale. Set to 0 to disable.")]
        [SerializeField, Min(0f)] private float slamShakeStrength = 0.55f;
        [Tooltip("Seconds the slam shake takes to settle. Keep at or below the slam's active duration plus recovery.")]
        [SerializeField, Min(0f)] private float slamShakeDuration = 0.45f;
        [Tooltip("Peak heavy strike shake offset in world units. A single-target blow, so keep it under the slam.")]
        [SerializeField, Min(0f)] private float heavyShakeStrength = 0.35f;
        [Tooltip("Seconds the heavy strike shake takes to settle.")]
        [SerializeField, Min(0f)] private float heavyShakeDuration = 0.3f;
        [Tooltip("Sustained rumble while the Keeper closes distance during a charge or heavy advance. Keep well below the impact strengths.")]
        [SerializeField, Min(0f)] private float approachShakeStrength = 0.12f;

        [Header("Heavy Strike")]
        [SerializeField] private bool heavyStrikeEnabled = true;
        [Tooltip("Number of other attacks before a heavy strike becomes available, including slams.")]
        [SerializeField, Min(1)] private int attacksBetweenHeavyStrikes = 3;
        [SerializeField, Min(0.01f)] private float heavyActivationRange = 4.5f;
        [SerializeField, Min(0.01f)] private float heavyTelegraphDuration = 1f;
        [Tooltip("A short grounded step toward the player's position when the warning began. No contact damage.")]
        [SerializeField, Min(0f)] private float heavyStepDistance = 2f;
        [SerializeField, Min(0.01f)] private float heavyStepSpeed = 5f;
        [SerializeField, Min(0f)] private float heavyStoppingDistance = 1.2f;
        [Tooltip("Stationary warning after the step, before the hit becomes active.")]
        [SerializeField, Min(0.01f)] private float heavyWindupDuration = 0.35f;
        [SerializeField, Min(0.01f)] private float heavyActiveDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float heavyRecoveryDuration = 2f;
        [SerializeField, Min(1)] private int heavyDamage = 2;
        [Tooltip("World-space forward hitbox offset, mirrored with locked facing.")]
        [SerializeField] private Vector2 heavyHitboxOffset = new(1.1f, 0f);
        [SerializeField] private Vector2 heavyHitboxSize = new(2.2f, 1.8f);

        [Header("Health Phases")]
        [Tooltip("Phase 2 starts at or below this fraction of maximum health.")]
        [SerializeField, Range(0f, 1f)] private float phaseTwoThreshold = 0.7f;
        [Tooltip("Phase 3 starts at or below this fraction of maximum health.")]
        [SerializeField, Range(0f, 1f)] private float phaseThreeThreshold = 0.3f;
        [Tooltip("Runtime display. Phase changes affect the next attack, never one already in progress.")]
        [SerializeField] private Phase currentPhase = Phase.One;
        [SerializeField] private PhaseTuning phaseOne = new(1.1f, 1.15f, 0.9f, 0, 0);
        [SerializeField] private PhaseTuning phaseTwo = new(0.98f, 0.85f, 1.1f, 4, 0);
        [SerializeField] private PhaseTuning phaseThree = new(0.9f, 0.65f, 1.25f, 3, 1);
        [Tooltip("Additional recovery multiplier for an occasional chained attack in Phases 2 and 3.")]
        [SerializeField, Range(0.1f, 1f)] private float chainedRecoveryMultiplier = 0.4f;

        [Header("Flat Arena Limits")]
        [Tooltip("World X limits for the boss ROOT, leaving room for its collider inside the floor edges.")]
        [SerializeField] private Vector2 arenaXLimits = new(-6.5f, 6.5f);
        [Tooltip("Widen the limits at Start to the arena floor the Keeper is standing on, so they cannot fall behind the level as it grows. Never narrows the values set above.")]
        [SerializeField] private bool fitLimitsToFloor = true;
        [Tooltip("Small world-space gap maintained before solid obstacles or the player.")]
        [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;

        [Header("Temporary Visual Feedback")]
        [Tooltip("Whether the unflipped sprite faces right. This never flips the boss collider.")]
        [SerializeField] private bool spriteFacesRight = true;
        [SerializeField] private Color telegraphColor = new(1f, 0.65f, 0.1f, 1f);
        [SerializeField] private Color swipeColor = new(1f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color chargeTelegraphColor = new(0.3f, 0.75f, 1f, 1f);
        [SerializeField] private Color chargeColor = new(0.75f, 0.3f, 1f, 1f);
        [SerializeField] private Color slamTelegraphColor = new(0.7f, 1f, 0.15f, 1f);
        [SerializeField] private Color slamColor = new(0.2f, 1f, 0.4f, 1f);
        [SerializeField] private Color heavyTelegraphColor = new(1f, 0.5f, 0.8f, 1f);
        [SerializeField] private Color heavyStrikeColor = new(0.85f, 0f, 0.45f, 1f);

        public enum State
        {
            Idle, Telegraph, Swipe, Recovery, Defeated, ChargeTelegraph, Charge, SlamTelegraph, Slam,
            HeavyTelegraph, HeavyAdvance, HeavyWindup, HeavyStrike
        }
        public enum Phase { One = 1, Two = 2, Three = 3 }

        [System.Serializable]
        private sealed class PhaseTuning
        {
            [Tooltip("Scales attack warnings. Keep this high enough for attacks to remain readable.")]
            [SerializeField, Min(0.1f)] private float telegraphMultiplier = 1f;
            [SerializeField, Min(0.1f)] private float recoveryMultiplier = 1f;
            [SerializeField, Min(0.1f)] private float movementSpeedMultiplier = 1f;
            [Tooltip("0 disables chains. Otherwise this attack count gets a much shorter recovery.")]
            [SerializeField, Min(0)] private int chainEveryAttacks;
            [Tooltip("Reduces the configured slam and heavy intervals. Phase 3 uses 1.")]
            [SerializeField, Min(0)] private int specialAttackIntervalReduction;

            public float TelegraphMultiplier => Mathf.Max(0.1f, telegraphMultiplier);
            public float RecoveryMultiplier => Mathf.Max(0.1f, recoveryMultiplier);
            public float MovementSpeedMultiplier => Mathf.Max(0.1f, movementSpeedMultiplier);
            public int ChainEveryAttacks => Mathf.Max(0, chainEveryAttacks);
            public int SpecialAttackIntervalReduction => Mathf.Max(0, specialAttackIntervalReduction);

            public PhaseTuning(float telegraph, float recovery, float movement, int chainEvery,
                int specialAttackReduction)
            {
                telegraphMultiplier = telegraph;
                recoveryMultiplier = recovery;
                movementSpeedMultiplier = movement;
                chainEveryAttacks = chainEvery;
                specialAttackIntervalReduction = specialAttackReduction;
            }
        }

        public State CurrentState { get; private set; }
        public Phase CurrentPhase => currentPhase;
        public bool IsFacingRight => facingDirection > 0f;

        private readonly List<Collider2D> overlaps = new(8);
        private readonly List<RaycastHit2D> chargeHits = new(8);
        private readonly List<RaycastHit2D> floorHits = new(4);
        private Vector2 shockwaveXLimits;
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
        private int attacksSinceSlam;
        private GroundShockwave leftShockwave;
        private GroundShockwave rightShockwave;
        private int attacksSinceHeavyStrike;
        private float heavyDestinationX;
        private bool finishHeavyAdvanceAfterStep;
        private int attacksSinceChain;
        private float attackTelegraphMultiplier = 1f;
        private float attackRecoveryMultiplier = 1f;
        private float attackMovementSpeedMultiplier = 1f;
        private bool currentAttackChains;

        private Vector2 HitboxCenter => (Vector2)transform.position +
            new Vector2(Mathf.Abs(hitboxOffset.x) * facingDirection, hitboxOffset.y);
        private Vector2 HitboxSize => new(
            Mathf.Max(0.01f, hitboxSize.x), Mathf.Max(0.01f, hitboxSize.y));
        private Vector2 HeavyHitboxCenter => (Vector2)transform.position +
            new Vector2(Mathf.Abs(heavyHitboxOffset.x) * facingDirection, heavyHitboxOffset.y);
        private Vector2 HeavyHitboxSize => new(
            Mathf.Max(0.01f, heavyHitboxSize.x), Mathf.Max(0.01f, heavyHitboxSize.y));

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

            if (shockwavePrefab != null &&
                (shockwavePrefab.gameObject.scene.IsValid() || !shockwavePrefab.IsConfigured))
            {
                Debug.LogError("Keeper Ground Slam needs an active prefab from the Project window with an enabled GroundShockwave and a centered sprite, without Collider2D or Rigidbody2D components. Slams are disabled until this is corrected.", this);
                shockwavePrefab = null;
            }

            shockwaveXLimits = arenaXLimits;
            originalColor = spriteRenderer.color;
            visualCached = true;
        }

        private void Start()
        {
            if (fitLimitsToFloor)
                FitLimitsToFloor();
        }

        // The hand-set limits were narrower than the arena floor, which left a strip at each end that the
        // Keeper could not walk into and no wave could reach: a safe spot created by the numbers drifting
        // behind the level, not by anything the player did. Measuring the floor at Start keeps reach and
        // level in step. Only widens, so a deliberately tighter fighting area set in the Inspector survives.
        private void FitLimitsToFloor()
        {
            Bounds bounds = bodyCollider.bounds;
            ContactFilter2D filter = new() { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

            // Start just inside the feet so the boss's own collider is not the first thing hit.
            Vector2 origin = new(bounds.center.x, bounds.min.y + 0.05f);
            int count = Physics2D.Raycast(origin, Vector2.down, filter, floorHits, 2f);

            Collider2D floor = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = floorHits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody == body)
                    continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    floor = hit.collider;
                }
            }

            if (floor == null)
            {
                Debug.LogWarning("KeeperController found no floor under the boss, so Arena X Limits stay as configured.", this);
                return;
            }

            Bounds floorBounds = floor.bounds;

            // Waves are clamped by their own half width inside Launch, so they get the raw floor span and
            // stop against whatever solid geometry they meet, such as the arena gate.
            shockwaveXLimits = new Vector2(floorBounds.min.x, floorBounds.max.x);

            // The root keeps its own collider inside the floor. Walls are handled by the charge's obstacle
            // cast rather than by these limits, so the Keeper can close right up against the arena gate.
            float inset = bounds.extents.x;
            arenaXLimits = new Vector2(
                Mathf.Min(arenaXLimits.x, floorBounds.min.x + inset),
                Mathf.Max(arenaXLimits.y, floorBounds.max.x - inset));
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Defeated.AddListener(HandleDefeat);
                health.Damaged.AddListener(HandleHealthChanged);
            }
            if (target != null)
                target.Died.AddListener(HandleTargetDeath);
            preferCharge = false;
            attacksSinceSlam = 0;
            attacksSinceHeavyStrike = 0;
            attacksSinceChain = 0;
            UpdatePhase(true);
            SetState(State.Idle);
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Defeated.RemoveListener(HandleDefeat);
                health.Damaged.RemoveListener(HandleHealthChanged);
            }
            if (target != null)
                target.Died.RemoveListener(HandleTargetDeath);
            SetState(State.Idle);
            ClearShockwave();
            overlaps.Clear();
            chargeHits.Clear();
        }

        private void HandleDefeat()
        {
            SetState(State.Defeated);
            ClearShockwave();
        }

        private void HandleHealthChanged()
        {
            UpdatePhase(false);
        }

        private void HandleTargetDeath()
        {
            SetState(State.Idle);
            ClearShockwave();
        }

        private void FixedUpdate()
        {
            if (health == null || !health.isActiveAndEnabled)
            {
                ClearShockwave();
                SetState(State.Idle);
                return;
            }

            if (health.IsDefeated)
            {
                ClearShockwave();
                SetState(State.Defeated);
                return;
            }

            // Defeat presentation remains active even after the arena gate disables the solid body collider.
            if (body == null || !body.simulated || bodyCollider == null || !bodyCollider.enabled)
            {
                ClearShockwave();
                SetState(State.Idle);
                return;
            }

            if (target == null || !target.isActiveAndEnabled || target.IsDead)
            {
                ClearShockwave();
                SetState(State.Idle);
                return;
            }

            // A health reset may revive this object; a scene reload also starts fresh in Idle.
            if (CurrentState == State.Defeated)
                SetState(State.Idle);

            if (CurrentState == State.Idle)
            {
                // A tuned slower wave must finish before another attack can overlap it.
                if ((leftShockwave != null && leftShockwave.IsTravelling) ||
                    (rightShockwave != null && rightShockwave.IsTravelling))
                    return;

                Vector2 distance = target.transform.position - transform.position;
                if (Mathf.Abs(distance.x) > 0.01f)
                    facingDirection = Mathf.Sign(distance.x);
                if (spriteRenderer != null)
                    spriteRenderer.flipX = IsFacingRight != spriteFacesRight;

                float horizontalDistance = Mathf.Abs(distance.x);
                float swipeRange = Mathf.Max(0.01f, activationRange);

                // Beyond charge range the Keeper used to simply idle, which made the arena corners a safe
                // place to stand and wait. Each charge closes ground toward the player, so engaging across
                // the whole arena width removes that spot using the existing telegraphed attacks: from a far
                // corner he lunges twice rather than reaching in one go, which still reads as a fair warning.
                float engagementRange = Mathf.Max(swipeRange, chargeActivationRange);
                if (pursueAcrossArena)
                    engagementRange = Mathf.Max(engagementRange, arenaXLimits.y - arenaXLimits.x);

                if (horizontalDistance <= engagementRange &&
                    Mathf.Abs(distance.y) <= Mathf.Max(0f, maxVerticalDistance))
                {
                    UpdatePhase(false);
                    hitAttempted = false;
                    int heavyInterval = GetHeavyInterval();
                    if (heavyStrikeEnabled && horizontalDistance <= Mathf.Max(0.01f, heavyActivationRange) &&
                        attacksSinceHeavyStrike >= heavyInterval)
                    {
                        attacksSinceHeavyStrike = 0;
                        float step = Mathf.Min(Mathf.Max(0f, heavyStepDistance),
                            Mathf.Max(0f, horizontalDistance - Mathf.Max(0f, heavyStoppingDistance)));
                        heavyDestinationX = Mathf.Clamp(body.position.x + facingDirection * step,
                            arenaXLimits.x, arenaXLimits.y);
                        BeginAttack(State.HeavyTelegraph, heavyTelegraphDuration);
                        return;
                    }

                    if (attacksSinceHeavyStrike < heavyInterval)
                        attacksSinceHeavyStrike++;
                    int slamInterval = GetSlamInterval();
                    if (shockwavePrefab != null && attacksSinceSlam >= slamInterval)
                    {
                        attacksSinceSlam = 0;
                        BeginAttack(State.SlamTelegraph, slamTelegraphDuration);
                        return;
                    }

                    attacksSinceSlam = Mathf.Min(attacksSinceSlam + 1, slamInterval);
                    bool useCharge = horizontalDistance > swipeRange || preferCharge;
                    preferCharge = !useCharge;
                    BeginAttack(useCharge ? State.ChargeTelegraph : State.Telegraph,
                        useCharge ? chargeTelegraphDuration : telegraphDuration);
                }
                return;
            }

            if (CurrentState == State.Telegraph || CurrentState == State.ChargeTelegraph ||
                CurrentState == State.SlamTelegraph || CurrentState == State.HeavyTelegraph ||
                CurrentState == State.HeavyWindup || CurrentState == State.Recovery)
            {
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining > 0f)
                    return;

                if (CurrentState == State.Recovery)
                {
                    SetState(State.Idle);
                    return;
                }

                if (CurrentState == State.SlamTelegraph)
                {
                    SetState(State.Slam, slamActiveDuration);
                    SpawnShockwave();
                }
                else if (CurrentState == State.HeavyTelegraph)
                {
                    SetState(State.HeavyAdvance);
                }
                else if (CurrentState == State.HeavyWindup)
                {
                    SetState(State.HeavyStrike, heavyActiveDuration);
                    ShakeCameraForHeavyStrike();
                }
                else
                {
                    bool isCharge = CurrentState == State.ChargeTelegraph;
                    SetState(isCharge ? State.Charge : State.Swipe,
                        isCharge ? GetChargeDuration() : activeDuration);
                }
            }

            if (CurrentState == State.Charge)
            {
                UpdateCharge();
                return;
            }

            if (CurrentState == State.HeavyAdvance)
            {
                UpdateHeavyAdvance();
                return;
            }

            if (CurrentState == State.HeavyStrike)
            {
                CheckHeavyHit();
                if (CurrentState != State.HeavyStrike)
                    return; // A lethal damage callback may already have cancelled the attack.
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining <= 0f)
                    BeginRecovery(heavyRecoveryDuration);
                return;
            }

            if (CurrentState == State.Slam)
            {
                stateTimeRemaining -= Time.fixedDeltaTime;
                if (stateTimeRemaining <= 0f)
                    BeginRecovery(slamRecoveryDuration);
                return;
            }

            // Facing stays locked throughout warning, swipe and recovery: no last-second tracking.
            CheckSwipeHit();
            if (CurrentState != State.Swipe)
                return;
            stateTimeRemaining -= Time.fixedDeltaTime;
            if (stateTimeRemaining <= 0f)
                BeginRecovery(recoveryDuration);
        }

        // A fixed charge duration covers a fixed distance, which left the far end of the platform out of
        // reach even once the Keeper engaged from anywhere. Sizing the window to the arena width lets one
        // charge cross the whole floor; the charge still stops the moment it reaches the player or a wall,
        // so nothing changes for the close-range charges that already connected.
        private float GetChargeDuration()
        {
            float duration = Mathf.Max(0.01f, chargeDuration);
            if (!chargeReachesArenaEdge)
                return duration;

            float speed = Mathf.Max(0.01f, chargeSpeed) * Mathf.Max(0.01f, attackMovementSpeedMultiplier);
            float arenaWidth = Mathf.Max(0f, arenaXLimits.y - arenaXLimits.x);

            // Size the window to the gap the Keeper actually has to close, not to the whole arena, so a
            // wider floor does not turn every charge into a marathon. Contact or a wall still ends it early.
            float gap = target != null
                ? Mathf.Abs(target.transform.position.x - body.position.x) + 1f
                : arenaWidth;
            return Mathf.Max(duration, Mathf.Min(gap, arenaWidth) / speed);
        }

        private void UpdateCharge()
        {
            if (finishChargeAfterStep || stateTimeRemaining <= 0f)
            {
                BeginRecovery(chargeRecoveryDuration);
                return;
            }

            Vector2 direction = new(facingDirection, 0f);
            float limit = IsFacingRight ? arenaXLimits.y : arenaXLimits.x;
            float distanceToLimit = Mathf.Max(0f, (limit - body.position.x) * facingDirection);
            float distance = Mathf.Min(Mathf.Max(0.01f, chargeSpeed) * attackMovementSpeedMultiplier *
                Mathf.Min(Time.fixedDeltaTime, stateTimeRemaining), distanceToLimit);

            if (distance <= 0f)
            {
                BeginRecovery(chargeRecoveryDuration);
                return;
            }

            float skin = Mathf.Max(0.001f, collisionSkin);
            RaycastHit2D nearest = FindForwardObstacle(direction, distance, skin);

            if (nearest.collider != null)
            {
                distance = Mathf.Min(distance, Mathf.Max(0f, nearest.distance - skin));
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
            if (distance > 0f)
                RumbleCameraForApproach();
            stateTimeRemaining -= Time.fixedDeltaTime;
            finishChargeAfterStep |= distance >= distanceToLimit;
            // Recovery starts next physics step, after this final bounded movement has happened.
        }

        private void UpdateHeavyAdvance()
        {
            float remaining = Mathf.Max(0f, (heavyDestinationX - body.position.x) * facingDirection);
            if (finishHeavyAdvanceAfterStep || remaining <= 0.001f)
            {
                SetState(State.HeavyWindup, heavyWindupDuration * attackTelegraphMultiplier);
                return;
            }

            Vector2 direction = new(facingDirection, 0f);
            float limit = IsFacingRight ? arenaXLimits.y : arenaXLimits.x;
            float distanceToLimit = Mathf.Max(0f, (limit - body.position.x) * facingDirection);
            float distance = Mathf.Min(remaining, Mathf.Min(distanceToLimit,
                Mathf.Max(0.01f, heavyStepSpeed) * attackMovementSpeedMultiplier * Time.fixedDeltaTime));
            float skin = Mathf.Max(0.001f, collisionSkin);
            RaycastHit2D nearest = FindForwardObstacle(direction, distance, skin);
            if (nearest.collider != null)
            {
                distance = Mathf.Min(distance, Mathf.Max(0f, nearest.distance - skin));
                finishHeavyAdvanceAfterStep = true;
            }

            // The step is harmless, including when it stops against the player's solid body.
            body.MovePosition(body.position + direction * distance);
            if (distance > 0f)
                RumbleCameraForApproach();
            finishHeavyAdvanceAfterStep |= distance >= remaining || distance >= distanceToLimit;
            // Let this move reach physics before starting the stationary warning next step.
        }

        private RaycastHit2D FindForwardObstacle(Vector2 direction, float distance, float skin)
        {
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

            return nearest;
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

        private void CheckHeavyHit()
        {
            if (hitAttempted)
                return;

            ContactFilter2D filter = new() { useTriggers = false };
            Physics2D.OverlapBox(HeavyHitboxCenter, HeavyHitboxSize, 0f, filter, overlaps);
            for (int i = 0; i < overlaps.Count; i++)
            {
                Collider2D hit = overlaps[i];
                if (hit == null || hit.GetComponentInParent<PlayerHealth>() != target)
                    continue;

                // Do not swing through a solid obstacle that stopped the approach.
                float reach = Mathf.Abs(hit.bounds.center.x - bodyCollider.bounds.center.x);
                RaycastHit2D obstacle = FindForwardObstacle(new Vector2(facingDirection, 0f), reach,
                    Mathf.Max(0.001f, collisionSkin));
                if (obstacle.collider != null && obstacle.collider.GetComponentInParent<PlayerHealth>() != target)
                    return;

                hitAttempted = true;
                target.TakeDamage(Mathf.Max(1, heavyDamage), transform.position);
                return;
            }
        }

        private void SpawnShockwave()
        {
            ClearShockwave();
            ShakeCameraForSlam();
            if (shockwavePrefab == null)
                return;

            // Spawn at the feet, independent of the boss sprite's scale or pivot.
            Bounds bounds = bodyCollider.bounds;
            Vector3 position = new(bounds.center.x, bounds.min.y, transform.position.z);
            leftShockwave = Instantiate(shockwavePrefab, position, Quaternion.identity);
            leftShockwave.Launch(this, target, -1f, shockwaveXLimits);
            rightShockwave = Instantiate(shockwavePrefab, position, Quaternion.identity);
            rightShockwave.Launch(this, target, 1f, shockwaveXLimits);
        }

        // Called at the telegraph-to-slam transition, which is the frame the impact reads on screen.
        // Every slam calls this, including repeats, because the camera keeps the strongest live request.
        private void ShakeCameraForSlam()
        {
            if (slamShakeStrength <= 0f || slamShakeDuration <= 0f)
                return;

            CameraFollow2D shakeTarget = ResolveShakeCamera();
            if (shakeTarget != null)
                shakeTarget.Shake(slamShakeDuration, slamShakeStrength);
        }

        // The heavy strike lands as the windup ends, which is also the frame the contact drawing appears.
        private void ShakeCameraForHeavyStrike()
        {
            if (heavyShakeStrength <= 0f || heavyShakeDuration <= 0f)
                return;

            CameraFollow2D shakeTarget = ResolveShakeCamera();
            if (shakeTarget != null)
                shakeTarget.Shake(heavyShakeDuration, heavyShakeStrength);
        }

        // Renewed every movement step. The camera fades the rumble out by itself once the Keeper stops closing in,
        // so no state transition has to remember to clear it.
        private void RumbleCameraForApproach()
        {
            if (approachShakeStrength <= 0f)
                return;

            CameraFollow2D shakeTarget = ResolveShakeCamera();
            if (shakeTarget != null)
                shakeTarget.Rumble(approachShakeStrength);
        }

        private CameraFollow2D ResolveShakeCamera()
        {
            if (shakeCamera != null)
                return shakeCamera;

            // A missing Main Camera is retried rather than cached, so a late or replaced camera still shakes.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                shakeCamera = mainCamera.GetComponent<CameraFollow2D>();
            return shakeCamera;
        }

        private void ClearShockwave()
        {
            if (leftShockwave != null)
                leftShockwave.Cancel();
            if (rightShockwave != null)
                rightShockwave.Cancel();
            leftShockwave = null;
            rightShockwave = null;
        }

        private void UpdatePhase(bool resetChainCount)
        {
            if (health == null || health.MaxHealth <= 0)
                return;

            float phaseTwoStart = Mathf.Clamp01(phaseTwoThreshold);
            float phaseThreeStart = Mathf.Clamp(phaseThreeThreshold, 0f, phaseTwoStart);
            float healthFraction = (float)health.CurrentHealth / health.MaxHealth;
            Phase nextPhase = healthFraction > phaseTwoStart
                ? Phase.One
                : healthFraction > phaseThreeStart ? Phase.Two : Phase.Three;

            if (resetChainCount || nextPhase != currentPhase)
                attacksSinceChain = 0;
            currentPhase = nextPhase;
        }

        private PhaseTuning CurrentPhaseTuning => currentPhase switch
        {
            Phase.Two => phaseTwo,
            Phase.Three => phaseThree,
            _ => phaseOne
        };

        private int GetSlamInterval()
        {
            return Mathf.Max(1, attacksBetweenSlams - CurrentPhaseTuning.SpecialAttackIntervalReduction);
        }

        private int GetHeavyInterval()
        {
            return Mathf.Max(1, attacksBetweenHeavyStrikes - CurrentPhaseTuning.SpecialAttackIntervalReduction);
        }

        private void BeginAttack(State telegraphState, float baseTelegraphDuration)
        {
            PhaseTuning tuning = CurrentPhaseTuning;
            attackTelegraphMultiplier = tuning.TelegraphMultiplier;
            attackRecoveryMultiplier = tuning.RecoveryMultiplier;
            attackMovementSpeedMultiplier = tuning.MovementSpeedMultiplier;

            currentAttackChains = false;
            int chainInterval = tuning.ChainEveryAttacks;
            if (chainInterval <= 0)
            {
                attacksSinceChain = 0;
            }
            else
            {
                attacksSinceChain++;
                if (attacksSinceChain >= chainInterval)
                {
                    attacksSinceChain = 0;
                    currentAttackChains = true;
                }
            }

            SetState(telegraphState, baseTelegraphDuration * attackTelegraphMultiplier);
        }

        private void BeginRecovery(float baseRecoveryDuration)
        {
            float chainMultiplier = currentAttackChains ? Mathf.Clamp(chainedRecoveryMultiplier, 0.1f, 1f) : 1f;
            SetState(State.Recovery, baseRecoveryDuration * attackRecoveryMultiplier * chainMultiplier);
        }

        private void SetState(State nextState, float duration = 0f)
        {
            CurrentState = nextState;
            stateTimeRemaining = Mathf.Max(Time.fixedDeltaTime, duration);
            if (nextState != State.Charge && nextState != State.HeavyAdvance)
                StopMovement();
            if (nextState != State.Charge)
                finishChargeAfterStep = false;
            if (nextState != State.HeavyAdvance)
                finishHeavyAdvanceAfterStep = false;
            if (visualCached && spriteRenderer != null)
            {
                // Attack animations now communicate the windup. Keep debug colors in gizmos only.
                spriteRenderer.color = originalColor;
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
            if (heavyStrikeEnabled)
            {
                Gizmos.color = heavyTelegraphColor;
                Vector2 heavyCenter = (Vector2)transform.position +
                    new Vector2(Mathf.Abs(heavyHitboxOffset.x) * previewDirection, heavyHitboxOffset.y);
                Gizmos.DrawWireCube(heavyCenter, HeavyHitboxSize);
            }
            Gizmos.color = chargeTelegraphColor;
            Vector3 left = new(arenaXLimits.x, transform.position.y, transform.position.z);
            Vector3 right = new(arenaXLimits.y, transform.position.y, transform.position.z);
            Gizmos.DrawLine(left, right);
            Gizmos.DrawLine(left + Vector3.up, left + Vector3.down);
            Gizmos.DrawLine(right + Vector3.up, right + Vector3.down);
        }
    }
}
