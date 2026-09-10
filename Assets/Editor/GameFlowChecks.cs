using System;
using System.Linq;
using System.Reflection;
using TheRedDoor.Boss;
using TheRedDoor.Player;
using TheRedDoor.UI;
using TheRedDoor.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Opt-in, Play-only smoke test. Never saves a scene or invokes Quit.
[InitializeOnLoad]
public static class GameFlowChecks
{
    static int step;
    static double next;
    static Vector3 startPosition;
    static GameFlowChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("RedDoor.MenuChecks", false))
            {
                SessionState.SetBool("RedDoor.MenuChecks", false);
                step = 0;
                next = EditorApplication.timeSinceStartup + 1;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.update -= Tick;
        };
    }

    [MenuItem("Tools/TheRedDoor/Verify Menu Flow")]
    static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play before running menu checks."); return; }
        SessionState.SetBool("RedDoor.MenuChecks", true);
        EditorApplication.isPlaying = true;
    }

    static T Field<T>(object o, string field) => (T)o.GetType().GetField(field,
        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    static void Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("MENU FAIL: " + message);
        Debug.Log("MENU PASS: " + message);
    }
    static void Click(GameFlowUI flow, string name) => flow.GetComponentsInChildren<Button>()
        .Single(b => string.Equals(b.name, "Button " + name,
            StringComparison.OrdinalIgnoreCase)).onClick.Invoke();
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        try
        {
            var flow = UnityEngine.Object.FindAnyObjectByType<GameFlowUI>();
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            var manager = UnityEngine.Object.FindAnyObjectByType<RespawnManager>();
            var health = manager.PlayerHealth;
            string page = Field<object>(flow, "current").ToString();
            switch (step)
            {
                case 0:
                    Check(page == "Title" && Time.timeScale == 0 && player.IsControlLocked, "Title freezes gameplay before spawn");
                    Check(GameFlowUI.SuppressGameMusic && Field<AudioSource>(flow, "titleMusic").isPlaying,
                        "Title music plays and suppresses gameplay beds");
                    Check(Field<AudioClip>(flow, "hoverClip") != null && Field<AudioClip>(flow, "clickClip") != null &&
                        Field<AudioClip>(flow, "backClip") != null, "All three UI sounds loaded");
                    Check(Field<PlayerHealth>(flow, "playerHealth") == health, "Menu uses authoritative health");
                    var playerInput = player.GetComponent<PlayerInput>();
                    var uiModule = UnityEngine.Object.FindAnyObjectByType<InputSystemUIInputModule>();
                    Check(uiModule != null && playerInput != null &&
                        uiModule.actionsAsset == playerInput.actions,
                        "Every menu page uses the project's controller-aware UI actions");
                    Check(uiModule.move != null && uiModule.move.action != null &&
                        uiModule.move.action.controls.All(control => !(control.device is Joystick)),
                        "Generic HID navigation is routed through the corrected joystick fallback");
                    Check(Field<bool>(flow, "invertJoystickMenuVertical"),
                        "This receiver's reversed joystick Y axis is corrected");
                    startPosition = player.transform.position;
                    ScreenCapture.CaptureScreenshot("/private/tmp/thereddoor-title.png");
                    break;
                case 1: Click(flow, "Begin"); break;
                case 2:
                    Check(page == "None" && Time.timeScale == 1 && !player.IsControlLocked, "Begin resumes gameplay");
                    var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHealthUI>();
                    var mat = Field<Material>(hud, "liquidMaterial");
                    Check(mat != null && mat.shader.isSupported && !ShaderUtil.ShaderHasError(mat.shader), "Liquid shader compiled and attached");
                    Check(health.TakeDamage(1, player.transform.position + Vector3.left), "Potion responds to real damage");
                    Invoke(flow, "Show", Enum.Parse(flow.GetType().GetNestedType("Page", BindingFlags.NonPublic), "Pause"));
                    break;
                case 3:
                    Check(page == "Pause" && Time.timeScale == 0 && GameFlowUI.MusicDuck == .35f, "Pause freezes and ducks music");
                    ScreenCapture.CaptureScreenshot("/private/tmp/thereddoor-pause.png");
                    break;
                case 4: Click(flow, "Resume"); break;
                case 5:
                    Check(page == "None" && Time.timeScale == 1, "Resume restores gameplay");
                    manager.TryActivateArenaCheckpoint(health, new Vector2(-5.5f, -1.5f));
                    health.Kill(player.transform.position);
                    next = EditorApplication.timeSinceStartup + 4;
                    step++; return;
                case 6:
                    Check(page == "None" && !health.IsDead && manager.HasArenaCheckpoint, "Death reload skips title and retains checkpoint");
                    Check(UnityEngine.Object.FindObjectsByType<GameFlowUI>().Length == 1,
                        "Exactly one menu after scene reload");
                    UnityEngine.Object.FindAnyObjectByType<BossHealth>().TakeDamage(9999);
                    var door = UnityEngine.Object.FindAnyObjectByType<RedDoor>();
                    player.transform.position = door.transform.position;
                    player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
                    Check(door.TryOpen(), "Unlocked door opens through real interaction");
                    break;
                case 7:
                    Check(page == "End" && Time.timeScale == 0, "Door shows end card");
                    ScreenCapture.CaptureScreenshot("/private/tmp/thereddoor-ending.png");
                    break;
                case 8: Click(flow, "Keep Looking Around"); break;
                case 9:
                    Check(page == "None" && !player.IsControlLocked && Time.timeScale == 1, "Continue Exploring restores controls");
                    var legacy = UnityEngine.Object.FindAnyObjectByType<RedDoorUI>();
                    Check(Field<CanvasGroup>(legacy, "endingOverlay").alpha == 0 &&
                        !Field<CanvasGroup>(legacy, "endingOverlay").blocksRaycasts, "Legacy ending remains hidden during exploration");
                    Check(UnityEngine.Object.FindAnyObjectByType<BossHealth>().IsDefeated, "Exploration preserves boss defeat");
                    Invoke(flow, "Show", Enum.Parse(flow.GetType().GetNestedType("Page", BindingFlags.NonPublic), "Pause"));
                    break;
                case 10: Click(flow, "Main Menu"); break;
                case 11:
                    Check(page == "Title" && !manager.HasArenaCheckpoint && !UnityEngine.Object.FindAnyObjectByType<BossHealth>().IsDefeated,
                        "Main Menu resets run and checkpoint");
                    Check(Mathf.Abs(player.transform.position.x - startPosition.x) < .01f, "New journey returns to tutorial start");
                    Check(Time.timeScale == 0 && Field<AudioSource>(flow, "titleMusic").isPlaying, "Title and its music survive repeated loads");
                    Debug.Log("MENU CHECKS COMPLETE: Quit intentionally not executed. Stop Play to discard all test state.");
                    EditorApplication.update -= Tick;
                    return;
            }
            step++;
            next = EditorApplication.timeSinceStartup + .75;
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.update -= Tick; }
    }
}
