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

        [Header("Liquid")]
        [Tooltip("How hard the contents slosh when health changes. 0 keeps the old still potion.")]
        [SerializeField, Range(0f, 1f)] private float sloshStrength = 0.5f;
        [Tooltip("Slosh oscillations per second, and how fast they settle.")]
        [SerializeField, Min(0.1f)] private float sloshFrequency = 4.6f;
        [SerializeField, Min(0.1f)] private float sloshDamping = 3.2f;
        [Tooltip("Constant idle drift, so the liquid is never completely still.")]
        [SerializeField, Range(0f, 1f)] private float idleSway = 0.35f;

        private PlayerHealth subscribedHealth;
        private bool initialized;
        private int previousHealth = -1;
        private int maximumHealth = 1;
        private float hitPulse;
        private Vector3 imageScale;
        private Color imageColor;
        private Quaternion imageRotation;
        private Vector2 imagePosition;
        private float slosh;          // displacement
        private float sloshVelocity;  // and its rate: a plain damped spring
        private Material liquidMaterial, originalMaterial;

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

            imageScale = healthImage.rectTransform.localScale;
            imageColor = healthImage.color;
            imageRotation = healthImage.rectTransform.localRotation;
            imagePosition = healthImage.rectTransform.anchoredPosition;
            originalMaterial = healthImage.material;
            Shader liquidShader = Resources.Load<Shader>("HealthPotion");
            if (liquidShader != null)
            {
                liquidMaterial = new Material(liquidShader) { name = "Health liquid (runtime)" };
                healthImage.material = liquidMaterial;
            }
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
            if (initialized && healthImage != null)
            {
                healthImage.rectTransform.localScale = imageScale;
                healthImage.rectTransform.localRotation = imageRotation;
                healthImage.rectTransform.anchoredPosition = imagePosition;
                healthImage.color = imageColor;
                slosh = 0f;
                sloshVelocity = 0f;
            }
            previousHealth = -1;
            hitPulse = 0f;
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
            if (previousHealth >= 0 && current != previousHealth)
            {
                hitPulse = 1f;
                // Losing health kicks the liquid harder than gaining it, and downward.
                sloshVelocity += current < previousHealth ? -9f : 5f;
            }
            previousHealth = current;
            maximumHealth = maximum;
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
            {
                healthImage.sprite = healthStages[stageIndex];
                if (liquidMaterial != null)
                {
                    Sprite sprite = healthImage.sprite;
                    Rect uv = sprite.textureRect;
                    liquidMaterial.SetVector("_UVRect", new Vector4(uv.x / sprite.texture.width,
                        uv.y / sprite.texture.height, uv.width / sprite.texture.width, uv.height / sprite.texture.height));
                }
            }
            if (healthLabel != null)
                healthLabel.text = $"{current} / {maximum}";
        }

        private void Update()
        {
            if (!initialized || healthImage == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 2.5f);
            float lowHealthPulse = previousHealth > 0 && previousHealth <= maximumHealth * 0.25f
                ? (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * 0.025f : 0f;

            // Damped spring: a hit displaces the contents, and they rock back and settle rather
            // than snapping. This is what sells a potion as a liquid without a shader or new art.
            float omega = Mathf.Max(0.1f, sloshFrequency) * 2f * Mathf.PI;
            // Small substeps keep the spring stable even at 20 FPS or during a hitch.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * Mathf.Max(120f, omega * 4f)));
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                sloshVelocity += (-omega * omega * slosh - 2f * Mathf.Max(0.1f, sloshDamping) * sloshVelocity) * step;
                slosh += sloshVelocity * step;
            }
            slosh = Mathf.Clamp(slosh, -1.5f, 1.5f);

            // Idle drift on two slightly detuned sines, so it never reads as a loop.
            float drift = idleSway * (Mathf.Sin(Time.unscaledTime * 1.13f) * 0.6f +
                Mathf.Sin(Time.unscaledTime * 0.67f + 2.1f) * 0.4f);
            float tilt = (slosh * 6f + drift * 1.4f) * sloshStrength;
            float bob = (slosh * 5f + drift * 2.2f) * sloshStrength;

            // Volume-preserving squash: wider as it flattens, so it reads as fluid, not rubber.
            float squash = 1f + (slosh * 0.10f + drift * 0.02f) * sloshStrength;
            float pulse = 1f + Mathf.Sin(hitPulse * Mathf.PI) * 0.12f + lowHealthPulse;
            var rect = healthImage.rectTransform;
            if (liquidMaterial != null)
            {
                liquidMaterial.SetFloat("_LiquidTime", Time.unscaledTime * idleSway);
                liquidMaterial.SetFloat("_Slosh", slosh);
                liquidMaterial.SetFloat("_Strength", sloshStrength);
                rect.localScale = imageScale * pulse;
                rect.localRotation = imageRotation;
                rect.anchoredPosition = imagePosition;
            }
            else // Graceful fallback if the optional shader cannot be loaded.
            {
                rect.localScale = new Vector3(imageScale.x * pulse / squash,
                    imageScale.y * pulse * squash, imageScale.z);
                rect.localRotation = imageRotation * Quaternion.Euler(0f, 0f, tilt);
                rect.anchoredPosition = imagePosition + new Vector2(0f, bob);
            }
            healthImage.color = Color.Lerp(imageColor, new Color(1f, 0.55f, 0.42f, imageColor.a), hitPulse * 0.6f);
        }

        private void OnDestroy()
        {
            if (healthImage != null) healthImage.material = originalMaterial;
            if (liquidMaterial != null) Destroy(liquidMaterial);
        }
    }
}
