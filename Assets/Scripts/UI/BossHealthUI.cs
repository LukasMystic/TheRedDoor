using TheRedDoor.Boss;
using TheRedDoor.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheRedDoor.UI
{
    // Presentation only: observing an encounter never activates or changes boss AI.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class BossHealthUI : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private BossHealth bossHealth;
        [SerializeField] private KeeperController keeper;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("UI Children")]
        [Tooltip("Separate red fill Image beneath this HUD root, not the decorative frame.")]
        [SerializeField] private Image healthFill;
        [SerializeField] private TMP_Text bossNameLabel;
        [SerializeField] private string bossName = "THE KEEPER";

        private CanvasGroup hudGroup;
        private BossHealth subscribedHealth;
        private bool initialized;
        private bool encounterSeen;

        private void Awake()
        {
            hudGroup = GetComponent<CanvasGroup>();
            Hide();
        }

        private void Start()
        {
            if (bossHealth == null || keeper == null || playerHealth == null ||
                bossHealth.gameObject != keeper.gameObject ||
                bossHealth.gameObject.scene != gameObject.scene ||
                playerHealth.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("BossHealthUI needs the same scene boss's Boss Health and Keeper Controller, plus the scene Player Health.", this);
                enabled = false;
                return;
            }

            if (hudGroup == null || healthFill == null || bossNameLabel == null ||
                healthFill.transform == transform || bossNameLabel.transform == transform ||
                !healthFill.transform.IsChildOf(transform) || !bossNameLabel.transform.IsChildOf(transform))
            {
                Debug.LogError("BossHealthUI needs a Canvas Group on its root and separate Fill Image and Name Text children.", this);
                enabled = false;
                return;
            }

            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            healthFill.preserveAspect = false;
            healthFill.raycastTarget = false;
            bossNameLabel.text = bossName;
            bossNameLabel.raycastTarget = false;
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
                subscribedHealth.HealthChanged.RemoveListener(HandleHealthChanged);
            subscribedHealth = null;
            Hide();
        }

        private void ConnectHealth()
        {
            if (subscribedHealth != null)
                subscribedHealth.HealthChanged.RemoveListener(HandleHealthChanged);
            subscribedHealth = bossHealth;
            if (subscribedHealth == null)
            {
                Hide();
                return;
            }

            subscribedHealth.HealthChanged.AddListener(HandleHealthChanged);
            DrawHealth(subscribedHealth.CurrentHealth, subscribedHealth.MaxHealth);
            UpdateVisibility();
        }

        private void HandleHealthChanged(int current, int maximum)
        {
            // A full reset ends the old presentation; re-enabling the HUD alone does not.
            if (current >= maximum)
                encounterSeen = false;
            DrawHealth(current, maximum);
            UpdateVisibility();
        }

        private void DrawHealth(int current, int maximum)
        {
            if (healthFill != null)
                healthFill.fillAmount = Mathf.Clamp01((float)current / Mathf.Max(1, maximum));
        }

        private void LateUpdate()
        {
            if (initialized)
                UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (bossHealth == null || !bossHealth.isActiveAndEnabled || bossHealth.IsDefeated ||
                keeper == null || !keeper.isActiveAndEnabled ||
                playerHealth == null || !playerHealth.isActiveAndEnabled || playerHealth.IsDead)
            {
                encounterSeen = false;
                Hide();
                return;
            }

            // Stay visible during recovery, jumps and temporary retreats out of attack range.
            bool attacking = keeper.CurrentState != KeeperController.State.Idle &&
                keeper.CurrentState != KeeperController.State.Defeated;
            encounterSeen |= attacking || bossHealth.CurrentHealth < bossHealth.MaxHealth;
            if (hudGroup != null)
            {
                hudGroup.alpha = encounterSeen ? 1f : 0f;
                hudGroup.interactable = false;
                hudGroup.blocksRaycasts = false;
            }
        }

        private void Hide()
        {
            if (hudGroup == null)
                return;
            hudGroup.alpha = 0f;
            hudGroup.interactable = false;
            hudGroup.blocksRaycasts = false;
        }
    }
}
