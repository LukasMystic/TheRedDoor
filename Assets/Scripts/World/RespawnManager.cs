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

        public bool IsRestarting => isRestarting;

        private void Awake()
        {
            if (playerHealth == null || playerHealth.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("RespawnManager needs the scene Player's Player Health reference.", this);
                enabled = false;
            }
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

        private IEnumerator RestartAfterDelay(string scenePath)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, restartDelay));

            if (playerHealth == null || !playerHealth.IsDead)
            {
                isRestarting = false;
                yield break;
            }

            // Single-mode reload restores all authored scene state: player, dummy, and timers.
            // Arena checkpoints will be added before this scene contains a tutorial section.
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
                enabled = false;
            }
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
