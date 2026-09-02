using UnityEngine;
using UnityEngine.Events;

namespace TheRedDoor.Boss
{
    [DisallowMultipleComponent]
    public sealed class BossHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 20;
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
            maxHealth = Mathf.Max(1, maxHealth);
            currentHealth = maxHealth;
            IsDefeated = false;
            onHealthChanged.Invoke(currentHealth, maxHealth);
        }
    }
}
