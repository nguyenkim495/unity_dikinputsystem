using Dik.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Dik.Input.Samples
{
    /// <summary>
    /// Updates a player's device label and color on hot-swap and unplug, using DeviceDisplayProfile
    /// (classified by device type and layout, not by name strings).
    /// </summary>
    public sealed class SampleDeviceDisplay : MonoBehaviour
    {
        [SerializeField] DikInputPlayer m_Player;
        [SerializeField] DeviceDisplayProfile m_Profile;
        [SerializeField] Text m_Label;
        [SerializeField] Graphic m_Tint;

        void OnEnable()
        {
            if (m_Player == null)
                return;
            m_Player.ControlsChanged += Refresh;
            m_Player.DeviceLost += Refresh;
            m_Player.DeviceRegained += Refresh;
            Refresh(m_Player);
        }

        void OnDisable()
        {
            if (m_Player == null)
                return;
            m_Player.ControlsChanged -= Refresh;
            m_Player.DeviceLost -= Refresh;
            m_Player.DeviceRegained -= Refresh;
        }

        void Refresh(DikInputPlayer player)
        {
            var style = m_Profile != null ? m_Profile.GetStyle(player) : null;
            var text = style != null ? style.displayName : $"{player.CurrentDeviceKind} {player.CurrentGamepadFamily}";
            if (m_Label != null)
                m_Label.text = $"P{player.PlayerIndex + 1}: {text}";
            if (m_Tint != null && style != null)
                m_Tint.color = style.color;
        }
    }
}
