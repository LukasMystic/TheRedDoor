using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Diagnostic only. A controller Unity has no layout for reports its buttons by position in the HID
// report, not by name, so "which button is Start" and "which way does the stick's Y point" cannot
// be read off the layout. This records what actually moves when a real controller is used.
public static class ControllerPressRecorder
{
    private const double Duration = 30.0;

    private static readonly List<string> lines = new List<string>();
    private static readonly Dictionary<InputControl, float> lastValue = new Dictionary<InputControl, float>();
    private static double startTime;
    private static bool running;

    [MenuItem("Tools/TheRedDoor/Record Controller Presses (30s)")]
    private static void Record()
    {
        lines.Clear();
        lastValue.Clear();
        startTime = EditorApplication.timeSinceStartup;
        running = true;

        // Seed every control with its resting value so the first sample is not reported as a change.
        Each((device, axis) => lastValue[axis] = axis.ReadValue());

        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;

        Debug.Log("RECORDING controller input for 30 seconds. Keep Unity focused and press, " +
            "pausing about a second between each: D-pad Up, D-pad Down, D-pad Left, D-pad Right, " +
            "Start, Back. Then hold the left stick UP for two seconds, then DOWN for two seconds. " +
            "Result is written to controller-presses.txt in the project root.");
    }

    private static void Tick()
    {
        if (!running)
            return;

        double elapsed = EditorApplication.timeSinceStartup - startTime;
        if (elapsed > Duration)
        {
            Stop();
            return;
        }

        Each((device, axis) =>
        {
            float value = axis.ReadValue();
            lastValue.TryGetValue(axis, out float previous);
            // 0.4 keeps stick jitter and resting-trigger noise out of the log.
            if (Mathf.Abs(value - previous) > 0.4f)
            {
                lines.Add($"{elapsed,6:0.00}s  {device.name} / {axis.name,-12} " +
                    $"{previous,6:0.00} -> {value,6:0.00}");
            }
            lastValue[axis] = value;
        });
    }

    private static void Stop()
    {
        running = false;
        EditorApplication.update -= Tick;

        var text = new StringBuilder();
        text.AppendLine("Order asked for: D-pad Up, Down, Left, Right, Start, Back, " +
            "then stick held UP, then stick held DOWN.");
        text.AppendLine("Stick lines name stick/y: a positive value while the stick is held UP means " +
            "Unity's polarity is right for this controller and invertJoystickMenuVertical " +
            "should be off.");
        text.AppendLine();
        if (lines.Count == 0)
            text.AppendLine("(nothing recorded -- controller asleep, or Unity was not the focused app)");
        foreach (string line in lines)
            text.AppendLine(line);

        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "controller-presses.txt"));
        File.WriteAllText(path, text.ToString());
        Debug.Log($"RECORDING DONE: {lines.Count} changes written to {path}");
    }

    // ButtonControl derives from AxisControl, so one pass covers buttons, triggers and stick axes,
    // and skips the Vector2 parents whose children are already listed.
    private static void Each(System.Action<InputDevice, AxisControl> visit)
    {
        foreach (InputDevice device in InputSystem.devices)
        {
            if (!(device is Joystick) && !(device is Gamepad))
                continue;
            var controls = device.allControls;
            for (int i = 0; i < controls.Count; i++)
                if (controls[i] is AxisControl axis)
                    visit(device, axis);
        }
    }
}
