using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    public enum DeviceKind
    {
        None,
        KeyboardMouse,
        Gamepad,
        Joystick,
        Touch,
        XR,
        Other
    }

    public enum GamepadFamily
    {
        None,
        Xbox,
        PlayStation,
        Switch,
        Generic
    }

    /// <summary>
    /// Classifies devices by type and layout inheritance instead of comparing device-name strings,
    /// so it keeps working across platforms, controller revisions and Input System versions.
    /// </summary>
    public static class DeviceClassifier
    {
        public static DeviceKind GetKind(InputDevice device)
        {
            switch (device)
            {
                case null: return DeviceKind.None;
                case Keyboard _:
                case Mouse _: return DeviceKind.KeyboardMouse;
                case Gamepad _: return DeviceKind.Gamepad;
                case Joystick _: return DeviceKind.Joystick;
                case Touchscreen _:
                case Pen _: return DeviceKind.Touch;
                case TrackedDevice _: return DeviceKind.XR;
                default: return DeviceKind.Other;
            }
        }

        public static GamepadFamily GetGamepadFamily(InputDevice device)
        {
            if (!(device is Gamepad))
                return GamepadFamily.None;
            if (IsBasedOn(device, "XInputController"))
                return GamepadFamily.Xbox;
            if (IsBasedOn(device, "DualShockGamepad"))
                return GamepadFamily.PlayStation;
            if (IsBasedOn(device, "SwitchProControllerHID"))
                return GamepadFamily.Switch;
            return GamepadFamily.Generic;
        }

        /// <summary>
        /// The device that represents the player: a gamepad, keyboard, joystick or touchscreen before a mouse or anything else.
        /// </summary>
        public static InputDevice GetPrimaryDevice(IReadOnlyList<InputDevice> devices)
        {
            if (devices == null)
                return null;

            InputDevice fallback = null;
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (device is Gamepad || device is Keyboard || device is Joystick || device is Touchscreen)
                    return device;
                if (fallback == null)
                    fallback = device;
            }
            return fallback;
        }

        public static bool IsBasedOn(InputDevice device, string layout)
        {
            if (device == null || string.IsNullOrEmpty(layout))
                return false;
            try
            {
                return InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout);
            }
            catch (System.ArgumentException)
            {
                // Layout not registered on this platform.
                return false;
            }
        }
    }
}
