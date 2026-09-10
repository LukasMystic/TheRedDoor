using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// Diagnostic only. Codex's ControllerSupportChecks validates that bindings exist against a
// synthetic Gamepad; this one reports what the real controller plugged into this Mac actually
// says about itself, which is the only way to settle control names and axis polarity.
public static class ControllerDeviceDump
{
    [MenuItem("Tools/TheRedDoor/Dump Controller Layout")]
    private static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== all devices ===");
        foreach (InputDevice device in InputSystem.devices)
        {
            sb.AppendLine($"{device.name} | layout={device.layout} | type={device.GetType().Name} " +
                $"| iface={device.description.interfaceName} | product={device.description.product} " +
                $"| manufacturer={device.description.manufacturer}");
        }

        foreach (InputDevice device in InputSystem.devices)
        {
            if (!(device is Gamepad) && !(device is Joystick))
                continue;

            sb.AppendLine();
            sb.AppendLine($"=== controls on {device.name} [{device.layout}] ===");
            foreach (InputControl control in device.allControls)
            {
                string value;
                try
                {
                    object read = control.ReadValueAsObject();
                    value = read == null ? "null" : read.ToString();
                }
                catch (Exception error)
                {
                    value = "err:" + error.GetType().Name;
                }

                sb.AppendLine($"  {control.path} | layout={control.layout} | type={control.GetType().Name} " +
                    $"| usages=[{string.Join(",", control.usages.Select(u => u.ToString()))}] | value={value}");
            }

            sb.AppendLine();
            sb.AppendLine($"--- generated layout '{device.layout}' (parameters decide axis polarity) ---");
            try
            {
                var layout = UnityEngine.InputSystem.InputSystem.LoadLayout(device.layout);
                foreach (var item in layout.controls)
                {
                    sb.AppendLine($"  {item.name} | layout={item.layout} " +
                        $"| params=[{string.Join(",", item.parameters.Select(p => p.ToString()))}] " +
                        $"| processors=[{string.Join(",", item.processors.Select(p => p.ToString()))}] " +
                        $"| default={item.defaultState} " +
                        $"| usages=[{string.Join(",", item.usages.Select(u => u.ToString()))}]");
                }
            }
            catch (Exception error)
            {
                sb.AppendLine("  layout load failed: " + error);
            }
        }

        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "controller-dump.txt"));
        File.WriteAllText(path, sb.ToString());
        Debug.Log("Controller dump written to " + path);
    }
}
