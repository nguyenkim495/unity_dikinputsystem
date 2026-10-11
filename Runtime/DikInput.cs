using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dik.Input
{
    /// <summary>
    /// How this game instance routes Replicated input commands.
    /// </summary>
    public enum InputNetworkMode
    {
        /// <summary>No networking. Commands go straight to the local receiver.</summary>
        Offline,
        /// <summary>Server with local players. Local commands are processed directly (same as Offline).</summary>
        Host,
        /// <summary>Client. Replicated commands are sent to the server through an IInputNetworkBridge.</summary>
        Client,
        /// <summary>Dedicated server. No local players; receivers are fed by the network bridge.</summary>
        Server
    }

    /// <summary>
    /// Global settings and the registry of active players.
    /// </summary>
    public static class DikInput
    {
        static readonly List<DikInputPlayer> s_Players = new List<DikInputPlayer>();
        static InputNetworkMode s_NetworkMode = InputNetworkMode.Offline;

        /// <summary>Fired after a DikInputPlayer becomes active.</summary>
        public static event Action<DikInputPlayer> PlayerAdded;

        /// <summary>Fired after a DikInputPlayer is disabled or destroyed.</summary>
        public static event Action<DikInputPlayer> PlayerRemoved;

        /// <summary>Fired when <see cref="NetworkMode"/> changes.</summary>
        public static event Action<InputNetworkMode> NetworkModeChanged;

        /// <summary>
        /// Set this from your session or netcode bootstrap. Defaults to Offline.
        /// </summary>
        public static InputNetworkMode NetworkMode
        {
            get => s_NetworkMode;
            set
            {
                if (s_NetworkMode == value)
                    return;
                s_NetworkMode = value;
                NetworkModeChanged?.Invoke(value);
            }
        }

        /// <summary>True when Replicated commands must go to the server instead of the local receiver.</summary>
        public static bool SendsToServer => s_NetworkMode == InputNetworkMode.Client;

        /// <summary>
        /// Tick stamped on every command. Point this at your network tick (e.g. NetworkManager.LocalTime.Tick).
        /// </summary>
        public static Func<uint> TickSource { get; set; } = DefaultTick;

        /// <summary>Where binding overrides are persisted. Replace to use cloud saves, files, etc.</summary>
        public static IBindingOverrideStore BindingStore { get; set; } = new PlayerPrefsBindingOverrideStore();

        /// <summary>All enabled players.</summary>
        public static IReadOnlyList<DikInputPlayer> Players => s_Players;

        public static DikInputPlayer FindPlayer(int playerIndex)
        {
            for (var i = 0; i < s_Players.Count; i++)
            {
                if (s_Players[i].PlayerIndex == playerIndex)
                    return s_Players[i];
            }
            return null;
        }

        /// <summary>
        /// Enables or disables input on every player, optionally skipping one (e.g. the player who opened the pause menu).
        /// </summary>
        public static void SetInputEnabledForAll(bool enabled, DikInputPlayer except = null)
        {
            for (var i = 0; i < s_Players.Count; i++)
            {
                if (s_Players[i] != except)
                    s_Players[i].InputEnabled = enabled;
            }
        }

        internal static uint CurrentTick()
        {
            var source = TickSource;
            return source != null ? source() : DefaultTick();
        }

        internal static void Register(DikInputPlayer player)
        {
            if (s_Players.Contains(player))
                return;
            s_Players.Add(player);
            PlayerAdded?.Invoke(player);
        }

        internal static void Unregister(DikInputPlayer player)
        {
            if (!s_Players.Remove(player))
                return;
            PlayerRemoved?.Invoke(player);
        }

        static uint DefaultTick() => (uint)Time.frameCount;

        // Supports "Enter Play Mode Options" with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Players.Clear();
            s_NetworkMode = InputNetworkMode.Offline;
            TickSource = DefaultTick;
            BindingStore = new PlayerPrefsBindingOverrideStore();
            PlayerAdded = null;
            PlayerRemoved = null;
            NetworkModeChanged = null;
        }
    }
}
