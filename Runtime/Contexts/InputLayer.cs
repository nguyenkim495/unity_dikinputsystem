using System;

namespace Dik.Input
{
    /// <summary>
    /// The category a mapping context belongs to. A player's mode decides which layers may receive input.
    /// </summary>
    [Flags]
    public enum InputLayer
    {
        None = 0,
        /// <summary>Character control: move, attack, interact...</summary>
        Gameplay = 1 << 0,
        /// <summary>Menu navigation: navigate, submit, cancel, point...</summary>
        UI = 1 << 1,
        /// <summary>Always available while input is enabled: pause, screenshot, chat toggle...</summary>
        System = 1 << 2,
        Custom1 = 1 << 3,
        Custom2 = 1 << 4,
        Custom3 = 1 << 5,
        Custom4 = 1 << 6,
        All = ~0
    }

    /// <summary>
    /// Presets for the allowed layers, equivalent to UE5 SetInputMode (GameOnly / UIOnly / GameAndUI).
    /// </summary>
    public enum InputMode
    {
        /// <summary>Everything except UI.</summary>
        GameOnly,
        /// <summary>UI and System only. Gameplay actions are disabled (and release any held values).</summary>
        UIOnly,
        /// <summary>All layers.</summary>
        GameAndUI,
        /// <summary>Nothing.</summary>
        None
    }

    public static class InputModeExtensions
    {
        public static InputLayer ToLayers(this InputMode mode)
        {
            switch (mode)
            {
                case InputMode.GameOnly: return InputLayer.All & ~InputLayer.UI;
                case InputMode.UIOnly: return InputLayer.UI | InputLayer.System;
                case InputMode.GameAndUI: return InputLayer.All;
                default: return InputLayer.None;
            }
        }
    }
}
