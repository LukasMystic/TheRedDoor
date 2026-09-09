using TheRedDoor.Boss;
using TheRedDoor.Player;
using TheRedDoor.World;
using UnityEngine;

namespace TheRedDoor.Audio
{
    // Scene-owned presentation only. Sources disappear on reload: no duplicate persistent music.
    [DisallowMultipleComponent]
    public sealed class DemoAudio : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private KeeperController keeper;
        [SerializeField] private BossHealth keeperHealth;
        [SerializeField] private RespawnManager respawnManager;
        [SerializeField] private ArenaGate gate;
        [SerializeField] private RedDoor door;

        [Header("Music and Ambience")]
        [SerializeField] private AudioClip explorationMusic;
        [SerializeField] private AudioClip battleMusic;
        [SerializeField] private AudioClip forestAmbience;
        [Tooltip("Takes over once the Keeper falls and carries through to the Red Door.")]
        [SerializeField] private AudioClip outroMusic;
        [SerializeField, Min(0.1f)] private float musicFadeSeconds = 1.2f;

        [Header("Sound Effects")]
        [SerializeField] private AudioClip[] footsteps;
        [SerializeField] private AudioClip jump;
        [SerializeField] private AudioClip dash;
        [SerializeField] private AudioClip swordSwing;
        [SerializeField] private AudioClip playerHit;
        [SerializeField] private AudioClip keeperHit;
        [SerializeField] private AudioClip keeperWindup;
        [SerializeField] private AudioClip keeperSwing;
        [SerializeField] private AudioClip groundSlam;
        [Tooltip("Separate from the slam so the single-target blow does not read as the arena-wide one.")]
        [SerializeField] private AudioClip heavyStrike;
        [Tooltip("The big one: encounter start and the heavy strike telegraph.")]
        [SerializeField] private AudioClip keeperRoar;
        [SerializeField] private AudioClip keeperDeath;
        [SerializeField] private AudioClip gateClose;
        [SerializeField] private AudioClip gateOpen;
        [SerializeField] private AudioClip doorOpen;
        [SerializeField] private AudioClip victory;

        [Header("Processed Clips")]
        [Tooltip("Load the level-matched set from Resources/Audio by name, falling back to whatever is assigned above when a file is missing. Turn off to use only the Inspector assignments.")]
        [SerializeField] private bool useProcessedClips = true;

        [Header("Mix (0 = mute)")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.12f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        private AudioSource explorationSource, battleSource, ambienceSource, outroSource;
        private AudioSource playerSource, bossSource, worldSource, stepSource;
        private Rigidbody2D playerBody;
        private bool initialized, connected, wasGrounded, wasDashing, wasRaised, wasOpened, encounterSeen;
        private KeeperController.State lastState;
        private float footstepTimer, airborneTime, explorationBlend, battleBlend, ambienceBlend, outroBlend;
        private int nextFootstep;

        private void Awake()
        {
            if (player == null || playerHealth == null || combat == null || keeper == null ||
                keeperHealth == null || respawnManager == null || gate == null || door == null ||
                player.gameObject != playerHealth.gameObject || keeper.gameObject != keeperHealth.gameObject)
            {
                Debug.LogError("DemoAudio needs the configured scene player, Keeper, gate, door and respawn manager.", this);
                enabled = false;
                return;
            }
            playerBody = player.GetComponent<Rigidbody2D>();
            ResolveProcessedClips();
            explorationSource = CreateSource("Exploration Music", explorationMusic, true);
            battleSource = CreateSource("Boss Music", battleMusic, true);
            ambienceSource = CreateSource("Forest Ambience", forestAmbience, true);
            outroSource = CreateSource("Outro Music", outroMusic, true);
            playerSource = CreateSource("Player SFX");
            bossSource = CreateSource("Keeper SFX");
            worldSource = CreateSource("World SFX");
            stepSource = CreateSource("Footsteps");
            initialized = true;
        }

        // The processed set is level-matched and loop-prepared, so it is preferred when present. Anything
        // missing from Resources quietly keeps whatever is wired in the Inspector.
        private void ResolveProcessedClips()
        {
            if (!useProcessedClips)
                return;

            explorationMusic = Pick(explorationMusic, "Audio/Music/Music_Exploration");
            battleMusic = Pick(battleMusic, "Audio/Music/Music_Boss");
            forestAmbience = Pick(forestAmbience, "Audio/Music/Ambience_Forest");
            outroMusic = Pick(outroMusic, "Audio/Music/Music_Outro");

            AudioClip stepA = Pick(null, "Audio/SFX/Footstep_Grass_A");
            AudioClip stepB = Pick(null, "Audio/SFX/Footstep_Grass_B");
            if (stepA != null && stepB != null)
                footsteps = new[] { stepA, stepB };

            jump = Pick(jump, "Audio/SFX/Player_Jump");
            dash = Pick(dash, "Audio/SFX/Player_Dash");
            swordSwing = Pick(swordSwing, "Audio/SFX/Player_Swing");
            playerHit = Pick(playerHit, "Audio/SFX/Player_Hit");
            keeperHit = Pick(keeperHit, "Audio/SFX/Keeper_Hit");
            keeperWindup = Pick(keeperWindup, "Audio/SFX/Keeper_Windup");
            keeperSwing = Pick(keeperSwing, "Audio/SFX/Keeper_Swing");
            groundSlam = Pick(groundSlam, "Audio/SFX/Keeper_Slam");
            heavyStrike = Pick(heavyStrike, "Audio/SFX/Keeper_HeavyStrike");
            keeperRoar = Pick(keeperRoar, "Audio/SFX/Keeper_Roar");
            keeperDeath = Pick(keeperDeath, "Audio/SFX/Keeper_Death");
            gateClose = Pick(gateClose, "Audio/SFX/Gate_Close");
            gateOpen = Pick(gateOpen, "Audio/SFX/Gate_Open");
            doorOpen = Pick(doorOpen, "Audio/SFX/Door_Open");
            victory = Pick(victory, "Audio/SFX/Victory");
        }

        private static AudioClip Pick(AudioClip assigned, string resourcePath)
        {
            AudioClip processed = Resources.Load<AudioClip>(resourcePath);
            return processed != null ? processed : assigned;
        }

        private AudioSource CreateSource(string label, AudioClip clip = null, bool loop = false)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = loop;
            source.clip = clip;
            source.volume = 0f;
            source.priority = loop ? 128 : 64;
            return source;
        }

        private void OnEnable()
        {
            if (!initialized) return;
            playerHealth.Damaged.AddListener(OnPlayerHit);
            playerHealth.Died.AddListener(OnPlayerDeath);
            combat.AttackStarted.AddListener(OnPlayerAttack);
            keeperHealth.Damaged.AddListener(OnKeeperHit);
            keeperHealth.Defeated.AddListener(OnKeeperDeath);
            connected = true;
            wasGrounded = player.IsGrounded;
            wasDashing = player.IsDashing;
            wasRaised = gate.IsRaised;
            wasOpened = door.HasOpened;
            lastState = keeper.CurrentState;
            footstepTimer = 0f;
        }

        private void OnDisable()
        {
            if (connected)
            {
                playerHealth.Damaged.RemoveListener(OnPlayerHit);
                playerHealth.Died.RemoveListener(OnPlayerDeath);
                combat.AttackStarted.RemoveListener(OnPlayerAttack);
                keeperHealth.Damaged.RemoveListener(OnKeeperHit);
                keeperHealth.Defeated.RemoveListener(OnKeeperDeath);
                connected = false;
            }
            if (!initialized) return;
            foreach (var source in GetComponentsInChildren<AudioSource>()) source.Stop();
            explorationBlend = battleBlend = ambienceBlend = 0f;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            bool dead = playerHealth.IsDead;
            encounterSeen |= respawnManager.HasArenaCheckpoint ||
                (keeper.CurrentState != KeeperController.State.Idle && keeper.CurrentState != KeeperController.State.Defeated);
            bool battle = encounterSeen && !keeperHealth.IsDefeated;
            // The outro owns the music from the moment the Keeper falls, through the walk to the Red Door.
            bool outro = encounterSeen && keeperHealth.IsDefeated;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.1f, musicFadeSeconds);
            explorationBlend = Mathf.MoveTowards(explorationBlend, !dead && !battle && !outro ? 1f : 0f, step);
            battleBlend = Mathf.MoveTowards(battleBlend, !dead && battle && !door.HasOpened ? 1f : 0f, step);
            outroBlend = Mathf.MoveTowards(outroBlend, !dead && outro ? 1f : 0f, step);
            ambienceBlend = Mathf.MoveTowards(ambienceBlend, dead ? 0f : battle ? 0.35f : 1f, step);
            UpdateLoop(explorationSource, explorationBlend * musicVolume);
            UpdateLoop(battleSource, battleBlend * musicVolume);
            UpdateLoop(outroSource, outroBlend * musicVolume);
            UpdateLoop(ambienceSource, ambienceBlend * ambienceVolume);
            playerSource.volume = bossSource.volume = worldSource.volume = stepSource.volume = masterVolume * sfxVolume;

            if (!dead && !player.IsControlLocked)
            {
                if (player.IsDashing && !wasDashing) Play(playerSource, dash, 0.65f, 1.2f);
                if (wasGrounded && !player.IsGrounded && !player.IsDashing &&
                    player.ControlsEnabled && playerBody.linearVelocity.y > 1f)
                    Play(playerSource, jump, 0.5f, 1.15f);
                if (!player.IsGrounded) airborneTime += Time.deltaTime;
                else
                {
                    if (!wasGrounded && airborneTime > 0.12f) Step(0.5f);
                    airborneTime = 0f;
                }
                footstepTimer -= Time.deltaTime;
                if (player.IsGrounded && player.ControlsEnabled &&
                    Mathf.Abs(player.HorizontalInput) > 0.1f && Mathf.Abs(playerBody.linearVelocity.x) > 0.5f && footstepTimer <= 0f)
                {
                    Step(0.3f);
                    footstepTimer = 0.32f;
                }
            }
            wasGrounded = player.IsGrounded;
            wasDashing = player.IsDashing;

            if (gate.IsRaised != wasRaised)
            {
                Play(worldSource, gate.IsRaised ? gateClose : gateOpen, 0.7f, 0.8f);
                if (gate.IsRaised && !keeperHealth.IsDefeated)
                    Play(bossSource, keeperRoar, 0.9f); // The arena sealing is the encounter announcing itself.
            }
            wasRaised = gate.IsRaised;
            if (door.HasOpened && !wasOpened) Play(worldSource, doorOpen, 0.7f);
            wasOpened = door.HasOpened;

            if (keeper.CurrentState != lastState && !dead)
            {
                switch (keeper.CurrentState)
                {
                    case KeeperController.State.Telegraph:
                    case KeeperController.State.ChargeTelegraph:
                    case KeeperController.State.SlamTelegraph:
                        Play(bossSource, keeperWindup, 0.6f); break;
                    case KeeperController.State.HeavyTelegraph:
                        Play(bossSource, keeperRoar != null ? keeperRoar : keeperWindup, 0.85f); break;
                    case KeeperController.State.Swipe:
                        Play(bossSource, keeperSwing, 0.75f, 0.75f); break;
                    case KeeperController.State.Charge:
                        Play(bossSource, dash, 0.9f, 0.65f); break;
                    case KeeperController.State.Slam:
                        // The clip is already pitched and layered for weight, so it plays at its own pitch.
                        Play(bossSource, groundSlam, 1f); break;
                    case KeeperController.State.HeavyStrike:
                        Play(bossSource, heavyStrike != null ? heavyStrike : groundSlam, 0.95f); break;
                }
            }
            lastState = keeper.CurrentState;
        }

        private void UpdateLoop(AudioSource source, float gain)
        {
            source.volume = gain * masterVolume;
            if (source.clip == null) return;
            if (gain > 0f && !source.isPlaying) source.Play();
            else if (gain <= 0f && source.isPlaying) source.Stop();
        }

        private void Play(AudioSource source, AudioClip clip, float gain = 1f, float pitch = 1f)
        {
            if (!initialized || !isActiveAndEnabled || source == null || clip == null) return;
            source.volume = masterVolume * sfxVolume;
            source.pitch = pitch;
            source.PlayOneShot(clip, gain);
        }

        private void Step(float gain)
        {
            if (footsteps == null || footsteps.Length == 0) return;
            Play(stepSource, footsteps[nextFootstep++ % footsteps.Length], gain, Random.Range(0.94f, 1.06f));
        }
        private void OnPlayerAttack() => Play(playerSource, swordSwing, 0.65f);
        private void OnPlayerHit() { if (!playerHealth.IsDead) Play(playerSource, playerHit, 0.85f); }
        private void OnPlayerDeath() => Play(playerSource, playerHit, 0.9f, 0.65f);
        private void OnKeeperHit() { if (!keeperHealth.IsDefeated) Play(bossSource, keeperHit, 0.8f, 0.8f); }
        private void OnKeeperDeath()
        {
            Play(bossSource, keeperDeath != null ? keeperDeath : groundSlam, 0.95f);
            Play(worldSource, victory, 0.6f, 0.9f);
        }
    }
}
