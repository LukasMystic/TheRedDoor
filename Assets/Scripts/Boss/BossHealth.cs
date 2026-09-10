using UnityEngine;
using UnityEngine.Events;

namespace TheRedDoor.Boss
{
    [DisallowMultipleComponent]
    public sealed class BossHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 20;
        [Tooltip("Multiplies Max Health at startup. The demo is about resilience, so the Keeper is meant to outlast several attempts. 1 restores the authored value.")]
        [SerializeField, Min(0.1f)] private float difficultyHealthScale = 4f;
        [Tooltip("Runtime value. Set Max Health before Play Mode; do not edit Current Health during play.")]
        [SerializeField] private int currentHealth;

        [Header("Events")]
        [SerializeField] private UnityEvent<int, int> onHealthChanged = new();
        [SerializeField] private UnityEvent onDamaged = new();
        [SerializeField] private UnityEvent onDefeated = new();

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public bool IsDefeated { get; private set; }
        public UnityEvent<int, int> HealthChanged => onHealthChanged;
        public UnityEvent Damaged => onDamaged;
        public UnityEvent Defeated => onDefeated;

        private int baseMaxHealth = -1;

        private void Awake()
        {
            ResetHealth();
        }

        public bool TakeDamage(int amount)
        {
            if (!isActiveAndEnabled || IsDefeated || amount <= 0)
                return false;

            currentHealth = Mathf.Max(0, currentHealth - amount);
            bool defeatedByThisHit = currentHealth == 0;
            IsDefeated = defeatedByThisHit;

            onHealthChanged.Invoke(currentHealth, maxHealth);
            onDamaged.Invoke();
            if (defeatedByThisHit)
                onDefeated.Invoke();

            return true;
        }

        public void ResetHealth()
        {
            // The authored value is captured once, so repeated resets cannot compound the scale.
            if (baseMaxHealth < 0)
                baseMaxHealth = Mathf.Max(1, maxHealth);
            maxHealth = Mathf.Max(1,
                Mathf.RoundToInt(baseMaxHealth * Mathf.Max(0.1f, difficultyHealthScale)));
            currentHealth = maxHealth;
            IsDefeated = false;
            onHealthChanged.Invoke(currentHealth, maxHealth);
        }
    }
}
