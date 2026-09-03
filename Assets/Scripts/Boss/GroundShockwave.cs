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
        [SerializeField, Min(0.01f)] private float maxLifetime = 2f;
        [Tooltip("Full hitbox size in world units. The centered root sprite is resized to match on launch.")]
        [SerializeField] private Vector2 hitboxSize = new(0.8f, 0.45f);
        [Tooltip("Distance above the flat floor. Keep small so grounded players cannot stand below the wave.")]
        [SerializeField, Min(0.001f)] private float groundClearance = 0.02f;

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
            remainingDistance = Mathf.Min(Mathf.Max(0.01f, travelDistance),
                direction.x > 0f ? right - position.x : position.x - left);
            remainingLifetime = Mathf.Max(0.01f, maxLifetime);

            // This prefab uses a centered sprite, not a physical collider that could block the player.
            Vector3 spriteSize = GetComponent<SpriteRenderer>().sprite.bounds.size;
            transform.localScale = new Vector3(size.x / Mathf.Max(0.001f, spriteSize.x),
                size.y / Mathf.Max(0.001f, spriteSize.y), 1f);
            running = true;
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
