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

        [Header("Level Bounds")]
        [Tooltip("Enable after the level edges have been blocked out.")]
        [SerializeField] private bool useHorizontalBounds;
        [Tooltip("Minimum and maximum camera-center X positions, not world-edge positions.")]
        [SerializeField] private Vector2 horizontalBounds = new(-10f, 10f);

        private float horizontalVelocity;

        private void Awake()
        {
            if (target == null || target.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("CameraFollow2D needs the scene Player root as its Target.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            float desiredX = target.position.x + horizontalOffset;
            float minimumX = Mathf.Min(horizontalBounds.x, horizontalBounds.y);
            float maximumX = Mathf.Max(horizontalBounds.x, horizontalBounds.y);

            if (useHorizontalBounds)
                desiredX = Mathf.Clamp(desiredX, minimumX, maximumX);

            float nextX = smoothTime <= 0f
                ? desiredX
                : Mathf.SmoothDamp(transform.position.x, desiredX, ref horizontalVelocity, smoothTime);

            if (useHorizontalBounds)
                nextX = Mathf.Clamp(nextX, minimumX, maximumX);

            Vector3 position = transform.position;
            position.x = nextX;
            transform.position = position;
        }

        private void OnDisable()
        {
            horizontalVelocity = 0f;
        }
    }
}
