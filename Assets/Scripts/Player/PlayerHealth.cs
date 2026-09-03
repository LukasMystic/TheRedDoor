using UnityEngine;
using UnityEngine.Events;

namespace TheRedDoor.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField, Min(1)] private int maxHealth = 5;
        [Tooltip("Runtime value. Set Max Health before Play Mode; do not edit Current Health during play.")]
        [SerializeField] private int currentHealth;
        [SerializeField, Min(0f)] private float invulnerabilityDuration = 0.8f;

        [Header("Hit Reaction")]
        [Tooltip("Horizontal speed away from the damage source, and upward speed.")]
        [SerializeField] private Vector2 knockbackVelocity = new(6f, 4f);
        [SerializeField, Min(0.01f)] private float knockbackDuration = 0.18f;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField, Min(0f)] private float flashDuration = 0.12f;

        [Header("Events (Optional)")]
        [SerializeField] private UnityEvent<int, int> onHealthChanged = new();
        [SerializeField] private UnityEvent onDamaged = new();
        [SerializeField] private UnityEvent onDied = new();

        private PlayerController controller;
        private float invulnerableUntil;
        private float flashUntil;
        private Color originalColor;
        private bool isFlashing;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0;
        public bool IsInvulnerable => Time.time < invulnerableUntil;
        public UnityEvent<int, int> HealthChanged => onHealthChanged;
        public UnityEvent Damaged => onDamaged;
        public UnityEvent Died => onDied;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            maxHealth = Mathf.Max(1, maxHealth);
            currentHealth = maxHealth;

            if (spriteRenderer == null)
                Debug.LogWarning("PlayerHealth has no Sprite Renderer; damage will work without a hit flash.", this);
        }

        private void Start()
        {
            onHealthChanged.Invoke(currentHealth, maxHealth);
        }

        private void Update()
        {
            if (isFlashing && Time.time >= flashUntil)
                RestoreSpriteColor();
        }

        private void OnDisable()
        {
            RestoreSpriteColor();
        }

        // Call this from a boss attack with the hit's world-space origin.
        public bool TakeDamage(int amount, Vector2 damageSource)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || Time.timeScale <= 0f ||
                IsDead || IsInvulnerable || amount <= 0)
                return false;

            currentHealth = Mathf.Max(0, currentHealth - amount);
            invulnerableUntil = Time.time + Mathf.Max(0f, invulnerabilityDuration);

            float offset = transform.position.x - damageSource.x;
            float direction = Mathf.Abs(offset) > 0.01f
                ? Mathf.Sign(offset)
                : (controller.IsFacingRight ? -1f : 1f);
            controller.ApplyKnockback(
                new Vector2(Mathf.Abs(knockbackVelocity.x) * direction, Mathf.Max(0f, knockbackVelocity.y)),
                knockbackDuration);

            // A timed knockback must never turn controls back on after death.
            bool diedFromThisHit = IsDead;
            if (diedFromThisHit)
                controller.SetControlsEnabled(false);

            FlashSprite();
            onHealthChanged.Invoke(currentHealth, maxHealth);
            onDamaged.Invoke();
            if (diedFromThisHit)
                onDied.Invoke();

            return true;
        }

        private void FlashSprite()
        {
            if (spriteRenderer == null || flashDuration <= 0f)
                return;

            if (!isFlashing)
                originalColor = spriteRenderer.color;

            spriteRenderer.color = flashColor;
            flashUntil = Time.time + flashDuration;
            isFlashing = true;
        }

        private void RestoreSpriteColor()
        {
            if (isFlashing && spriteRenderer != null)
                spriteRenderer.color = originalColor;
            isFlashing = false;
        }

#if UNITY_EDITOR
        [ContextMenu("Test/Take 1 Damage (Play Mode)")]
        private void TestDamage()
        {
            if (Application.isPlaying)
                TakeDamage(1, (Vector2)transform.position + Vector2.right);
        }
#endif
    }
}
