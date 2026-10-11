using Dik.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Dik.Input.Samples
{
    /// <summary>
    /// Pause flow from the InputSystem Warriors sample, rewritten on this package:
    /// - the player who pressed Pause gets the UI (UIOnly mode and owns the UI module);
    /// - every other player's input is disabled;
    /// - gameplay values are released automatically (no stuck movement after unpausing).
    ///
    /// Setup: put this on each player next to DikInputPlayer. The TogglePause action should live in a
    /// System-layer context so it works in both GameOnly and UIOnly modes.
    /// </summary>
    [RequireComponent(typeof(DikInputPlayer), typeof(DikInputReceiver))]
    public sealed class SamplePauseFlow : MonoBehaviour
    {
        static DikInputPlayer s_FocusedPlayer;

        [SerializeField] InputActionReference m_TogglePause;
        [SerializeField] InputSystemUIInputModule m_SharedUIModule;
        [SerializeField] GameObject m_PauseMenu;

        DikInputPlayer m_Player;
        InputBindingHandle m_Binding;

        void Awake() => m_Player = GetComponent<DikInputPlayer>();

        void OnEnable() => m_Binding = GetComponent<DikInputReceiver>().Bind(m_TogglePause, InputTrigger.Started, _ => Toggle());

        void OnDisable() => m_Binding.Dispose();

        void Toggle()
        {
            var pausing = s_FocusedPlayer == null;
            if (!pausing && s_FocusedPlayer != m_Player)
                return; // Only the player who paused can unpause.

            s_FocusedPlayer = pausing ? m_Player : null;
            Time.timeScale = pausing ? 0f : 1f;

            DikInput.SetInputEnabledForAll(!pausing, except: m_Player);
            m_Player.SetMode(pausing ? InputMode.UIOnly : InputMode.GameOnly);

            if (pausing && m_SharedUIModule != null)
                m_Player.UIInputModule = m_SharedUIModule;

            if (m_PauseMenu != null)
                m_PauseMenu.SetActive(pausing);
        }
    }
}
