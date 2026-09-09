using UnityEngine;

namespace TheRedDoor.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [Header("Scene Reference")]
        [Tooltip("The Player root Transform in this scene.")]
        [SerializeField] private Transform target;

        [Header("Horizontal Follow")]
        [SerializeField] private float horizontalOffset;
        [Tooltip("Seconds used to ease toward the target. Set to 0 for immediate movement.")]
        [SerializeField, Min(0f)] private float smoothTime = 0.15f;
        [Tooltip("Place the camera on the Player before the first frame instead of travelling from its saved scene position.")]
        [SerializeField] private bool snapOnStart = true;

        [Header("Level Bounds")]
        [Tooltip("Enable after the level edges have been blocked out.")]
        [SerializeField] private bool useHorizontalBounds;
        [Tooltip("Minimum and maximum camera-center X positions, not world-edge positions.")]
        [SerializeField] private Vector2 horizontalBounds = new(-10f, 10f);

        [Header("Shake")]
        [Tooltip("Multiplies every impact and rumble. Set to 0 to disable shaking without touching the callers.")]
        [SerializeField, Min(0f)] private float shakeScale = 1f;
        [Tooltip("Per-axis weighting in world units. Lower X keeps the horizontal follow readable.")]
        [SerializeField] private Vector2 shakeAxisWeights = new(0.7f, 1f);
        [Tooltip("Largest combined offset the camera may reach, in world units. Guards against a mistuned caller.")]
        [SerializeField, Min(0f)] private float maxShakeOffset = 1.2f;

        [Header("Impact Shake")]
        [Tooltip("Noise samples per second for one-off impacts. Higher feels sharper, lower feels heavier.")]
        [SerializeField, Min(0.01f)] private float impactFrequency = 30f;

        [Header("Rumble")]
        [Tooltip("Noise samples per second for the sustained rumble. Keep below the impact frequency so the two read differently.")]
        [SerializeField, Min(0.01f)] private float rumbleFrequency = 13f;
        [Tooltip("Seconds the rumble takes to fade in and out, so a moving source does not pop on and off.")]
        [SerializeField, Min(0.01f)] private float rumbleFadeDuration = 0.14f;
        [Tooltip("Seconds a rumble request survives without being renewed. Callers refresh it each step while moving.")]
        [SerializeField, Min(0.01f)] private float rumbleHoldDuration = 0.12f;

        private float horizontalVelocity;
        private bool shouldSnap;
        private Vector2 shakeOffset;
        private Vector2 impactSeed;
        private Vector2 rumbleSeed;
        private float impactStrength;
        private float impactDuration;
        private float impactTimeRemaining;
        private float rumbleStrength;
        private float rumbleHoldRemaining;
        private float rumbleLevel;

        private void Awake()
        {
            if (target == null || target.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("CameraFollow2D needs the scene Player root as its Target.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            shouldSnap = snapOnStart;
        }

        // One-off impact. Repeat calls keep the strongest and longest request instead of restarting a weaker one,
        // so a slam landing during an earlier slam's tail never weakens the shake.
        public void Shake(float duration, float strength)
        {
            if (!isActiveAndEnabled || shakeScale <= 0f || duration <= 0f || strength <= 0f)
                return;

            if (impactTimeRemaining <= 0f)
                impactSeed = new Vector2(Random.value * 100f, Random.value * 100f);

            impactStrength = Mathf.Max(impactStrength, strength * shakeScale);
            impactDuration = Mathf.Max(impactDuration, duration);
            impactTimeRemaining = Mathf.Max(impactTimeRemaining, duration);
        }

        // Sustained rumble for a moving source. Call this every step while the source moves; it fades out on its
        // own once the caller stops renewing it, so no explicit stop call is needed when a state changes.
        public void Rumble(float strength)
        {
            if (!isActiveAndEnabled || shakeScale <= 0f || strength <= 0f)
                return;

            if (rumbleHoldRemaining <= 0f && rumbleLevel <= 0f)
                rumbleSeed = new Vector2(Random.value * 100f, Random.value * 100f);

            rumbleStrength = strength * shakeScale;
            rumbleHoldRemaining = rumbleHoldDuration;
        }

        public void StopShake()
        {
            shakeOffset = Vector2.zero;
            impactStrength = 0f;
            impactDuration = 0f;
            impactTimeRemaining = 0f;
            rumbleStrength = 0f;
            rumbleHoldRemaining = 0f;
            rumbleLevel = 0f;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            // Work from the unshaken position so the shake never feeds back into the follow easing.
            Vector3 position = transform.position;
            float baseX = position.x - shakeOffset.x;
            float baseY = position.y - shakeOffset.y;

            float desiredX = target.position.x + horizontalOffset;
            float minimumX = Mathf.Min(horizontalBounds.x, horizontalBounds.y);
            float maximumX = Mathf.Max(horizontalBounds.x, horizontalBounds.y);

            if (useHorizontalBounds)
                desiredX = Mathf.Clamp(desiredX, minimumX, maximumX);

            float nextX;
            if (shouldSnap || smoothTime <= 0f)
            {
                nextX = desiredX;
                horizontalVelocity = 0f;
                shouldSnap = false;
            }
            else
            {
                nextX = Mathf.SmoothDamp(baseX, desiredX, ref horizontalVelocity, smoothTime);
            }

            if (useHorizontalBounds)
                nextX = Mathf.Clamp(nextX, minimumX, maximumX);

            AdvanceShake(Time.deltaTime);

            position.x = nextX + shakeOffset.x;
            position.y = baseY + shakeOffset.y;
            transform.position = position;
        }

        private void AdvanceShake(float deltaTime)
        {
            float impactAmplitude = AdvanceImpact(deltaTime);
            float rumbleAmplitude = AdvanceRumble(deltaTime);

            if (impactAmplitude <= 0f && rumbleAmplitude <= 0f)
            {
                shakeOffset = Vector2.zero;
                return;
            }

            Vector2 offset = Vector2.zero;
            if (impactAmplitude > 0f)
                offset += SampleNoise(impactSeed, impactFrequency) * impactAmplitude;
            if (rumbleAmplitude > 0f)
                offset += SampleNoise(rumbleSeed, rumbleFrequency) * rumbleAmplitude;

            offset.x *= shakeAxisWeights.x;
            offset.y *= shakeAxisWeights.y;
            shakeOffset = Vector2.ClampMagnitude(offset, Mathf.Max(0f, maxShakeOffset));
        }

        private float AdvanceImpact(float deltaTime)
        {
            if (impactTimeRemaining <= 0f)
                return 0f;

            impactTimeRemaining = Mathf.Max(0f, impactTimeRemaining - deltaTime);
            if (impactTimeRemaining <= 0f)
            {
                impactStrength = 0f;
                impactDuration = 0f;
                return 0f;
            }

            // Squared falloff settles quickly instead of trailing off at a constant rate.
            float falloff = impactDuration > 0f ? impactTimeRemaining / impactDuration : 0f;
            return impactStrength * falloff * falloff;
        }

        private float AdvanceRumble(float deltaTime)
        {
            if (rumbleHoldRemaining > 0f)
                rumbleHoldRemaining = Mathf.Max(0f, rumbleHoldRemaining - deltaTime);

            float desiredLevel = rumbleHoldRemaining > 0f ? 1f : 0f;
            rumbleLevel = Mathf.MoveTowards(rumbleLevel, desiredLevel,
                deltaTime / Mathf.Max(0.01f, rumbleFadeDuration));

            if (rumbleLevel <= 0f)
            {
                rumbleStrength = 0f;
                return 0f;
            }

            return rumbleStrength * rumbleLevel;
        }

        // Perlin noise keeps successive frames related, which reads as a shake rather than static. Perlin rarely
        // reaches its extremes, so the remap is widened and clamped instead of losing most of the requested range.
        private static Vector2 SampleNoise(Vector2 seed, float frequency)
        {
            float sample = Time.time * frequency;
            float x = Mathf.Clamp((Mathf.PerlinNoise(seed.x + sample, 0f) - 0.5f) * 2.6f, -1f, 1f);
            float y = Mathf.Clamp((Mathf.PerlinNoise(0f, seed.y + sample) - 0.5f) * 2.6f, -1f, 1f);
            return new Vector2(x, y);
        }

        private void OnDisable()
        {
            horizontalVelocity = 0f;

            // Leave the camera on its unshaken position so a mid-shake disable cannot bake in an offset.
            if (shakeOffset != Vector2.zero)
                transform.position -= (Vector3)shakeOffset;
            StopShake();
        }
    }
}
