using TheRedDoor.Player;
using UnityEngine;

namespace TheRedDoor.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class FallHazard : MonoBehaviour
    {
        [Tooltip("The Respawn Manager in this scene.")]
        [SerializeField] private RespawnManager respawnManager;

        private BoxCollider2D triggerCollider;

        private void Reset()
        {
            BoxCollider2D hazardCollider = GetComponent<BoxCollider2D>();
            hazardCollider.isTrigger = true;
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider2D>();

            if (respawnManager == null || respawnManager.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("FallHazard needs the scene Respawn Manager reference.", this);
                enabled = false;
                return;
            }

            if (!triggerCollider.isTrigger)
            {
                Debug.LogError("FallHazard's Box Collider 2D must be a trigger.", this);
                enabled = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerHealth enteringPlayer = other.GetComponentInParent<PlayerHealth>();
            if (enteringPlayer != null)
                respawnManager.TryKillPlayerFromHazard(enteringPlayer, transform.position);
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider2D hazardCollider = GetComponent<BoxCollider2D>();
            if (hazardCollider == null)
                return;

            Gizmos.color = Color.red;
            Vector3 center = transform.TransformPoint(hazardCollider.offset);
            Vector3 size = Vector3.Scale(hazardCollider.size, transform.lossyScale);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
