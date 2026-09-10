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

        [Header("Taken Damage Effect")]
        [Tooltip("The bright health segment that briefly remains after the Keeper takes damage. Created automatically when left empty.")]
        [SerializeField] private Image damageFill;
        [SerializeField] private Color damageColor = Color.white;
        [Tooltip("How long the white chunk sits still before it starts closing. This is the delay.")]
        [SerializeField, Min(0f)] private float damageHoldDuration = 0.09f;
        [Tooltip("Brightness of the bar's inked outline in the white segment. 1 flattens the " +
            "segment to pure white; lower keeps a soft edge so it reads the same height as the red.")]
        [SerializeField, Range(0.4f, 1f)] private float damageEdgeShade = 0.88f;
        [Tooltip("How long the white chunk then takes to slide down onto the red.")]
        [SerializeField, Min(0.01f)] private float damageDrainDuration = 0.18f;

        private CanvasGroup hudGroup;
        private BossHealth subscribedHealth;
        private bool initialized;
        private bool encounterSeen;
        private float targetFill = 1f;
        private float targetAlpha;
        private float hitPulse;
        private float damageHoldRemaining;
        private float damageDrainElapsed;
        private float damageDrainStartFill = 1f;
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
            EnsureDamageFill();
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
            damageHoldRemaining = 0f;
            damageDrainElapsed = 0f;
            if (initialized && healthFill != null)
                healthFill.color = fillColor;
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
            if (damageFill != null)
                damageFill.fillAmount = targetFill;
            damageHoldRemaining = 0f;
            damageDrainElapsed = 0f;
            damageDrainStartFill = targetFill;
            UpdateVisibility();
        }

        private void HandleHealthChanged(int current, int maximum)
        {
            // A full reset ends the old presentation; re-enabling the HUD alone does not.
            if (current >= maximum)
                encounterSeen = false;
            float nextFill = Mathf.Clamp01((float)current / Mathf.Max(1, maximum));
            if (nextFill < targetFill)
            {
                hitPulse = 1f;
                if (damageFill != null)
                    damageFill.fillAmount = Mathf.Max(damageFill.fillAmount, targetFill);
                damageHoldRemaining = damageHoldDuration;
                damageDrainElapsed = 0f;
                damageDrainStartFill = damageFill != null ? damageFill.fillAmount : targetFill;
            }
            DrawHealth(current, maximum);
            // Actual health responds immediately. The separate bright layer preserves the lost
            // amount long enough to read, then catches up in LateUpdate.
            healthFill.fillAmount = targetFill;
            if (damageFill != null && damageFill.fillAmount < targetFill)
                damageFill.fillAmount = targetFill;
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
                healthFill.fillAmount = targetFill;
                if (damageFill != null)
                {
                    damageHoldRemaining = Mathf.Max(0f, damageHoldRemaining - dt);
                    if (damageHoldRemaining <= 0f && damageFill.fillAmount > targetFill)
                    {
                        damageDrainElapsed += dt;
                        float progress = Mathf.Clamp01(damageDrainElapsed / damageDrainDuration);
                        damageFill.fillAmount = Mathf.Lerp(
                            damageDrainStartFill, targetFill, Mathf.SmoothStep(0f, 1f, progress));
                    }

                    float memoryFlash = Mathf.Sin(hitPulse * Mathf.PI) * 0.18f;
                    damageFill.color = Color.Lerp(damageColor, Color.white, memoryFlash);
                }
                hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 3f);
                healthFill.color = Color.Lerp(fillColor, new Color(1f, 0.8f, 0.45f), hitPulse * 0.55f);
                transform.localScale = authoredScale * (1f + Mathf.Sin(hitPulse * Mathf.PI) * 0.025f);
            }
        }

        private void EnsureDamageFill()
        {
            if (damageFill == null)
            {
                var damageObject = new GameObject(
                    "DamageMemoryFill",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                damageObject.layer = healthFill.gameObject.layer;
                RectTransform source = healthFill.rectTransform;
                RectTransform target = damageObject.GetComponent<RectTransform>();
                target.SetParent(source.parent, false);
                target.anchorMin = source.anchorMin;
                target.anchorMax = source.anchorMax;
                target.pivot = source.pivot;
                target.anchoredPosition = source.anchoredPosition;
                target.sizeDelta = source.sizeDelta;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
                target.SetSiblingIndex(source.GetSiblingIndex());
                damageFill = damageObject.GetComponent<Image>();
            }

            // An Image set to Filled with no sprite draws nothing at all: fillAmount changes
            // exactly as expected and not one pixel appears.
            //
            // It cannot simply reuse the authored sprite either, because that sprite is red and
            // Image.color multiplies -- tinting red white returns red. So this builds a white
            // copy of the same sprite: same alpha, so the capsule's rounded caps and inked edge
            // land exactly where the red bar's do, with only the colour replaced.
            damageFill.sprite = WhitenedCopy(healthFill.sprite) ?? SolidSprite();
            damageFill.color = damageColor;
            damageFill.type = Image.Type.Filled;
            damageFill.fillMethod = Image.FillMethod.Horizontal;
            damageFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            damageFill.fillClockwise = true;
            damageFill.preserveAspect = false;
            damageFill.raycastTarget = false;
        }

        private static Sprite whitenedSprite;
        private static Sprite whitenedSource;

        // Repaints the bar sprite white while keeping its alpha untouched, so the white segment has
        // the bar's silhouette rather than a rectangle's.
        //
        // The texture is imported with Read/Write off, so its pixels cannot be read directly. A blit
        // through a RenderTexture reads back any texture regardless of that setting.
        //
        // Brightness comes from the sprite's strongest colour channel, not its luminance: the bar is
        // red, and red is dark in luminance terms, so a luminance mapping would turn the body grey.
        // On this art the outline sits near 100/255 and the body and gloss run 190-255, which the
        // remap below separates cleanly.
        private Sprite WhitenedCopy(Sprite source)
        {
            if (source == null)
                return null;
            if (whitenedSprite != null && whitenedSource == source)
                return whitenedSprite;

            try
            {
                Texture2D sourceTexture = source.texture;
                int width = sourceTexture.width;
                int height = sourceTexture.height;

                RenderTexture staging = RenderTexture.GetTemporary(
                    width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(sourceTexture, staging);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = staging;

                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(staging);

                Color32[] pixels = copy.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    int strongest = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    float tone = Mathf.InverseLerp(90f, 255f, strongest);
                    byte shade = (byte)Mathf.RoundToInt(
                        Mathf.Lerp(damageEdgeShade, 1f, tone) * 255f);
                    pixels[i] = new Color32(shade, shade, shade, pixel.a);
                }

                copy.SetPixels32(pixels);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = sourceTexture.filterMode;
                copy.Apply();

                Rect region = source.textureRect;
                whitenedSprite = Sprite.Create(copy, region, new Vector2(0.5f, 0.5f),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
                whitenedSprite.name = source.name + " (White)";
                whitenedSource = source;
                return whitenedSprite;
            }
            catch (System.Exception error)
            {
                // A packed atlas or an unsupported blit lands here. A plain white bar is worse
                // looking than a matched one but far better than nothing at all.
                Debug.LogWarning("BossHealthUI could not recolour the health sprite, " +
                    "falling back to a plain white segment: " + error.Message, this);
                return null;
            }
        }

        private static Sprite solidSprite;

        private static Sprite SolidSprite()
        {
            if (solidSprite != null)
                return solidSprite;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            solidSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            solidSprite.name = "BossDamageFill";
            return solidSprite;
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
