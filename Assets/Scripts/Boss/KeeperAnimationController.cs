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
        [SerializeField] private string chargeStateName = "Keeper_Charge";
        [SerializeField] private string groundSlamStateName = "Keeper_GroundSlam";
        [SerializeField] private string heavyStrikeStateName = "Keeper_HeavyStrike";
        [SerializeField] private string deathStateName = "Keeper_Death";

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.05f;

        private KeeperController controller;
        private int idleStateHash;
        private int attackOneStateHash;
        private int chargeStateHash;
        private int groundSlamStateHash;
        private int heavyStrikeStateHash;
        private int deathStateHash;
        private int currentStateHash;

        private void Awake()
        {
            controller = GetComponent<KeeperController>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>();

            idleStateHash = HashStateName(idleStateName);
            attackOneStateHash = HashStateName(attackOneStateName);
            chargeStateHash = HashStateName(chargeStateName);
            groundSlamStateHash = HashStateName(groundSlamStateName);
            heavyStrikeStateHash = HashStateName(heavyStrikeStateName);
            deathStateHash = HashStateName(deathStateName);
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
            if (state == KeeperController.State.Defeated)
                return deathStateHash;

            if (state == KeeperController.State.Telegraph || state == KeeperController.State.Swipe)
                return attackOneStateHash;

            if (state == KeeperController.State.Charge || state == KeeperController.State.HeavyAdvance)
                return chargeStateHash;

            if (state == KeeperController.State.SlamTelegraph || state == KeeperController.State.Slam)
                return groundSlamStateHash;

            if (state == KeeperController.State.HeavyWindup || state == KeeperController.State.HeavyStrike)
                return heavyStrikeStateHash;

            // Stationary attacks have authored return poses; moving attacks return to Idle when movement stops.
            if (state == KeeperController.State.Recovery
                && (currentStateHash == attackOneStateHash
                    || currentStateHash == groundSlamStateHash
                    || currentStateHash == heavyStrikeStateHash))
            {
                return currentStateHash;
            }

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

            if (!animator.HasState(0, idleStateHash)
                || !animator.HasState(0, attackOneStateHash)
                || !animator.HasState(0, chargeStateHash)
                || !animator.HasState(0, groundSlamStateHash)
                || !animator.HasState(0, heavyStrikeStateHash)
                || !animator.HasState(0, deathStateHash))
            {
                Debug.LogError(
                    "KeeperAnimator must contain the configured Idle, Attack 1, Charge, Ground Slam, and " +
                    "Heavy Strike, and Death states on Base Layer.",
                    this);
                return false;
            }

            return true;
        }
    }
}
