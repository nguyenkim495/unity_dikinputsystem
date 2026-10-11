using Dik.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dik.Input.Samples
{
    /// <summary>
    /// A rebind row: shows the binding for the player's current scheme (icon if the profile has one, text otherwise)
    /// and rebinds it on click. Composites (WASD) rebind part by part. Escape or Start cancels. Overrides are saved.
    /// </summary>
    public sealed class SampleRebindButton : MonoBehaviour
    {
        [SerializeField] DikInputPlayer m_Player;
        [SerializeField] InputActionReference m_Action;
        [SerializeField] DeviceDisplayProfile m_Profile;
        [SerializeField] Text m_BindingText;
        [SerializeField] Image m_BindingIcon;
        [SerializeField] Button m_RebindButton;
        [SerializeField] Button m_ResetButton;

        RebindSession m_Session;

        void OnEnable()
        {
            m_RebindButton.onClick.AddListener(StartRebind);
            if (m_ResetButton != null)
                m_ResetButton.onClick.AddListener(ResetBinding);
            m_Player.ControlsChanged += OnControlsChanged;
            Refresh();
        }

        void OnDisable()
        {
            m_RebindButton.onClick.RemoveListener(StartRebind);
            if (m_ResetButton != null)
                m_ResetButton.onClick.RemoveListener(ResetBinding);
            m_Player.ControlsChanged -= OnControlsChanged;
            m_Session?.Cancel();
        }

        void OnControlsChanged(DikInputPlayer _) => Refresh();

        void StartRebind()
        {
            m_Session?.Cancel();
            m_BindingText.text = "Press a button... (Esc/Start to cancel)";
            m_Session = m_Player.StartRebind(m_Action, onFinished: _ =>
            {
                m_Session = null;
                Refresh();
            });
            if (m_Session == null)
                Refresh();
        }

        void ResetBinding()
        {
            var action = m_Player.ResolveAction(m_Action);
            DikRebind.ResetBinding(action, DikRebind.FindBindingIndex(action, m_Player.CurrentControlScheme));
            m_Player.SaveBindingOverrides();
            Refresh();
        }

        void Refresh()
        {
            var action = m_Player.ResolveAction(m_Action);
            var index = DikRebind.FindBindingIndex(action, m_Player.CurrentControlScheme);
            var icon = m_Profile != null ? m_Profile.GetIcon(m_Player, action, index) : null;

            if (m_BindingIcon != null)
            {
                m_BindingIcon.enabled = icon != null;
                m_BindingIcon.sprite = icon;
            }
            m_BindingText.enabled = icon == null;
            m_BindingText.text = DikRebind.GetDisplayString(action, index);
        }
    }
}
