using System;
using System.Linq;
using TheRedDoor.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

// Binding-resolution check for the one actions asset actually assigned to Player.prefab.
// A generic Gamepad is intentional: Xbox, PlayStation and Switch layouts all derive from it,
// so a binding resolved here is shared by every controller family rather than one vendor.
public static class ControllerSupportChecks
{
    private const string ActionsPath = "Assets/InputSystem_Action.inputactions";

    [MenuItem("Tools/TheRedDoor/Verify Controller Bindings")]
    private static void Verify()
    {
        LogConnectedControllers();

        InputActionAsset source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        if (source == null)
            throw new Exception("CONTROLLER FAIL: Missing " + ActionsPath);

        InputActionAsset actions = UnityEngine.Object.Instantiate(source);
        Gamepad pad = null;
        Joystick joystick = null;
        try
        {
            pad = InputSystem.AddDevice<Gamepad>();
            joystick = InputSystem.AddDevice<Joystick>();
            actions.Enable();

            Check(actions.controlSchemes.Count == 0,
                "Keyboard and controller stay live together (no pairing-only control scheme)");
            CheckControls(actions, pad, "Player/Move", "leftStick", "dpad");
            CheckControls(actions, pad, "Player/Jump", "buttonSouth");
            CheckControls(actions, pad, "Player/Sprint", "rightShoulder", "leftStickPress");
            CheckControls(actions, pad, "Player/Attack", "buttonWest", "rightTrigger");
            CheckControls(actions, pad, "Player/Interact", "buttonNorth");
            // A 2DVector composite resolves into its four child axes, so validate the authored
            // stick path and the directly resolved D-pad separately.
            CheckBinding(actions, "UI/Navigate", "<Gamepad>/leftStick/up");
            CheckControls(actions, pad, "UI/Navigate", "dpad");
            CheckControls(actions, pad, "UI/Submit", "buttonSouth");
            CheckControls(actions, pad, "UI/Cancel", "buttonEast");

            // Controllers which macOS exposes as a generic Joystick use the conventional HID
            // button order rather than Gamepad's semantic names.
            CheckBinding(actions, "Player/Move", "<Joystick>/{Hatswitch}");
            CheckBinding(actions, "Player/Jump", "<Joystick>/trigger");
            CheckBinding(actions, "Player/Sprint", "<Joystick>/button6");
            CheckBinding(actions, "Player/Attack", "<Joystick>/button3");
            CheckBinding(actions, "Player/Interact", "<Joystick>/button4");
            CheckBinding(actions, "UI/Navigate", "<Joystick>/{Hatswitch}");
            CheckCompositePart(actions, "UI/Navigate", "up", "<Joystick>/stick/up");
            CheckCompositePart(actions, "UI/Navigate", "down", "<Joystick>/stick/down");
            CheckBinding(actions, "UI/Submit", "*/{Submit}");
            CheckBinding(actions, "UI/Cancel", "<Joystick>/button2");

            // Exercise the actual UI action value, not only its authored paths. Unity's stick
            // child controls define up as positive Y and down as negative Y; this catches an
            // accidental inversion processor or a reversed composite before it ships.
            CheckDirection(joystick.stick, Vector2.up, "Generic joystick up is not inverted");
            CheckDirection(joystick.stick, Vector2.down, "Generic joystick down is not inverted");
            CheckDpadDirection(pad, GamepadButton.DpadUp, Vector2.up,
                "Gamepad D-pad up navigates up");
            CheckDpadDirection(pad, GamepadButton.DpadDown, Vector2.down,
                "Gamepad D-pad down navigates down");

            string hints = InputDeviceHints.Format(
                "{move}|{jump}|{dash}|{attack}|{interact}|{pause}");
            Check(!hints.Contains("{") && hints.Split('|').All(label => !string.IsNullOrWhiteSpace(label)),
                "Controller tutorial and door prompts resolve every button token");

            Debug.Log("CONTROLLER CHECKS COMPLETE: generic Gamepad bindings cover Xbox, " +
                "PlayStation, Switch Pro and compatible controllers. Physical wireless pairing " +
                "and platform drivers still require a real-device smoke test.");
        }
        finally
        {
            actions.Disable();
            UnityEngine.Object.DestroyImmediate(actions);
            if (pad != null && pad.added)
                InputSystem.RemoveDevice(pad);
            if (joystick != null && joystick.added)
                InputSystem.RemoveDevice(joystick);
        }
    }

    private static void LogConnectedControllers()
    {
        foreach (InputDevice device in InputSystem.devices)
        {
            if (!(device is Gamepad) && !(device is Joystick))
                continue;

            string buttons = string.Join(", ", device.allControls
                .OfType<ButtonControl>().Select(control => control.name));
            Debug.Log($"CONTROLLER DEVICE: {device.displayName} [{device.layout}] " +
                $"interface={device.description.interfaceName}; buttons={buttons}");
        }
    }

    private static void CheckControls(InputActionAsset actions, Gamepad pad, string actionPath,
        params string[] requiredControls)
    {
        InputAction action = actions.FindAction(actionPath, true);
        string[] resolved = action.controls.Where(control => control.device == pad)
            .Select(control => control.name).ToArray();
        for (int i = 0; i < requiredControls.Length; i++)
            Check(resolved.Contains(requiredControls[i]),
                actionPath + " resolves " + requiredControls[i]);
    }

    private static void CheckBinding(InputActionAsset actions, string actionPath, string bindingPath)
    {
        InputAction action = actions.FindAction(actionPath, true);
        Check(action.bindings.Any(binding => binding.path == bindingPath),
            actionPath + " binds " + bindingPath);
    }

    private static void CheckCompositePart(InputActionAsset actions, string actionPath,
        string partName, string bindingPath)
    {
        InputAction action = actions.FindAction(actionPath, true);
        Check(action.bindings.Any(binding => binding.isPartOfComposite &&
                binding.name == partName && binding.path == bindingPath),
            actionPath + " maps " + bindingPath + " to " + partName);
    }

    private static void CheckDirection(Vector2Control control, Vector2 input, string description)
    {
        InputSystem.QueueDeltaStateEvent(control, input);
        InputSystem.Update();
        Vector2 value = control.ReadValue();
        Check(Vector2.Dot(value.normalized, input.normalized) > 0.99f,
            $"{description} (control={value})");

        InputSystem.QueueDeltaStateEvent(control, Vector2.zero);
        InputSystem.Update();
    }

    private static void CheckDpadDirection(Gamepad pad, GamepadButton button,
        Vector2 expected, string description)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        InputSystem.Update();
        Vector2 value = pad.dpad.ReadValue();
        Check(Vector2.Dot(value.normalized, expected.normalized) > 0.99f,
            $"{description} (control={value})");

        InputSystem.QueueStateEvent(pad, new GamepadState());
        InputSystem.Update();
    }

    private static void Check(bool result, string description)
    {
        if (!result)
            throw new Exception("CONTROLLER FAIL: " + description);
        Debug.Log("CONTROLLER PASS: " + description);
    }
}
