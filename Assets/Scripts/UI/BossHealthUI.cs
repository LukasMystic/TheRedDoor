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
        private float targetFill = 1f;
        private float targetAlpha;
        private float hitPulse;
        private Vector3 authoredScale;
        private Color fillColor;

        private void Awake()
        {
            hudGroup = GetComponent<CanvasGroup>();
            authoredScale = transform.localScale;
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
            fillColor = healthFill.color;
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
            transform.localScale = authoredScale;
            hitPulse = 0f;
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
            healthFill.fillAmount = targetFill;
            UpdateVisibility();
        }

        private void HandleHealthChanged(int current, int maximum)
        {
            // A full reset ends the old presentation; re-enabling the HUD alone does not.
            if (current >= maximum)
                encounterSeen = false;
            float nextFill = Mathf.Clamp01((float)current / Mathf.Max(1, maximum));
            if (nextFill < targetFill)
                hitPulse = 1f;
            DrawHealth(current, maximum);
            UpdateVisibility();
        }

        private void DrawHealth(int current, int maximum)
        {
            targetFill = Mathf.Clamp01((float)current / Mathf.Max(1, maximum));
        }

        private void LateUpdate()
        {
            if (initialized)
            {
                UpdateVisibility();
                float dt = Time.unscaledDeltaTime;
                hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, targetAlpha, dt * 2.5f);
                healthFill.fillAmount = Mathf.MoveTowards(healthFill.fillAmount, targetFill, dt * 1.8f);
                hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 3f);
                healthFill.color = Color.Lerp(fillColor, new Color(1f, 0.8f, 0.45f), hitPulse * 0.55f);
                transform.localScale = authoredScale * (1f + Mathf.Sin(hitPulse * Mathf.PI) * 0.025f);
            }
        }

        private void UpdateVisibility()
        {
            if (bossHealth == null || !bossHealth.isActiveAndEnabled || bossHealth.IsDefeated ||
                keeper == null || !keeper.isActiveAndEnabled ||
                playerHealth == null || !playerHealth.isActiveAndEnabled || playerHealth.IsDead)
            {
                encounterSeen = false;
                targetAlpha = 0f;
                return;
            }

            // Stay visible during recovery, jumps and temporary retreats out of attack range.
            bool attacking = keeper.CurrentState != KeeperController.State.Idle &&
                keeper.CurrentState != KeeperController.State.Defeated;
            encounterSeen |= attacking || bossHealth.CurrentHealth < bossHealth.MaxHealth;
            if (hudGroup != null)
            {
                targetAlpha = encounterSeen ? 1f : 0f;
                hudGroup.interactable = false;
                hudGroup.blocksRaycasts = false;
            }
        }

        private void Hide()
        {
            targetAlpha = 0f;
            if (hudGroup == null)
                return;
            hudGroup.alpha = 0f;
            hudGroup.interactable = false;
            hudGroup.blocksRaycasts = false;
        }
    }
}
