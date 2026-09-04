using TheRedDoor.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheRedDoor.UI
{
    // Scene-owned HUD: listens to health without changing any gameplay values.
    [DisallowMultipleComponent]
    public sealed class PlayerHealthUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Image healthImage;
        [Tooltip("Optional exact HP display, for example 5 / 5.")]
        [SerializeField] private TMP_Text healthLabel;

        [Header("Health Artwork")]
        [Tooltip("Assign before Play: empty first, progressively fuller bulbs, full last. At least three sprites.")]
        [SerializeField] private Sprite[] healthStages = new Sprite[8];

        private PlayerHealth subscribedHealth;
        private bool initialized;

        private void Start()
        {
            // Start runs after scene health components initialize their HP in Awake.
            if (playerHealth == null || healthImage == null ||
                playerHealth.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("PlayerHealthUI needs the scene Player Health and a UI Health Image.", this);
                enabled = false;
                return;
            }

            if (healthStages == null || healthStages.Length < 3)
            {
                Debug.LogError("PlayerHealthUI needs at least three Health Stages, ordered empty to full.", this);
                enabled = false;
                return;
            }

            foreach (Sprite stage in healthStages)
            {
                if (stage != null)
                    continue;

                Debug.LogError("Assign every PlayerHealthUI Health Stage before entering Play Mode.", this);
                enabled = false;
                return;
            }

            healthImage.type = Image.Type.Simple;
            healthImage.preserveAspect = true;
            healthImage.raycastTarget = false;
            if (healthLabel != null)
                healthLabel.raycastTarget = false;

            initialized = true;
            ConnectHealth();
        }

        private void OnEnable()
        {
            if (initialized)
                ConnectHealth();
        }

        private void OnDisable()
        {
            if (subscribedHealth != null)
                subscribedHealth.HealthChanged.RemoveListener(Refresh);
            subscribedHealth = null;
        }

        private void ConnectHealth()
        {
            if (playerHealth == null)
                return;

            if (subscribedHealth != null)
                subscribedHealth.HealthChanged.RemoveListener(Refresh);

            subscribedHealth = playerHealth;
            subscribedHealth.HealthChanged.AddListener(Refresh);
            Refresh(subscribedHealth.CurrentHealth, subscribedHealth.MaxHealth);
        }

        private void Refresh(int currentHealth, int maxHealth)
        {
            int maximum = Mathf.Max(1, maxHealth);
            int current = Mathf.Clamp(currentHealth, 0, maximum);
            int lastStage = healthStages.Length - 1;
            int stageIndex;

            if (current == 0)
                stageIndex = 0;
            else if (current == maximum)
                stageIndex = lastStage;
            else
            {
                // Reserve empty for death and full for genuinely full health.
                stageIndex = Mathf.Clamp(
                    Mathf.RoundToInt((float)current / maximum * lastStage), 1, lastStage - 1);
            }

            if (healthImage != null)
                healthImage.sprite = healthStages[stageIndex];
            if (healthLabel != null)
                healthLabel.text = $"{current} / {maximum}";
        }
    }
}
