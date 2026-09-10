using System;
using System.Collections;
using TheRedDoor.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheRedDoor.World
{
    // One scene-owned manager for the current test encounter. Do not persist it between scenes.
    [DisallowMultipleComponent]
    public sealed class RespawnManager : MonoBehaviour
    {
        [SerializeField] private PlayerHealth playerHealth;
        [Tooltip("Real-time delay before reloading this scene. Scene loading adds a little extra time.")]
        [SerializeField, Min(0f)] private float restartDelay = 2.5f;

        private bool isRestarting;
        private bool sceneLoadRequested;
        private bool hasArenaCheckpoint;
        private Vector2 arenaCheckpointPosition;

        private static bool hasPendingCheckpointRespawn;
        private static string pendingCheckpointScenePath;
        private static Vector2 pendingCheckpointPosition;

        public bool IsRestarting => isRestarting;
        public PlayerHealth PlayerHealth => playerHealth;
        public bool HasArenaCheckpoint => hasArenaCheckpoint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPendingCheckpoint()
        {
            ClearPendingCheckpoint();
        }

        private void Awake()
        {
            if (playerHealth == null || playerHealth.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("RespawnManager needs the scene Player's Player Health reference.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            if (!hasPendingCheckpointRespawn)
                return;

            string scenePath = gameObject.scene.path;
            if (pendingCheckpointScenePath != scenePath)
            {
                ClearPendingCheckpoint();
                return;
            }

            hasArenaCheckpoint = true;
            arenaCheckpointPosition = pendingCheckpointPosition;
            ClearPendingCheckpoint();
            MovePlayerToArenaCheckpoint();
        }

        private void Update()
        {
            // This also catches a death that occurred while the manager was disabled.
            if (!isRestarting && playerHealth != null && playerHealth.IsDead)
            {
                string scenePath = gameObject.scene.path;
                if (string.IsNullOrEmpty(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
                {
                    Debug.LogError("RespawnManager cannot reload this scene. Save it and enable it in File > Build Profiles > Scene List.", this);
                    enabled = false;
                    return;
                }

                isRestarting = true;
                StartCoroutine(RestartAfterDelay(scenePath));
            }
        }

        public bool TryActivateArenaCheckpoint(PlayerHealth enteringPlayer, Vector2 respawnPosition)
        {
            if (!Application.isPlaying || enteringPlayer == null ||
                enteringPlayer.gameObject != playerHealth.gameObject ||
                playerHealth.IsDead || isRestarting)
                return false;

            hasArenaCheckpoint = true;
            arenaCheckpointPosition = respawnPosition;
            return true;
        }

        public bool TryKillPlayerFromHazard(PlayerHealth enteringPlayer, Vector2 hazardPosition)
        {
            if (!Application.isPlaying || enteringPlayer == null ||
                enteringPlayer.gameObject != playerHealth.gameObject || isRestarting)
                return false;

            return playerHealth.Kill(hazardPosition);
        }

        private IEnumerator RestartAfterDelay(string scenePath)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, restartDelay));

            if (playerHealth == null || !playerHealth.IsDead)
            {
                isRestarting = false;
                yield break;
            }

            // Single-mode reload restores the encounter. Carry only the arena spawn position
            // through that one reload so a retry does not repeat the tutorial course.
            if (hasArenaCheckpoint)
            {
                hasPendingCheckpointRespawn = true;
                pendingCheckpointScenePath = scenePath;
                pendingCheckpointPosition = arenaCheckpointPosition;
            }
            else
            {
                ClearPendingCheckpoint();
            }

            sceneLoadRequested = true;
            Time.timeScale = 1f;
            try
            {
                if (SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single) == null)
                    throw new InvalidOperationException("Unity did not start the scene reload.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"RespawnManager failed to reload '{scenePath}': {exception.Message}", this);
                sceneLoadRequested = false;
                ClearPendingCheckpoint();
                enabled = false;
            }
        }

        private void MovePlayerToArenaCheckpoint()
        {
            Vector3 playerPosition = playerHealth.transform.position;
            playerPosition.x = arenaCheckpointPosition.x;
            playerPosition.y = arenaCheckpointPosition.y;
            playerHealth.transform.position = playerPosition;

            Rigidbody2D body = playerHealth.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = arenaCheckpointPosition;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.WakeUp();
            }

            Physics2D.SyncTransforms();
        }

        private static void ClearPendingCheckpoint()
        {
            hasPendingCheckpointRespawn = false;
            pendingCheckpointScenePath = null;
            pendingCheckpointPosition = Vector2.zero;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            // An issued scene load cannot be cancelled; do not queue another on re-enable.
            if (!sceneLoadRequested)
                isRestarting = false;
        }
    }
}
