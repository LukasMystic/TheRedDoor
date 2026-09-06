using TheRedDoor.Player;
using UnityEngine;

namespace TheRedDoor.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ArenaCheckpoint : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private RespawnManager respawnManager;
        [Tooltip("Where the Player root appears after a boss-fight death. Uses this object when empty.")]
        [SerializeField] private Transform respawnPoint;

        private BoxCollider2D triggerCollider;
        private bool isActivated;

        private void Reset()
        {
            BoxCollider2D checkpointCollider = GetComponent<BoxCollider2D>();
            checkpointCollider.isTrigger = true;
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider2D>();
            respawnPoint = respawnPoint != null ? respawnPoint : transform;

            if (respawnManager == null || respawnManager.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("ArenaCheckpoint needs the scene Respawn Manager reference.", this);
                enabled = false;
                return;
            }

            if (!triggerCollider.isTrigger)
            {
                Debug.LogError("ArenaCheckpoint's Box Collider 2D must be a trigger.", this);
                enabled = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isActivated)
                return;

            PlayerHealth enteringPlayer = other.GetComponentInParent<PlayerHealth>();
            if (respawnManager.TryActivateArenaCheckpoint(enteringPlayer, respawnPoint.position))
                isActivated = true;
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider2D checkpointCollider = GetComponent<BoxCollider2D>();
            if (checkpointCollider == null)
                return;

            Gizmos.color = Color.cyan;
            Vector3 center = transform.TransformPoint(checkpointCollider.offset);
            Vector3 size = Vector3.Scale(checkpointCollider.size, transform.lossyScale);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
