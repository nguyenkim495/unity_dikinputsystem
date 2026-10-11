using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Dik.Input
{
    public enum DeviceAutoPairPolicy
    {
        /// <summary>Only Unity's built-in behaviour (re-pairs the same device when it reconnects).</summary>
        Off,
        /// <summary>
        /// A player whose device was unplugged automatically takes the next unpaired device that is plugged in
        /// or used (pressing a key or button), with no extra code.
        /// </summary>
        ReplaceLostDevices
    }

    /// <summary>
    /// Fills gaps Unity leaves when devices are plugged or unplugged:
    /// - Single player: PlayerInput already switches schemes automatically between keyboard and gamepad;
    ///   DikInputPlayer makes the switch smooth (releases held input, re-applies contexts, keeps rebinds).
    /// - Multiplayer: Unity only re-pairs the exact same device. This service gives a player who lost their
    ///   device any free device the moment it is connected or touched.
    /// </summary>
    public static class DeviceAutoPairing
    {
        static DeviceAutoPairPolicy s_Policy = DeviceAutoPairPolicy.ReplaceLostDevices;
        static bool s_Listening;

        public static DeviceAutoPairPolicy Policy
        {
            get => s_Policy;
            set
            {
                s_Policy = value;
                Refresh();
            }
        }

        /// <summary>
        /// Tries to give <paramref name="device"/> to the first enabled player that is missing a device.
        /// </summary>
        public static bool TryAssign(InputDevice device)
        {
            if (s_Policy == DeviceAutoPairPolicy.Off || device == null || !device.added)
                return false;
            if (InputUser.FindUserPairedToDevice(device) != null)
                return false;

            var players = DikInput.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player.isActiveAndEnabled && player.NeedsDevice && player.TryPairDevice(device))
                {
                    Refresh();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Starts or stops listening for unpaired device activity, depending on whether any player needs a device.</summary>
        internal static void Refresh()
        {
            var needsListening = false;
            if (s_Policy != DeviceAutoPairPolicy.Off)
            {
                var players = DikInput.Players;
                for (var i = 0; i < players.Count && !needsListening; i++)
                    needsListening = players[i].isActiveAndEnabled && players[i].NeedsDevice;
            }

            if (needsListening == s_Listening)
                return;

            s_Listening = needsListening;
            if (needsListening)
                ++InputUser.listenForUnpairedDeviceActivity;
            else
                InputUser.listenForUnpairedDeviceActivity = Mathf.Max(0, InputUser.listenForUnpairedDeviceActivity - 1);
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // "Reconnected" (same device back) is handled by Unity's InputUser re-pairing.
            if (change == InputDeviceChange.Added)
                TryAssign(device);
        }

        static void OnUnpairedDeviceUsed(InputControl control, UnityEngine.InputSystem.LowLevel.InputEventPtr eventPtr)
        {
            if (s_Listening && control != null)
                TryAssign(control.device);
        }

        static void OnPlayersChanged(DikInputPlayer player) => Refresh();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            s_Listening = false;
            s_Policy = DeviceAutoPairPolicy.ReplaceLostDevices;

            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;

            // DikInput resets its events at SubsystemRegistration, which runs before this.
            DikInput.PlayerAdded += OnPlayersChanged;
            DikInput.PlayerRemoved += OnPlayersChanged;
        }
    }
}
