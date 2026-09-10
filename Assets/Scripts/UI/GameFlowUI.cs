using System;
using TheRedDoor.Player;
using TheRedDoor.World;
using TMPro;
using UnityEngine;
using TheRedDoor.Controls;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheRedDoor.UI
{
    // Title, pause and end-of-demo screens, built entirely in code so the scene needs no new
    // objects, no prefabs and no Inspector wiring. It spawns itself after the scene loads and
    // does nothing in a scene without a Player, so SampleScene is unaffected.
    [DisallowMultipleComponent]
    public sealed class GameFlowUI : MonoBehaviour
    {
        private enum Page { None, Title, Pause, End, Credits }

        // Survives the scene reloads that RespawnManager does on death, so dying does not
        // throw the player back to the title. Reset once per play session, not per load.
        private static bool showTitleOnLoad = true;

        public static bool SuppressGameMusic { get; private set; }
        public static bool OwnsEnding { get; private set; }
        public static float MusicDuck { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            showTitleOnLoad = true;
            SuppressGameMusic = false;
            OwnsEnding = false;
            MusicDuck = 1f;
            SceneManager.sceneLoaded -= OnSceneLoaded; // Editors with domain reload disabled keep statics.
        }

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
            // sceneLoaded runs after the scene's own Awake calls, so the Player is already there.
            if (FindAnyObjectByType<PlayerController>() == null)
                return; // Not a gameplay scene, so SampleScene is left alone.
            if (FindAnyObjectByType<GameFlowUI>() != null)
                return;
            var host = new GameObject("GameFlowUI");
            host.AddComponent<GameFlowUI>();
        }

        private static readonly Color Ink = new(0.031f, 0.055f, 0.043f, 0.94f);
        private static readonly Color Panel = new(0.055f, 0.086f, 0.067f, 0.90f);
        private static readonly Color Bone = new(0.91f, 0.86f, 0.75f, 1f);
        private static readonly Color Muted = new(0.62f, 0.66f, 0.58f, 1f);
        private static readonly Color Accent = new(0.78f, 0.27f, 0.18f, 1f);
        private static readonly Color Ember = new(0.88f, 0.64f, 0.23f, 1f);

        private PlayerController player;
        private PlayerHealth playerHealth;
        private RedDoor door;
        private RespawnManager respawn;

        private Canvas canvas;
        private GameObject titlePanel, pausePanel, endPanel, creditsPanel;
        private Page creditsReturn = Page.Title;
        private Page current = Page.None;
        private AudioSource uiSource, titleMusic;
        private AudioClip hoverClip, clickClip, backClip;
        private bool endShown;
        [Header("Controller")]
        [Tooltip("Unity has no layout for controllers that arrive as a generic HID joystick, so it " +
            "guesses which way the Y axis points. Its own source calls this a guess. This Mac's " +
            "2.4G XBOX 360 receiver is one it guesses wrong, which is why the menu scrolled " +
            "backwards. Turn this off if a different controller ends up inverted instead.")]
        [SerializeField] private bool invertJoystickMenuVertical = true;

        private float nextMenuRepeatTime;
        private bool menuRepeating;
        private int lastSubmitFrame = -1;
        private Canvas[] gameCanvases;
        private bool[] canvasWasEnabled;
        private TMP_FontAsset headingFont, bodyFont;
        private CanvasGroup activeGroup;
        private float reveal;
        private bool leaving;

        private void Awake()
        {
            player = FindAnyObjectByType<PlayerController>();
            door = FindAnyObjectByType<RedDoor>();
            respawn = FindAnyObjectByType<RespawnManager>();
            // Use the same component as retries, not an arbitrary duplicate on the prefab instance.
            playerHealth = respawn != null ? respawn.PlayerHealth :
                player != null ? player.GetComponent<PlayerHealth>() : null;
            if (player == null)
            {
                Destroy(gameObject);
                return;
            }

            OwnsEnding = true;
            gameCanvases = FindObjectsByType<Canvas>();
            canvasWasEnabled = new bool[gameCanvases.Length];
            for (int i = 0; i < gameCanvases.Length; i++) canvasWasEnabled[i] = gameCanvases[i].enabled;
            headingFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Oswald Bold SDF");
            bodyFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Roboto-Bold SDF");
            EventSystem menuEvents = EventSystem.current;
            if (menuEvents == null)
            {
                var events = new GameObject("Menu Event System", typeof(EventSystem),
                    typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                menuEvents = events.GetComponent<EventSystem>();
            }

            // The scene originally pointed at Input System's package-default UI actions. Use the
            // same project asset as gameplay so the generic-HID fallbacks (including this pad's
            // button numbering and hatswitch) also drive every menu page.
            var input = player.GetComponent<PlayerInput>();
            var uiModule = menuEvents != null ? menuEvents.GetComponent<InputSystemUIInputModule>() : null;
            if (uiModule == null && menuEvents != null)
                uiModule = menuEvents.gameObject.AddComponent<InputSystemUIInputModule>();
            if (uiModule != null && input != null && input.actions != null)
            {
                RouteGenericJoystickMenusThroughFallback(input.actions);
                uiModule.actionsAsset = input.actions;
            }

            hoverClip = Resources.Load<AudioClip>("Audio/SFX/UI_Hover");
            clickClip = Resources.Load<AudioClip>("Audio/SFX/UI_Click");
            backClip = Resources.Load<AudioClip>("Audio/SFX/UI_Back");
            uiSource = CreateSource("UI SFX", null, false);
            titleMusic = CreateSource("Title Music", Resources.Load<AudioClip>("Audio/Music/Music_Title"), true);

            BuildCanvas();
            BuildTitle();
            BuildPause();
            BuildEnd();
            BuildCredits();

            if (showTitleOnLoad)
                Show(Page.Title);
            else
                Show(Page.None);
        }

        private AudioSource CreateSource(string label, AudioClip clip, bool loop)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = loop;
            source.clip = clip;
            source.volume = loop ? 0.55f : 0.7f;
            source.ignoreListenerPause = true;
            return source;
        }

        private void Update()
        {
            if (leaving) return;
            if (activeGroup != null)
            {
                reveal = Mathf.MoveTowards(reveal, 1f, Time.unscaledDeltaTime * 5f);
                activeGroup.alpha = Mathf.SmoothStep(0f, 1f, reveal);
            }
            // Esc pauses and unpauses. Not while the title or the end card owns the screen,
            // and not while the player is dead or the scene is reloading around us.
            if (current == Page.Credits && BackPressed())
            {
                CreditsBack();
                return;
            }

            if (current == Page.Pause && BackPressed())
            {
                PlayClip(backClip);
                Show(Page.None);
                return;
            }

            bool blocked = current == Page.Title ||
                current == Page.End || current == Page.Credits ||
                (playerHealth != null && playerHealth.IsDead) ||
                (respawn != null && respawn.IsRestarting);
            if (!blocked && PausePressed())
            {
                PlayClip(current == Page.Pause ? backClip : clickClip);
                Show(current == Page.Pause ? Page.None : Page.Pause);
            }

            if (current != Page.None && EventSystem.current != null)
            {
                bool hasSelection = EventSystem.current.currentSelectedGameObject != null;

                // Moving the mouse drops keyboard focus, so a lit row always means the pointer is
                // there or you just navigated, never that something was selected a minute ago.
                // Skipped while a pad is in use: a nudged desk must not steal a controller's row.
                if (hasSelection && !InputDeviceHints.UsingGamepad && Mouse.current != null &&
                    Mouse.current.delta.ReadValue().sqrMagnitude > 4f)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                else if (!hasSelection && SubmitPressed())
                {
                    // A page opens with no highlighted row for mouse users. If the first thing a
                    // controller player does is press A/Cross, select and activate the first row in
                    // that same press rather than swallowing it merely to establish focus.
                    SelectFirstRow();
                    Submit(EventSystem.current.currentSelectedGameObject);
                }
                else if (!hasSelection && (NavigationPressed() || InputDeviceHints.UsingGamepad))
                {
                    // On a keyboard nothing is pre-selected and the first arrow press hands focus
                    // over. A pad has no pointer to fall back on, so it keeps a row lit at all
                    // times; without one there is nothing for Submit to press and the menu looks
                    // dead.
                    SelectFirstRow();
                }

                DriveJoystickMenu();
            }

            if (!endShown && door != null && door.HasOpened && current == Page.None)
            {
                endShown = true;
                Show(Page.End);
            }
        }

        // Esc on a keyboard, Start on a pad. The gameplay actions come from the Input Actions asset,
        // which already binds a gamepad, but these three menu inputs are read from devices directly,
        // so each one has to name the pad explicitly or a controller player cannot leave the fight.
        private static bool PausePressed()
        {
            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
                return true;
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].startButton.wasPressedThisFrame)
                    return true;
            // Start, plus Back, so there is a second way in if a controller reports them swapped.
            return JoystickButtonPressed(JoystickStart) || JoystickButtonPressed(JoystickBack);
        }

        private static bool BackPressed()
        {
            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
                return true;
            // Every connected pad, not just Gamepad.current: before a player has touched anything,
            // current is still null and they would have no way back out of the credits.
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].buttonEast.wasPressedThisFrame || pads[i].startButton.wasPressedThisFrame)
                    return true;
            return JoystickButtonPressed("button2") || JoystickButtonPressed(JoystickStart) ||
                JoystickButtonPressed(JoystickBack);
        }

        // Some otherwise normal USB/Bluetooth controllers arrive through macOS as Joystick
        // instead of Gamepad. Their conventional HID order is A/B/X/Y, shoulders, Back/Start.
        // Controls are looked up defensively because the base Joystick layout guarantees only
        // a stick and trigger; missing optional buttons simply return false.
        private static bool JoystickButtonPressed(string controlName)
        {
            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
            {
                ButtonControl button = sticks[i].TryGetChildControl<ButtonControl>(controlName);
                if (button != null && button.wasPressedThisFrame)
                    return true;
            }
            return false;
        }

        private static bool SubmitPressed()
        {
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].buttonSouth.wasPressedThisFrame)
                    return true;

            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
                if (sticks[i].trigger != null && sticks[i].trigger.wasPressedThisFrame)
                    return true;
            return false;
        }

        // Nothing is pre-selected, so some input has to hand focus to the first row. Without the pad
        // cases a controller could move the EventSystem's navigation but never had anything selected
        // to move from, which reads as the menus simply not responding.
        private static bool NavigationPressed()
        {
            var keys = Keyboard.current;
            if (keys != null && (keys.upArrowKey.wasPressedThisFrame || keys.downArrowKey.wasPressedThisFrame ||
                keys.wKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame ||
                keys.tabKey.wasPressedThisFrame))
                return true;

            // Naming pad controls one by one only covers the controllers Unity recognises as a
            // Gamepad, and it misses whichever button the player happens to try first. Asking
            // whether any pad-shaped device left its resting state covers every stick, trigger and
            // face button on every brand, including one that only reports as a plain Joystick.
            return InputDeviceHints.MenuNavigationRequested;
        }

        // A recognised Gamepad should use Unity's UI module. This receiver, however, is exposed as
        // a generic HID Joystick whose generated layout has the vertical direction reversed and
        // whose D-pad is four ordinary buttons rather than a Hatswitch. Disable only the explicit
        // <Joystick> UI bindings on this runtime copy so DriveJoystickMenu is the single owner of
        // those inputs; keyboard, mouse and semantic Gamepad bindings remain untouched.
        private static void RouteGenericJoystickMenusThroughFallback(InputActionAsset actions)
        {
            InputActionMap ui = actions != null ? actions.FindActionMap("UI", false) : null;
            if (ui == null)
                return;

            foreach (InputAction action in ui.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    string path = action.bindings[i].path;
                    if (!string.IsNullOrEmpty(path) &&
                        path.StartsWith("<Joystick>", StringComparison.OrdinalIgnoreCase))
                        action.ApplyBindingOverride(i, string.Empty);
                }
            }
        }

        // A controller that macOS hands over as a plain Joystick rather than a Gamepad cannot reach
        // the pages through the EventSystem at all: the UI input module's Navigate and Submit
        // actions resolve to keyboard, mouse and Gamepad controls only, so with a row lit the stick
        // moves nothing and the A button presses nothing. This walks the rows and fires the
        // highlighted one directly, and it stands down the moment the module can do the job itself.
        private void DriveJoystickMenu()
        {
            if (!JoystickMenuFallbackNeeded())
            {
                menuRepeating = false;
                return;
            }

            if (JoystickSubmitPressed() && Submit(EventSystem.current.currentSelectedGameObject))
                return;

            float vertical = JoystickVertical();
            if (Mathf.Abs(vertical) < 0.5f)
            {
                menuRepeating = false;
                return;
            }

            // Unscaled, because every page freezes the game.
            if (menuRepeating && Time.unscaledTime < nextMenuRepeatTime)
                return;
            nextMenuRepeatTime = Time.unscaledTime + (menuRepeating ? MenuRepeatRate : MenuRepeatDelay);
            menuRepeating = true;
            // Up on a stick is +Y, and the rows run downwards.
            MoveSelection(vertical > 0f ? -1 : 1);
        }

        private const float MenuRepeatDelay = 0.38f;
        private const float MenuRepeatRate = 0.14f;

        // A generic HID joystick has no semantic button names, only report order. The first eight
        // are the standard XInput order that Codex established and Stanley confirmed in play
        // (A, B, X, Y, LB, RB, Back, Start); these continue it. Tools > TheRedDoor >
        // Record Controller Presses prints the real numbers if a different controller disagrees.
        private const string JoystickDpadUp = "button12";
        private const string JoystickDpadDown = "button13";
        private const string JoystickStart = "button8";
        private const string JoystickBack = "button7";

        // Only stand in when a joystick is connected and the UI input module has nothing from that
        // device bound to Navigate. If the module's actions are ever repaired in the Inspector, the
        // joystick bindings in the UI map take over and this returns false, so the two can never
        // both move the selection on one press.
        private static bool JoystickMenuFallbackNeeded()
        {
            if (Joystick.all.Count == 0 || EventSystem.current == null)
                return false;

            var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
            InputAction move = module != null && module.move != null ? module.move.action : null;
            if (move == null)
                return true;

            var controls = move.controls;
            for (int i = 0; i < controls.Count; i++)
                if (controls[i].device is Joystick)
                    return false;
            return true;
        }

        // Returns +1 for up and -1 for down.
        //
        // The D-pad is checked first and deliberately: on a controller Unity does not recognise, the
        // D-pad comes through as four ordinary buttons, and a button has no polarity to get wrong.
        // The stick does. Unity's HID fallback cannot know which way a nameless Y axis points, so it
        // inverts it and hopes -- its own source says as much -- and on this receiver it guesses
        // wrong. That is why the menu scrolled backwards while walking left and right was fine:
        // vertical stick is the one axis nothing else in the game reads.
        private float JoystickVertical()
        {
            if (JoystickButtonHeld(JoystickDpadUp))
                return 1f;
            if (JoystickButtonHeld(JoystickDpadDown))
                return -1f;

            float value = 0f;
            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
            {
                var controls = sticks[i].allControls;
                for (int c = 0; c < controls.Count; c++)
                {
                    if (!(controls[c] is Vector2Control axis))
                        continue;
                    float y = axis.ReadValue().y;
                    if (Mathf.Abs(y) > Mathf.Abs(value))
                        value = y;
                }
            }
            return invertJoystickMenuVertical ? -value : value;
        }

        // Held, not pressed: navigation repeats while a direction is kept down.
        private static bool JoystickButtonHeld(string controlName)
        {
            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
            {
                ButtonControl button = sticks[i].TryGetChildControl<ButtonControl>(controlName);
                if (button != null && button.isPressed)
                    return true;
            }
            return false;
        }

        // The joystick half of SubmitPressed. Kept separate because a real Gamepad's A button is
        // already handled by the input module, and pressing it twice would fire the row twice.
        private static bool JoystickSubmitPressed()
        {
            var sticks = Joystick.all;
            for (int i = 0; i < sticks.Count; i++)
                if (sticks[i].trigger != null && sticks[i].trigger.wasPressedThisFrame)
                    return true;
            return false;
        }

        private void MoveSelection(int delta)
        {
            var panel = VisiblePanel();
            if (panel == null)
                return;
            var rows = panel.GetComponentsInChildren<Button>();
            if (rows.Length == 0)
                return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            int index = -1;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].gameObject == selected)
                    index = i;

            // Wraps, which is what a controller player expects from a short vertical list.
            index = index < 0 ? 0 : (index + delta + rows.Length) % rows.Length;
            EventSystem.current.SetSelectedGameObject(rows[index].gameObject);
            PlayHover();
        }

        // One gate for every code path that activates a row, so a button held across the branch
        // that establishes focus and the branch that fires the highlighted row can only count once.
        private bool Submit(GameObject row)
        {
            if (row == null || lastSubmitFrame == Time.frameCount)
                return false;
            lastSubmitFrame = Time.frameCount;
            ExecuteEvents.Execute(row, new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            return true;
        }

        private void SelectFirstRow()
        {
            if (EventSystem.current == null)
                return;
            var panel = VisiblePanel();
            var first = panel != null ? panel.GetComponentInChildren<Button>() : null;
            if (first != null)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        private GameObject VisiblePanel()
        {
            return current == Page.Title ? titlePanel
                : current == Page.Credits ? creditsPanel
                : current == Page.Pause ? pausePanel
                : current == Page.End ? endPanel : null;
        }

        private void Show(Page screen)
        {
            current = screen;
            if (titlePanel != null) titlePanel.SetActive(screen == Page.Title);
            if (creditsPanel != null) creditsPanel.SetActive(screen == Page.Credits);
            if (pausePanel != null) pausePanel.SetActive(screen == Page.Pause);
            if (endPanel != null) endPanel.SetActive(screen == Page.End);

            bool frozen = screen != Page.None;
            Time.timeScale = frozen ? 0f : 1f;
            // Time alone pauses gameplay. Do not cancel a live dash/zero its input when Esc opens.
            if (screen != Page.Pause && player != null && !(playerHealth != null && playerHealth.IsDead))
                player.SetControlsEnabled(!frozen);

            for (int i = 0; i < gameCanvases.Length; i++)
                if (gameCanvases[i] != null) gameCanvases[i].enabled = !frozen && canvasWasEnabled[i];
            GameObject visible = screen == Page.Title ? titlePanel
                : screen == Page.Credits ? creditsPanel
                : screen == Page.Pause ? pausePanel : endPanel;
            activeGroup = frozen && visible != null ? visible.GetComponent<CanvasGroup>() : null;
            reveal = screen == Page.Title ? 1f : 0f;
            if (activeGroup != null) activeGroup.alpha = reveal;

            SuppressGameMusic = screen == Page.Title ||
                (screen == Page.Credits && creditsReturn == Page.Title);
            MusicDuck = screen == Page.Pause ? 0.35f : 1f;

            if (titleMusic != null && titleMusic.clip != null)
            {
                bool wantsTitleMusic = screen == Page.Title ||
                    (screen == Page.Credits && creditsReturn == Page.Title);
                if (wantsTitleMusic && !titleMusic.isPlaying) titleMusic.Play();
                else if (!wantsTitleMusic && titleMusic.isPlaying) titleMusic.Stop();
            }

            ResetRows(visible);
        }

        // Nothing is pre-selected: a page opens with every row at rest, and the first arrow-key
        // press is what gives keyboard focus something to move from. Any stale hover left over from
        // a previous page is cleared here too, since a panel toggle never sends OnPointerExit.
        private void ResetRows(GameObject panel)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            if (panel == null)
                return;
            var rows = panel.GetComponentsInChildren<ButtonRowHighlight>(true);
            for (int i = 0; i < rows.Length; i++)
                rows[i].Clear();

            // A controller player opening a page should already be standing on the first row.
            if (InputDeviceHints.UsingGamepad)
                SelectFirstRow();
        }

        internal void PlayHover() => PlayClip(hoverClip);

        private void PlayClip(AudioClip clip)
        {
            if (uiSource != null && clip != null)
                uiSource.PlayOneShot(clip);
        }


        // ---------- construction ----------
        // Layout is in 1920x1080 reference pixels measured from the top left, identical to the
        // mockups these were designed against. Everything anchors top-left with a top-left pivot,
        // so anchoredPosition is literally (x, -y) and the C# matches the design one to one.

        private const float Margin = 190f;
        private const float EyebrowY = 300f, TitleY = 336f, RuleY = 524f, TagY = 558f;
        private const float BtnY0 = 682f, BtnY0NoTag = 616f, BtnW = 470f, BtnH = 70f, BtnStep = 92f;
        private const float FootY = 990f;

        private Sprite backdropSprite;

        private void BuildCanvas()
        {
            var go = new GameObject("Menu Canvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // Above both HUDs.
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            // Faces are already loaded in Awake, which runs before this.
            var tex = Resources.Load<Texture2D>("UI/Page_Backdrop");
            if (tex != null)
                backdropSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        // Places a child by top-left design coordinates.
        private static RectTransform At(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        private GameObject MakeScreen(string name, float backdropAlpha)
        {
            var root = NewUI(name, canvas.transform);
            Stretch(root);

            var back = NewUI("Backdrop", root.transform);
            Stretch(back);
            var backImage = back.AddComponent<Image>();
            if (backdropSprite != null)
            {
                backImage.sprite = backdropSprite;
                backImage.type = Image.Type.Simple;
                backImage.color = new Color(1f, 1f, 1f, backdropAlpha);
            }
            else
            {
                backImage.color = new Color(Ink.r, Ink.g, Ink.b, backdropAlpha);
            }

            // Ember spine down the left edge: the one piece of chrome that ties the pages together.
            var spine = NewUI("Spine", root.transform);
            var spineRect = (RectTransform)spine.transform;
            spineRect.anchorMin = new Vector2(0f, 0f);
            spineRect.anchorMax = new Vector2(0f, 1f);
            spineRect.pivot = new Vector2(0f, 0.5f);
            spineRect.sizeDelta = new Vector2(8f, 0f);
            spineRect.anchoredPosition = Vector2.zero;
            var spineImage = spine.AddComponent<Image>();
            spineImage.color = new Color(Ember.r, Ember.g, Ember.b, 0.15f);
            spineImage.raycastTarget = false;

            root.SetActive(false);
            return root;
        }

        private TMP_Text MakeText(Transform parent, string text, float x, float y, float w,
            float size, Color color, bool display, float tracking = 0f)
        {
            var go = NewUI("Text", parent);
            At(go, x, y, w, size * 1.6f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            var font = display ? headingFont : bodyFont;
            if (font != null)
                tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.characterSpacing = tracking;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void MakeRule(Transform parent, float y)
        {
            var go = NewUI("Rule", parent);
            At(go, Margin, y, 190f, 5f);
            var image = go.AddComponent<Image>();
            image.color = Ember;
            image.raycastTarget = false;
        }

        private Button MakeButton(Transform parent, string label, int index, bool hasTagline,
            System.Action action, float y0 = -1f, float step = -1f)
        {
            float baseY = y0 >= 0f ? y0 : (hasTagline ? BtnY0 : BtnY0NoTag);
            float y = baseY + index * (step > 0f ? step : BtnStep);
            var go = NewUI("Button " + label, parent);
            At(go, Margin, y, BtnW, BtnH);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.055f, 0.086f, 0.071f, 0.72f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.9f, 1.75f, 1.55f, 1f);
            // Selection must NOT tint the background. Unity keeps the selected object tinted until
            // something else is selected, which is what made BEGIN look permanently active.
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(1.2f, 0.85f, 0.6f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => action());

            // Leading marker, the part that actually reads as selection on a list-style page.
            var marker = NewUI("Marker", go.transform);
            At(marker, 0f, 0f, 4f, BtnH);
            var markerImage = marker.AddComponent<Image>();
            markerImage.color = new Color(0.157f, 0.212f, 0.173f, 1f);
            markerImage.raycastTarget = false;

            var text = MakeText(go.transform, label, 32f, 15f, BtnW - 60f, 32f, Muted, true, 6f);

            var hover = go.AddComponent<ButtonRowHighlight>();
            hover.Bind(this, markerImage, text, Ember, Bone, Muted,
                new Color(0.157f, 0.212f, 0.173f, 1f));
            return button;
        }

        private void BuildTitle()
        {
            titlePanel = MakeScreen("Title Page", 1f);
            var t = titlePanel.transform;
            MakeText(t, "THE RED DOOR", Margin, TitleY, 1400f, 128f, Bone, true, 5f);
            MakeRule(t, RuleY);
            MakeText(t, "Something old keeps the way. It has kept it a long time.", Margin, TagY, 1100f, 29f, Muted, false);
            MakeButton(t, "BEGIN", 0, true, Begin);
            MakeButton(t, "CREDITS", 1, true, OpenCredits);
#if !UNITY_WEBGL
            MakeButton(t, "QUIT TO DESKTOP", 2, true, Quit);
#endif
            var tag = MakeText(t, "DEMO BUILD", 0f, FootY, 1920f - Margin, 20f,
                new Color(Accent.r, Accent.g, Accent.b, 0.75f), false, 6f);
            tag.alignment = TextAlignmentOptions.TopRight;
        }

        private void BuildCredits()
        {
            creditsPanel = MakeScreen("Credits Page", 1f);
            var t = creditsPanel.transform;
            MakeText(t, "WITH THANKS TO", Margin, EyebrowY, 1000f, 21f, Muted, false, 28f);
            MakeText(t, "CREDITS", Margin, TitleY, 1000f, 96f, Bone, true, 5f);
            MakeRule(t, RuleY);

            const float Col2 = Margin + 780f;
            const float Head = 570f, Row0 = 610f, Row = 34f;

            MakeText(t, "ART & ASSETS", Margin, Head, 700f, 21f, Ember, false, 24f);
            string[] art =
            {
                "Mossy Cavern \u2014 Maaot",
                "2D Hand-Drawn Player \u2014 ForsakenVoid",
                "Moss Guardian \u2014 colingx",
                "Leaf & Branch UI \u2014 Coarsecurve / A. Moseley",
                "Forest Green UI Pack \u2014 Gamified soul",
                "Hand Painted Platformer, Dungeon \u2014 oleekconder",
            };
            for (int i = 0; i < art.Length; i++)
                MakeText(t, art[i], Margin, Row0 + i * Row, 740f, 23f, Muted, false);

            MakeText(t, "AUDIO", Col2, Head, 700f, 21f, Ember, false, 24f);
            string[] audio =
            {
                "Sound effects \u2014 Kenney (CC0)",
                "Forest ambience \u2014 TinyWorlds (OpenGameArt, CC0)",
                "Exploration & ending themes \u2014 Samza (OpenGameArt, CC0)",
            };
            for (int i = 0; i < audio.Length; i++)
                MakeText(t, audio[i], Col2, Row0 + i * Row, 740f, 23f, Muted, false);

            MakeText(t, "MADE BY", Col2, Row0 + 5 * Row, 700f, 21f, Ember, false, 24f);
            MakeText(t, "Stanley Pratama Teguh", Col2, Row0 + 6.1f * Row, 740f, 23f, Bone, false);
            MakeText(t, "Made with Unity", Col2, Row0 + 7.1f * Row, 740f, 23f, Muted, false);

            MakeButton(t, "BACK", 0, true, CreditsBack, 900f, 84f);
        }

        private void BuildPause()
        {
            // Keeps the backdrop slightly transparent: the forest stays faintly visible, which is
            // what tells the player the world is still there and merely on hold.
            pausePanel = MakeScreen("Pause Page", 0.9f);
            var t = pausePanel.transform;
            MakeText(t, "THE FOREST WAITS", Margin, EyebrowY, 1000f, 21f, Muted, false, 28f);
            MakeText(t, "PAUSED", Margin, TitleY, 1000f, 128f, Bone, true, 5f);
            MakeRule(t, RuleY);
            MakeButton(t, "RESUME", 0, false, Resume);
            MakeButton(t, "MAIN MENU", 1, false, MainMenu);
#if !UNITY_WEBGL
            MakeButton(t, "QUIT TO DESKTOP", 2, false, Quit);
#endif
        }

        private void BuildEnd()
        {
            endPanel = MakeScreen("End Page", 1f);
            var t = endPanel.transform;
            MakeText(t, "THE KEEPER IS STILL", Margin, EyebrowY, 1000f, 21f, Muted, false, 28f);
            MakeText(t, "END OF DEMO", Margin, TitleY, 1400f, 128f, Ember, true, 5f);
            MakeRule(t, RuleY);
            MakeText(t, "The door stands open. What lies beyond it is not part of this demo.",
                Margin, TagY, 1200f, 29f, Muted, false);
            MakeButton(t, "KEEP LOOKING AROUND", 0, true, KeepExploring, 636f, 84f);
            MakeButton(t, "CREDITS", 1, true, OpenCredits, 636f, 84f);
            MakeButton(t, "MAIN MENU", 2, true, MainMenu, 636f, 84f);
#if !UNITY_WEBGL
            MakeButton(t, "QUIT TO DESKTOP", 3, true, Quit, 636f, 84f);
#endif
            var tag = MakeText(t, "THANKS FOR PLAYING", 0f, FootY, 1920f - Margin, 20f,
                new Color(Accent.r, Accent.g, Accent.b, 0.8f), false, 6f);
            tag.alignment = TextAlignmentOptions.TopRight;
        }

        // ---------- actions ----------

        // The run counts as started here, so dying reloads straight back into the arena instead of
        // replaying the title on every attempt. The opening story lives in the hollow itself now.
        private void Begin()
        {
            PlayClip(clickClip);
            showTitleOnLoad = false;
            Show(Page.None);
        }

        private void Resume()
        {
            PlayClip(backClip);
            Show(Page.None);
        }

        private void KeepExploring()
        {
            PlayClip(backClip);
            Show(Page.None);
        }

        private void MainMenu()
        {
            if (leaving) return;
            leaving = true;
            if (respawn != null) respawn.enabled = false;
            PlayClip(clickClip);
            showTitleOnLoad = true;
            Time.timeScale = 1f;
            string path = gameObject.scene.path;
            if (string.IsNullOrEmpty(path) || !Application.CanStreamedLevelBeLoaded(path))
                path = SceneManager.GetActiveScene().path;
            SceneManager.LoadScene(path, LoadSceneMode.Single);
        }

        private void OpenCredits()
        {
            PlayClip(clickClip);
            creditsReturn = current == Page.End ? Page.End : Page.Title;
            Show(Page.Credits);
        }

        private void CreditsBack()
        {
            PlayClip(backClip);
            Show(creditsReturn);
        }

#if !UNITY_WEBGL
        // No desktop to quit to in a browser tab, so this and its three rows are compiled out
        // for the web build.
        private void Quit()
        {
            PlayClip(backClip);
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
#endif

        private void OnDestroy()
        {
            OwnsEnding = false;
            SuppressGameMusic = false;
            MusicDuck = 1f;
            Time.timeScale = 1f;
            if (gameCanvases == null) return;
            for (int i = 0; i < gameCanvases.Length; i++)
                if (gameCanvases[i] != null) gameCanvases[i].enabled = canvasWasEnabled[i];
        }
    }

    // Selection feedback for a list-style row: the leading marker lights and the label lifts from
    // muted to bone, on mouse hover and on keyboard focus alike. Button's own ColorTint only
    // touches the background, which on a dark page is not enough to read as "this one".
    [DisallowMultipleComponent]
    public sealed class ButtonRowHighlight : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private GameFlowUI owner;
        private Image marker;
        private TMP_Text label;
        private Color markerHot, markerCold, labelHot, labelCold;
        private bool hovered, focused;

        internal void Bind(GameFlowUI ui, Image markerImage, TMP_Text text,
            Color hotMarker, Color hotLabel, Color coldLabel, Color coldMarker)
        {
            owner = ui;
            marker = markerImage;
            label = text;
            markerHot = hotMarker;
            markerCold = coldMarker;
            labelHot = hotLabel;
            labelCold = coldLabel;
            Apply();
        }

        private void OnDisable() => Clear();

        internal void Clear()
        {
            hovered = false;
            focused = false;
            Apply();
        }

        private void Apply()
        {
            bool hot = hovered || focused;
            if (marker != null) marker.color = hot ? markerHot : markerCold;
            if (label != null) label.color = hot ? labelHot : labelCold;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hovered) return;
            hovered = true;
            Apply();
            if (owner != null) owner.PlayHover();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Apply();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (focused) return;
            focused = true;
            Apply();
            if (owner != null) owner.PlayHover();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            focused = false;
            Apply();
        }
    }
}
