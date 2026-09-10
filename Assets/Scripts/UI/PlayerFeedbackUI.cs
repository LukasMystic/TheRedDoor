using TheRedDoor.Boss;
using TheRedDoor.Player;
using TheRedDoor.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheRedDoor.UI
{
    // Two pieces of legibility the fight was missing: a screen hit that tells you the damage was
    // yours, and a visible attack recharge so the swing rhythm is something you can see rather than
    // guess. Built in code and self-spawning, so the scene needs nothing.
    [DisallowMultipleComponent]
    public sealed class PlayerFeedbackUI : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn();

        private static void Spawn()
        {
            if (FindAnyObjectByType<PlayerHealth>() == null)
                return;
            if (FindAnyObjectByType<PlayerFeedbackUI>() != null)
                return;
            new GameObject("PlayerFeedbackUI").AddComponent<PlayerFeedbackUI>();
        }

        private const float FlashPeak = 0.85f;
        private const float FlashFall = 2.4f;      // alpha per second
        private const float WashPeak = 0.22f;

        private PlayerHealth health;
        private PlayerCombat combat;
        private BossHealth keeperHealth;
        private ArenaGate gate;
        private RespawnManager respawn;
        private Canvas canvas;
        private Transform playerTransform;
        private RectTransform barRect;
        private Image vignette, wash, sides, cooldownFill, cooldownTrack;
        private float flash;
        private float barAlpha = 1f;
        private bool subscribed;
        private static Sprite solidSprite;

        private static readonly Color Blood = new(0.69f, 0.10f, 0.07f, 1f);
        private static readonly Color Ember = new(0.88f, 0.64f, 0.23f, 1f);
        private static readonly Color Charging = new(0.35f, 0.42f, 0.36f, 1f);

        private void Awake()
        {
            health = FindAnyObjectByType<PlayerHealth>();
            combat = FindAnyObjectByType<PlayerCombat>();
            keeperHealth = FindAnyObjectByType<BossHealth>();
            gate = FindAnyObjectByType<ArenaGate>();
            respawn = FindAnyObjectByType<RespawnManager>();
            if (combat != null)
                playerTransform = combat.transform;
            else if (health != null)
                playerTransform = health.transform;
            if (health == null)
            {
                Destroy(gameObject);
                return;
            }

            var canvasGo = new GameObject("Feedback Canvas");
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420; // Over the HUD, under the menu pages at 500.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Wash first, vignette over it: a flat tint alone reads as a bug, and edges alone read as
            // scenery. Together they read as being hit.
            wash = MakeFullScreen(canvasGo.transform, "Damage Wash", null);
            wash.color = new Color(Blood.r, Blood.g, Blood.b, 0f);

            var tex = Resources.Load<Texture2D>("UI/Damage_Vignette");
            Sprite sprite = tex != null
                ? Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f)
                : null;
            vignette = MakeFullScreen(canvasGo.transform, "Damage Vignette", sprite);
            vignette.color = new Color(1f, 1f, 1f, 0f);

            // Critical health gets its own shape: red only down the left and right edges, so it
            // never reads as the same event as taking a hit.
            var sideTex = Resources.Load<Texture2D>("UI/Critical_Sides");
            Sprite sideSprite = sideTex != null
                ? Sprite.Create(sideTex, new Rect(0f, 0f, sideTex.width, sideTex.height),
                    new Vector2(0.5f, 0.5f), 100f)
                : null;
            sides = MakeFullScreen(canvasGo.transform, "Critical Sides", sideSprite);
            sides.color = new Color(1f, 1f, 1f, 0f);

            BuildCooldownBar(canvasGo.transform);
        }

        // A Filled Image with no sprite draws nothing, so the bar needs a real one.
        private static Sprite SolidSprite()
        {
            if (solidSprite != null)
                return solidSprite;
            var tex = new Texture2D(4, 4) { name = "Solid (runtime)" };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            solidSprite = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            return solidSprite;
        }

        private static Image MakeFullScreen(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private void BuildCooldownBar(Transform parent)
        {
            if (combat == null)
                return;

            var track = new GameObject("Attack Recharge", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            barRect = (RectTransform)track.transform;
            // Anchored to the canvas origin and moved every frame to the player's screen position,
            // so the swing gauge sits where the player is already looking.
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = Vector2.zero;
            barRect.pivot = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(96f, 8f);
            var rt = barRect;
            cooldownTrack = track.AddComponent<Image>();
            cooldownTrack.color = new Color(0.05f, 0.08f, 0.06f, 0.75f);
            cooldownTrack.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(track.transform, false);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            cooldownFill = fillGo.AddComponent<Image>();
            cooldownFill.sprite = SolidSprite();
            cooldownFill.color = Ember;
            cooldownFill.raycastTarget = false;
            // Horizontal fill is the one bar shape everyone already reads as "recharging".
            cooldownFill.type = Image.Type.Filled;
            cooldownFill.fillMethod = Image.FillMethod.Horizontal;
            cooldownFill.fillOrigin = 0;
            cooldownFill.fillAmount = 1f;
        }

        private void OnEnable()
        {
            if (health == null) return;
            health.Damaged.AddListener(OnDamaged);
            subscribed = true;
        }

        private void OnDisable()
        {
            if (subscribed && health != null)
                health.Damaged.RemoveListener(OnDamaged);
            subscribed = false;
        }

        private void OnDamaged()
        {
            if (health.IsDead)
                return;
            flash = 1f;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            flash = Mathf.MoveTowards(flash, 0f, dt * FlashFall);

            // Judged in hit points, not as a fraction. With 5 max health a fraction threshold made
            // 1 HP compute to a barely visible 0.14 alpha, which is the opposite of a warning. Two
            // hit points is one Keeper blow from death, so that is where it starts.
            int hp = health.CurrentHealth;
            bool critical = !health.IsDead && hp > 0 && hp <= 2;
            float severity = critical ? (hp <= 1 ? 1f : 0.5f) : 0f;
            float pulse = 0.38f + 0.18f * Mathf.Sin(Time.unscaledTime * (hp <= 1 ? 4.4f : 3f));

            float eased = Mathf.SmoothStep(0f, 1f, flash);
            if (vignette != null)
                vignette.color = new Color(1f, 1f, 1f, Mathf.Clamp01(eased * FlashPeak));
            if (sides != null)
                sides.color = new Color(1f, 1f, 1f, Mathf.Clamp01(severity * pulse));
            if (wash != null)
                wash.color = new Color(Blood.r, Blood.g, Blood.b, eased * WashPeak);

            UpdateCooldown();
        }

        private const float BarWorldLift = 1.35f;  // world units above the player origin
        private const float BarScreenLift = 14f;   // plus a little screen padding

        private void FollowPlayer()
        {
            if (barRect == null || playerTransform == null || canvas == null)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 world = playerTransform.position + Vector3.up * BarWorldLift;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f)
                return; // behind the camera; leave it where it was rather than snapping

            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            barRect.anchoredPosition = new Vector2(screen.x, screen.y + BarScreenLift) / scale;
        }

        private void UpdateCooldown()
        {
            if (combat == null || cooldownFill == null)
                return;

            FollowPlayer();

            float remaining = combat.CooldownRemaining;
            float charged = 1f - Mathf.Clamp01(remaining / combat.AttackCooldown);
            cooldownFill.fillAmount = charged;

            // Recedes once charged, so a full bar is not permanent clutter, and comes back the
            // instant a swing spends it. Alpha is tracked separately because writing the base colour
            // each frame would otherwise reset the fade.
            // Only during the encounter. Outside it there is nothing to time a swing against, so
            // the gauge would be decoration competing with the story lines.
            bool inCombat = !health.IsDead && keeperHealth != null && !keeperHealth.IsDefeated &&
                (gate != null ? gate.IsRaised : respawn != null && respawn.HasArenaCheckpoint);
            bool hide = !inCombat;
            bool ready = combat.IsSwingReady;
            float target = hide ? 0f : ready ? 0.22f : 1f;
            barAlpha = Mathf.MoveTowards(barAlpha, target, Time.unscaledDeltaTime * 2.2f);
            Color tint = ready ? Ember : Charging;
            cooldownFill.color = new Color(tint.r, tint.g, tint.b, barAlpha);
            cooldownTrack.color = new Color(0.05f, 0.08f, 0.06f, 0.75f * barAlpha);
        }
    }
}
