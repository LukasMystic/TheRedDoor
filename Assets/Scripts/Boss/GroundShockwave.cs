using System.Collections.Generic;
using TheRedDoor.Player;
using UnityEngine;

namespace TheRedDoor.Boss
{
    // A short-lived, swept hitbox on a flat floor. No physical collider, Rigidbody or pooling needed.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class GroundShockwave : MonoBehaviour
    {
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0.01f)] private float speed = 6f;
        [SerializeField, Min(0.01f)] private float travelDistance = 7f;
        [Tooltip("Run the whole way to the arena edge instead of stopping after Travel Distance, so the far end of the platform is never out of reach. Travel Distance still caps the wave when this is off.")]
        [SerializeField] private bool travelToArenaEdge = true;
        [Tooltip("Safety cap only. A wave crossing the arena raises its own lifetime to match, so this can never cut it short mid-platform.")]
        [SerializeField, Min(0.01f)] private float maxLifetime = 2f;
        [Tooltip("Full hitbox size in world units. The centered root sprite is fitted inside it on launch.")]
        [SerializeField] private Vector2 hitboxSize = new(0.8f, 0.45f);
        [Tooltip("Distance above the flat floor. Keep small so grounded players cannot stand below the wave.")]
        [SerializeField, Min(0.001f)] private float groundClearance = 0.02f;
        [Tooltip("Presentation only. Values above 1 make the artwork larger than the damage hitbox, giving players a forgiving warning silhouette.")]
        [SerializeField, Min(0.1f)] private float visualScaleMultiplier = 1.5f;

        // Everything below is presentation only. The damage test uses the fixed hitbox size and the root
        // position, never the Transform's scale or rotation, so none of it can widen or narrow the hitbox.
        [Header("Animation")]
        [Tooltip("Seconds the spawn burst takes to settle into the travelling shape.")]
        [SerializeField, Min(0f)] private float spawnDuration = 0.12f;
        [Tooltip("Scale the artwork starts at, relative to its settled size. Narrow and tall reads as a burst out of the floor.")]
        [SerializeField] private Vector2 spawnScale = new(0.4f, 1.7f);
        [Tooltip("Extra scale gained by the end of the wave's travel, as a fraction. The wave spreads as it rolls out.")]
        [SerializeField] private Vector2 travelScaleGain = new(0.4f, 0.18f);
        [Tooltip("Vertical crest pulse, as a fraction of height. 0 leaves the wave rigid.")]
        [SerializeField, Min(0f)] private float crestPulse = 0.14f;
        [Tooltip("Crest pulses per second.")]
        [SerializeField, Min(0f)] private float crestPulseFrequency = 11f;
        [Tooltip("Degrees the wave leans in its travel direction.")]
        [SerializeField, Range(0f, 45f)] private float leanAngle = 7f;
        [Tooltip("Degrees of lean wobble around that lean, and its rate in cycles per second.")]
        [SerializeField, Range(0f, 45f)] private float wobbleAngle = 4f;
        [SerializeField, Min(0f)] private float wobbleFrequency = 6f;
        [Tooltip("Fraction of the travel over which the wave fades out as it dies down. 0 keeps it opaque until it vanishes.")]
        [SerializeField, Range(0f, 1f)] private float fadeOutFraction = 0.35f;

        private readonly List<Collider2D> overlaps = new(8);
        private readonly List<RaycastHit2D> hits = new(8);
        private KeeperController owner;
        private BossHealth ownerHealth;
        private PlayerHealth target;
        private Vector2 size;
        private Vector2 direction;
        private float remainingDistance;
        private float remainingLifetime;
        private bool running;
        private SpriteRenderer visual;
        private Vector3 settledScale;
        private Color settledColor;
        private float totalDistance;
        private float age;

        public bool IsTravelling => running && isActiveAndEnabled;
        internal bool IsConfigured => enabled && gameObject.activeSelf &&
            GetComponent<SpriteRenderer>() != null && GetComponent<SpriteRenderer>().sprite != null &&
            GetComponentInChildren<Collider2D>(true) == null && GetComponentInChildren<Rigidbody2D>(true) == null;

        // Instantiate at the boss's floor position first; this adds half the wave's height.
        internal void Launch(KeeperController source, PlayerHealth player, float facing, Vector2 arenaLimits)
        {
            if (!Application.isPlaying || !IsConfigured || source == null || player == null)
            {
                Cancel();
                return;
            }

            owner = source;
            ownerHealth = source.GetComponent<BossHealth>();
            target = player;
            size = new Vector2(Mathf.Max(0.05f, hitboxSize.x), Mathf.Max(0.05f, hitboxSize.y));
            direction = new Vector2(facing < 0f ? -1f : 1f, 0f);
            float left = arenaLimits.x + size.x * 0.5f;
            float right = arenaLimits.y - size.x * 0.5f;
            if (left >= right)
            {
                Cancel();
                return;
            }

            Vector3 position = transform.position;
            position.x = Mathf.Clamp(position.x, left, right);
            position.y += size.y * 0.5f + Mathf.Max(0.001f, groundClearance);
            transform.position = position;
            // The arena edge is the hard stop either way; Travel Distance only shortens the wave further.
            float distanceToEdge = Mathf.Max(0f, direction.x > 0f ? right - position.x : position.x - left);
            remainingDistance = travelToArenaEdge
                ? Mathf.Max(0.01f, distanceToEdge)
                : Mathf.Min(Mathf.Max(0.01f, travelDistance), distanceToEdge);

            // Lifetime has to cover the distance, or a longer wave would expire part way across the floor
            // and leave the far end safe again. It is only ever raised, never shortened.
            float timeToCross = remainingDistance / Mathf.Max(0.01f, speed);
            remainingLifetime = Mathf.Max(Mathf.Max(0.01f, maxLifetime), timeToCross + 0.25f);

            // This prefab uses a centered sprite, not a physical collider that could block the player.
            // A uniform scale preserves authored proportions while keeping the art inside the hitbox.
            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            Vector3 spriteSize = spriteRenderer.sprite.bounds.size;
            float visualScale = Mathf.Min(size.x / Mathf.Max(0.001f, spriteSize.x),
                size.y / Mathf.Max(0.001f, spriteSize.y));
            visualScale *= Mathf.Max(0.1f, visualScaleMultiplier);
            transform.localScale = new Vector3(visualScale, visualScale, 1f);
            spriteRenderer.flipX = direction.x < 0f;

            visual = spriteRenderer;
            settledScale = transform.localScale;
            settledColor = spriteRenderer.color;
            totalDistance = remainingDistance;
            age = 0f;
            running = true;
            ApplyAnimation(); // Show the spawn burst on the first frame rather than the settled shape.
        }

        private void Update()
        {
            if (!running)
                return;

            age += Time.deltaTime;
            ApplyAnimation();
        }

        // Scale, rotation and colour only. The hitbox is an axis-aligned box of the configured size at the
        // root position, so a bigger or leaning drawing never reaches further than the damage it warns about.
        private void ApplyAnimation()
        {
            if (visual == null)
                return;

            float travelled = totalDistance > 0f
                ? Mathf.Clamp01(1f - remainingDistance / totalDistance)
                : 1f;
            float spawn = spawnDuration > 0f ? Mathf.Clamp01(age / spawnDuration) : 1f;
            float burst = 1f - (1f - spawn) * (1f - spawn); // Ease out, so the burst settles rather than snaps.

            Vector2 spawnMultiplier = Vector2.Lerp(spawnScale, Vector2.one, burst);
            float pulse = 1f + crestPulse * Mathf.Sin(age * crestPulseFrequency * Mathf.PI * 2f);

            Vector3 scale = settledScale;
            scale.x *= spawnMultiplier.x * (1f + travelScaleGain.x * travelled);
            scale.y *= spawnMultiplier.y * (1f + travelScaleGain.y * travelled) * Mathf.Max(0.05f, pulse);
            transform.localScale = scale;

            float wobble = wobbleAngle * Mathf.Sin(age * wobbleFrequency * Mathf.PI * 2f);
            transform.rotation = Quaternion.Euler(0f, 0f, -direction.x * (leanAngle + wobble));

            float fade = fadeOutFraction > 0f
                ? Mathf.Clamp01((1f - travelled) / fadeOutFraction)
                : 1f;
            Color color = settledColor;
            color.a = settledColor.a * Mathf.Min(burst, fade);
            visual.color = color;
        }

        private void FixedUpdate()
        {
            if (!running)
                return;
            if (owner == null || !owner.isActiveAndEnabled || ownerHealth == null ||
                !ownerHealth.isActiveAndEnabled || ownerHealth.IsDefeated ||
                target == null || !target.isActiveAndEnabled || target.IsDead ||
                remainingDistance <= 0f || remainingLifetime <= 0f)
            {
                Cancel();
                return;
            }

            Vector2 origin = transform.position;
            ContactFilter2D filter = new() { useTriggers = false };

            // Explicit initial overlap also covers point-blank hits regardless of cast-start settings.
            Physics2D.OverlapBox(origin, size, 0f, filter, overlaps);
            bool touchingPlayer = false;
            for (int i = 0; i < overlaps.Count; i++)
            {
                Collider2D collider = overlaps[i];
                if (IgnoreCollider(collider))
                    continue;
                if (!IsPlayerCollider(collider))
                {
                    Cancel(); // A solid wall overlapping the origin shields anything behind it.
                    return;
                }
                touchingPlayer = true;
            }
            if (touchingPlayer)
            {
                HitPlayer();
                return;
            }

            float distance = Mathf.Min(remainingDistance,
                Mathf.Max(0.01f, speed) * Mathf.Min(Time.fixedDeltaTime, remainingLifetime));
            Physics2D.BoxCast(origin, size, 0f, direction, filter, hits, distance);
            Collider2D nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                RaycastHit2D hit = hits[i];
                if (IgnoreCollider(hit.collider))
                    continue;
                // On equal-distance hits, let a wall shield the player.
                if (hit.distance < nearestDistance ||
                    (hit.distance == nearestDistance && !IsPlayerCollider(hit.collider)))
                {
                    nearest = hit.collider;
                    nearestDistance = hit.distance;
                }
            }
            if (nearest != null)
            {
                if (IsPlayerCollider(nearest))
                    HitPlayer();
                else
                    Cancel();
                return;
            }

            transform.position += (Vector3)(direction * distance);
            remainingDistance -= distance;
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingDistance <= 0f || remainingLifetime <= 0f)
                Cancel();
        }

        private bool IgnoreCollider(Collider2D collider)
        {
            return collider == null || collider.transform.IsChildOf(owner.transform) ||
                collider.bounds.max.y <= transform.position.y - size.y * 0.5f;
        }

        private bool IsPlayerCollider(Collider2D collider)
        {
            // Damage the explicitly assigned health, even if the collider is on a child object.
            return collider.transform.IsChildOf(target.transform);
        }

        private void HitPlayer()
        {
            Vector2 sourcePosition = transform.position;
            Cancel(); // Consume before callbacks, even when dash/post-hit protection rejects damage.
            target.TakeDamage(Mathf.Max(1, damage), sourcePosition);
        }

        internal void Cancel()
        {
            running = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            running = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 1f);
            Vector2 previewSize = Application.isPlaying && running ? size :
                new Vector2(Mathf.Max(0.05f, hitboxSize.x), Mathf.Max(0.05f, hitboxSize.y));
            Gizmos.DrawWireCube(transform.position, previewSize);
        }
    }
}
