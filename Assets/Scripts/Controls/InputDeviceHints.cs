using UnityEngine;
using UnityEngine.InputSystem;

namespace TheRedDoor.Controls
{
    // One answer to "what is the player holding right now?", shared by every prompt on screen.
    //
    // The Input Actions asset declares no control schemes on purpose, so keyboard and pad are both
    // live at once and nothing has to switch. That leaves nobody tracking which one is actually in
    // the player's hands, which is what the on-screen wording needs, so this tracks it: the last
    // device to actually produce input wins, and everything that prints a button name reads Version
    // to know when to re-word itself.
    [DisallowMultipleComponent]
    public sealed class InputDeviceHints : MonoBehaviour
    {
        private const float MouseMoveThreshold = 4f;

        private static InputDeviceHints instance;
        private static bool padActive;
        private static bool padActuatedLastFrame;

        // True while the most recent input came from a pad. Prompts read this to pick their wording.
        public static bool UsingGamepad => padActive;

        // Bumped whenever the answer changes, so a label can refresh without rebuilding its string
        // every frame.
        public static int Version { get; private set; }

        // A pad-shaped device went from at rest to actuated this frame: any button, any stick, any
        // brand. The menus use this to hand keyboard focus to the first row, which is the only way a
        // controller can get a selection when nothing is pre-selected.
        public static bool MenuNavigationRequested { get; private set; }

        // Unity only recognises some controllers as Gamepad; anything else that reports as a stick
        // still deserves the pad wording, so both families count.
        public static bool AnyPadConnected => Gamepad.all.Count > 0 || Joystick.all.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Spawn();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => Spawn();

        private static void Spawn()
        {
            if (instance != null)
                return;
            var host = new GameObject("Input Device Hints");
            host.AddComponent<InputDeviceHints>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            // Start on whatever is plugged in, so the very first frame of the title screen is
            // already worded correctly for a controller player who never touches the keyboard.
            // Set() rather than a plain assignment, so anything that already cached a Version this
            // frame is told to re-word itself.
            Set(AnyPadConnected && Keyboard.current == null);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            bool padActuated = PadActuated();
            // Rising edge only. Holding a stick down must not keep re-claiming the first menu row.
            MenuNavigationRequested = padActuated && !padActuatedLastFrame;
            padActuatedLastFrame = padActuated;

            // Keyboard and mouse are checked first and win: a key press and a deliberate mouse
            // move are discrete events, while a cheap controller can report a stick or trigger as
            // permanently off its resting value. Testing the pad first would latch pad wording on
            // for the rest of the session.
            var keys = Keyboard.current;
            if (keys != null && keys.anyKey.wasPressedThisFrame)
            {
                Set(false);
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame ||
                mouse.delta.ReadValue().sqrMagnitude > MouseMoveThreshold))
            {
                Set(false);
                return;
            }

            if (padActuated)
                Set(true);
        }

        private static void Set(bool gamepad)
        {
            if (padActive == gamepad)
                return;
            padActive = gamepad;
            Version++;
        }

        // "Is a pad-shaped device sending anything other than its resting state?" Asking the device
        // rather than named controls means a stick, a trigger or a face button all answer yes, and
        // an unrecognised controller that only reports as a Joystick answers yes too.
        private static bool PadActuated()
        {
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (Actuated(pads[i]))
                    return true;

            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
                if (Actuated(sticks[i]))
                    return true;

            return false;
        }

        private static bool Actuated(InputDevice device)
        {
            return device != null && device.added && device.wasUpdatedThisFrame &&
                !device.CheckStateIsAtDefaultIgnoringNoise();
        }

        // Prompt text is written once with tokens and re-worded here, so the keyboard and pad
        // versions of a line can never drift apart.
        //
        // Button names come from the device itself: an Xbox pad answers "A", a DualSense answers
        // "Cross". Nothing has to know which brand is plugged in.
        public static string Format(string template)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            var pad = Gamepad.current;
            return template
                .Replace("{move}", "Left Stick or D-Pad")
                .Replace("{jump}", Name(pad != null ? pad.buttonSouth : null, "A"))
                .Replace("{attack}", Name(pad != null ? pad.buttonWest : null, "X"))
                .Replace("{interact}", Name(pad != null ? pad.buttonNorth : null, "Y"))
                .Replace("{dash}", Name(pad != null ? pad.rightShoulder : null, "RB"))
                .Replace("{pause}", Name(pad != null ? pad.startButton : null, "Start"));
        }

        private static string Name(InputControl control, string fallback)
        {
            if (control == null)
                return fallback;
            string label = control.shortDisplayName;
            if (string.IsNullOrEmpty(label))
                label = control.displayName;
            return string.IsNullOrEmpty(label) ? fallback : label;
        }
    }
}
