using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// Name, color and button icons per device kind/family. Icons are looked up by control path
    /// ("buttonSouth", "leftTrigger", "space", "leftButton"...), the stable identifier the Input System returns from
    /// GetBindingDisplayString. They are not looked up by localized display text.
    /// </summary>
    [CreateAssetMenu(fileName = "DeviceDisplayProfile", menuName = "Dik/Input/Device Display Profile", order = 20)]
    public sealed class DeviceDisplayProfile : ScriptableObject
    {
        [Serializable]
        public struct ControlIcon
        {
            [Tooltip("Control path without device, e.g. buttonSouth, leftTrigger, dpad/up, space, leftButton.")]
            public string controlPath;
            public Sprite icon;
        }

        [Serializable]
        public sealed class DeviceStyle
        {
            public string displayName = "Device";
            public DeviceKind kind = DeviceKind.Gamepad;
            [Tooltip("Leave None/Generic to match any gamepad family.")]
            public GamepadFamily family = GamepadFamily.None;
            public Color color = Color.white;
            public List<ControlIcon> icons = new List<ControlIcon>();

            public Sprite FindIcon(string controlPath)
            {
                if (string.IsNullOrEmpty(controlPath))
                    return null;
                for (var i = 0; i < icons.Count; i++)
                {
                    if (string.Equals(icons[i].controlPath, controlPath, StringComparison.OrdinalIgnoreCase))
                        return icons[i].icon;
                }
                return null;
            }
        }

        [SerializeField] List<DeviceStyle> m_Styles = new List<DeviceStyle>();
        [SerializeField] DeviceStyle m_Disconnected = new DeviceStyle { displayName = "Disconnected", kind = DeviceKind.None, color = Color.gray };

        public IReadOnlyList<DeviceStyle> Styles => m_Styles;
        public DeviceStyle Disconnected => m_Disconnected;

        public DeviceStyle GetStyle(DeviceKind kind, GamepadFamily family)
        {
            if (kind == DeviceKind.None)
                return m_Disconnected;

            DeviceStyle kindMatch = null;
            for (var i = 0; i < m_Styles.Count; i++)
            {
                var style = m_Styles[i];
                if (style == null || style.kind != kind)
                    continue;
                if (style.family == family && family != GamepadFamily.None)
                    return style;
                if (kindMatch == null && (style.family == GamepadFamily.None || style.family == GamepadFamily.Generic))
                    kindMatch = style;
            }
            return kindMatch;
        }

        public DeviceStyle GetStyle(DikInputPlayer player)
        {
            if (player == null || player.NeedsDevice)
                return m_Disconnected;
            return GetStyle(player.CurrentDeviceKind, player.CurrentGamepadFamily);
        }

        /// <summary>
        /// Icon for a binding as shown to this player, or null to fall back to text
        /// (see <see cref="DikRebind.GetDisplayString"/>).
        /// </summary>
        public Sprite GetIcon(DikInputPlayer player, InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return null;
            action.GetBindingDisplayString(bindingIndex, out _, out var controlPath);
            return GetStyle(player)?.FindIcon(controlPath);
        }
    }
}
