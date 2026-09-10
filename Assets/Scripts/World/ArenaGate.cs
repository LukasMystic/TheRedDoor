using TheRedDoor.Boss;
using UnityEngine;

namespace TheRedDoor.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class ArenaGate : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private ArenaCheckpoint checkpoint;
        [SerializeField] private RespawnManager respawnManager;
        [SerializeField] private BossHealth keeperHealth;
        [SerializeField] private Collider2D keeperBodyCollider;

        [Header("Movement")]
        [Tooltip("How far below its authored raised position the gate waits while open.")]
        [SerializeField, Min(0.01f)] private float loweredDistance = 4f;
        [SerializeField, Min(0.01f)] private float movementSpeed = 8f;

        private Collider2D gateCollider;
        private Vector3 raisedLocalPosition;
        private Vector3 loweredLocalPosition;
        private Vector3 targetLocalPosition;
        private bool isRaised;

        public bool IsRaised => isRaised;

        private void Awake()
        {
            gateCollider = GetComponent<Collider2D>();

            if (checkpoint == null || respawnManager == null || keeperHealth == null || keeperBodyCollider == null ||
                checkpoint.gameObject.scene != gameObject.scene ||
                respawnManager.gameObject.scene != gameObject.scene ||
                keeperHealth.gameObject.scene != gameObject.scene ||
                keeperBodyCollider.gameObject.scene != gameObject.scene ||
                keeperBodyCollider.GetComponentInParent<BossHealth>() != keeperHealth)
            {
                Debug.LogError("ArenaGate needs the scene checkpoint, respawn manager, Keeper health, and Keeper body collider.", this);
                enabled = false;
                return;
            }

            if (gateCollider.isTrigger || keeperBodyCollider.isTrigger)
            {
                Debug.LogError("ArenaGate and Keeper Body Collider must both be solid, non-trigger colliders.", this);
                enabled = false;
                return;
            }

            raisedLocalPosition = transform.localPosition;
            loweredLocalPosition = raisedLocalPosition + Vector3.down * Mathf.Max(0.01f, loweredDistance);
            SetRaised(false, true);
        }

        private void OnEnable()
        {
            if (checkpoint != null)
                checkpoint.Activated += HandleCheckpointActivated;
            if (keeperHealth != null)
                keeperHealth.Defeated.AddListener(HandleKeeperDefeated);
        }

        private void OnDisable()
        {
            if (checkpoint != null)
                checkpoint.Activated -= HandleCheckpointActivated;
            if (keeperHealth != null)
                keeperHealth.Defeated.RemoveListener(HandleKeeperDefeated);
        }

        private void Start()
        {
            // RespawnManager consumes a carried arena checkpoint during Start. Every Start runs before Update,
            // so the first Update below also safely catches either script execution order on a retry.
            if (respawnManager != null && respawnManager.HasArenaCheckpoint &&
                keeperHealth != null && !keeperHealth.IsDefeated)
                SetRaised(true, true);
        }

        private void Update()
        {
            if (!isRaised && respawnManager != null && respawnManager.HasArenaCheckpoint &&
                keeperHealth != null && !keeperHealth.IsDefeated)
                SetRaised(true, false);

            transform.localPosition = Vector3.MoveTowards(
                transform.localPosition,
                targetLocalPosition,
                Mathf.Max(0.01f, movementSpeed) * Time.deltaTime);

        }

        private void HandleCheckpointActivated()
        {
            if (keeperHealth != null && !keeperHealth.IsDefeated)
                SetRaised(true, false);
        }

        private void HandleKeeperDefeated()
        {
            SetRaised(false, false);
            if (keeperBodyCollider != null)
                keeperBodyCollider.enabled = false;
        }

        private void SetRaised(bool value, bool immediate)
        {
            isRaised = value;
            targetLocalPosition = value ? raisedLocalPosition : loweredLocalPosition;

            // Closing starts only after the checkpoint trigger, which sits inside the arena.
            // Opening removes collision immediately so neither the gate nor the defeated Keeper blocks the player.
            gateCollider.enabled = value;
            if (immediate)
                transform.localPosition = targetLocalPosition;
        }
    }
}
