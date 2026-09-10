using TheRedDoor.Boss;
using TheRedDoor.Player;
using TheRedDoor.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheRedDoor.World
{
    // Diegetic opening: the story lives in the hollow itself as world-space lines that surface as the
    // player nears them and fade behind as they walk east. Nothing is a page, nothing is modal, and
    // the direction is taught by the fact that the lines only continue to the right.
    //
    // It also takes over the tutorial controls label once those controls have been read, turning that
    // same HUD slot into a running objective rather than adding a second element beside it.
    [DisallowMultipleComponent]
    public sealed class WorldStory : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn();

        private static void Spawn()
        {
            if (FindAnyObjectByType<PlayerController>() == null)
                return;
            if (FindAnyObjectByType<WorldStory>() != null)
                return;
            new GameObject("WorldStory").AddComponent<WorldStory>();
        }

        private struct Beat
        {
            public float X;
            public string Line;
            public TMP_Text Text;
        }

        // Authored against the tutorial ground (x -38 to -26), the two jump platforms (-22, -17)
        // and the arena entry wall at -11. The lines run only eastward on purpose.
        private static readonly (float x, string line)[] Script =
        {
            (-34f, "You do not remember lying down."),
            (-27f, "Moss has grown over your hands."),
            (-20f, "One path leaves the hollow. It runs east."),
            (-13f, "Something has kept the way a long time."),
        };

        private const float LineY = 0.35f;
        // Tightened so neighbouring lines never overlap: visible within 4.4 units against 7 units
        // of spacing, which is what went wrong when three of them were on screen at once.
        private const float HoldRange = 1.8f;
        private const float FalloffRange = 2.6f;
        private const float HandoffX = -30.5f;  // controls give way to the objective past here

        private Beat[] beats;
        private PlayerController player;
        private RespawnManager respawn;
        private BossHealth keeperHealth;
        private RedDoor door;
        private TutorialControlsUI controls;
        private TMP_Text objective;
        private float controlsAlpha = 1f;
        private bool handedOff;
        private string shownObjective;

        private void Awake()
        {
            player = FindAnyObjectByType<PlayerController>();
            respawn = FindAnyObjectByType<RespawnManager>();
            keeperHealth = FindAnyObjectByType<BossHealth>();
            door = FindAnyObjectByType<RedDoor>();
            controls = FindAnyObjectByType<TutorialControlsUI>();
            if (player == null)
            {
                Destroy(gameObject);
                return;
            }

            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Roboto-Bold SDF");
            beats = new Beat[Script.Length];
            for (int i = 0; i < Script.Length; i++)
            {
                var go = new GameObject($"Story {i + 1}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(Script[i].x, LineY, 0f);
                var text = go.AddComponent<TextMeshPro>();
                if (font != null) text.font = font;
                text.text = Script[i].line;
                // Sized by measurement, not by guessing twice: at 5 a 36-character line spanned
                // about 12.5 world units, roughly 0.35 units per character, so 2 puts the longest
                // line near 5.5 units. Wide enough to read, narrow enough not to own the screen.
                text.fontSize = 2f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(0.91f, 0.86f, 0.75f, 0f);
                var rect = text.rectTransform;
                rect.sizeDelta = new Vector2(12f, 2f);
                // Above the environment art but below the shockwave, so nothing important is hidden.
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sortingOrder = 8;
                beats[i] = new Beat { X = Script[i].x, Line = Script[i].line, Text = text };
            }

            if (controls != null)
                objective = controls.ControlsText;
        }

        private void LateUpdate()
        {
            if (player == null)
                return;

            float px = player.transform.position.x;

            for (int i = 0; i < beats.Length; i++)
            {
                var text = beats[i].Text;
                if (text == null) continue;
                float distance = Mathf.Abs(px - beats[i].X);
                float alpha = 1f - Mathf.Clamp01((distance - HoldRange) / Mathf.Max(0.01f, FalloffRange));
                var color = text.color;
                // Ease the edges so lines breathe in rather than switch on.
                color.a = Mathf.SmoothStep(0f, 1f, alpha);
                text.color = color;
            }

            UpdateObjective(px);
        }

        // The controls label fades out once the player is walking, then the same label comes back
        // carrying the objective. One slot, two jobs, no competing HUD.
        private void UpdateObjective(float px)
        {
            if (objective == null)
                return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            if (!handedOff)
            {
                if (px < HandoffX)
                    return;
                controlsAlpha = Mathf.MoveTowards(controlsAlpha, 0f, dt * 1.4f);
                var fading = objective.color;
                fading.a = controlsAlpha;
                objective.color = fading;
                if (controlsAlpha > 0f)
                    return;

                if (controls != null)
                    controls.ReleaseControl();
                handedOff = true;
                shownObjective = null;
            }

            string wanted = CurrentObjective();
            if (wanted != shownObjective)
            {
                shownObjective = wanted;
                objective.text = wanted;
                controlsAlpha = 0f;
            }

            objective.enabled = !string.IsNullOrEmpty(wanted);
            controlsAlpha = Mathf.MoveTowards(controlsAlpha, string.IsNullOrEmpty(wanted) ? 0f : 1f, dt * 1.6f);
            var color = objective.color;
            color.a = controlsAlpha;
            objective.color = color;
        }

        private string CurrentObjective()
        {
            if (door != null && door.HasOpened)
                return string.Empty;
            if (keeperHealth != null && keeperHealth.IsDefeated)
                return "Open the red door";
            if (respawn != null && respawn.HasArenaCheckpoint)
                return "Survive the Keeper";
            return "Head east";
        }
    }
}
