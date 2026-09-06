using UnityEngine;

namespace TheRedDoor.Boss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeeperController), typeof(BossHealth))]
    public sealed class KeeperAnimationController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Animation States")]
        [SerializeField] private string layerName = "Base Layer";
        [SerializeField] private string idleStateName = "Keeper_Idle";
        [SerializeField] private string attackOneStateName = "Keeper_Attack1";

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.05f;

        private KeeperController controller;
        private int idleStateHash;
        private int attackOneStateHash;
        private int currentStateHash;

        private void Awake()
        {
            controller = GetComponent<KeeperController>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>();

            idleStateHash = HashStateName(idleStateName);
            attackOneStateHash = HashStateName(attackOneStateName);
        }

        private void Start()
        {
            if (!ValidateSetup())
                enabled = false;
        }

        private void OnEnable()
        {
            currentStateHash = 0;
        }

        private void LateUpdate()
        {
            int desiredStateHash = SelectAnimationState();
            if (desiredStateHash == currentStateHash)
                return;

            animator.CrossFade(desiredStateHash, Mathf.Max(0f, crossFadeDuration), 0);
            currentStateHash = desiredStateHash;
        }

        private int SelectAnimationState()
        {
            KeeperController.State state = controller.CurrentState;
            if (state == KeeperController.State.Telegraph || state == KeeperController.State.Swipe)
                return attackOneStateHash;

            // Let an attack finish its authored return pose during that attack's recovery.
            if (state == KeeperController.State.Recovery && currentStateHash != 0)
                return currentStateHash;

            return idleStateHash;
        }

        private int HashStateName(string stateName)
        {
            return Animator.StringToHash($"{layerName}.{stateName}");
        }

        private bool ValidateSetup()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("KeeperAnimationController needs the Keeper Visual Animator and its controller.", this);
                return false;
            }

            if (!animator.HasState(0, idleStateHash) || !animator.HasState(0, attackOneStateHash))
            {
                Debug.LogError(
                    "KeeperAnimator must contain the configured Idle and Attack 1 states on Base Layer.",
                    this);
                return false;
            }

            return true;
        }
    }
}
