using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
#if DIK_UGUI
using UnityEngine.InputSystem.UI;
#endif

namespace Dik.Input
{
    /// <summary>
    /// One local player. Sits on top of PlayerInput (which owns device pairing and control schemes) and adds:
    /// <list type="bullet">
    /// <item>UE5-style mapping contexts with priority and consumption (<see cref="AddContext"/>).</item>
    /// <item>Gameplay/UI isolation through modes (<see cref="SetMode"/>).</item>
    /// <item>Smooth device hot-swap: held input is released, contexts are re-applied, rebinds are kept, events are raised.</item>
    /// <item>Command routing: local receiver when offline or host, network bridge when client.</item>
    /// <item>Saved rebinding (<see cref="StartRebind"/>).</item>
    /// </list>
    /// Gameplay scripts should not talk to this component for actions; they bind on <see cref="DikInputReceiver"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    [AddComponentMenu("Dik/Input/Dik Input Player")]
    public sealed class DikInputPlayer : MonoBehaviour
    {
        [Serializable]
        public struct ContextEntry
        {
            public InputMappingContext context;
            [Tooltip("Use 'priority' instead of the context asset's priority.")]
            public bool overridePriority;
            public int priority;
        }

        [SerializeField] PlayerInput m_PlayerInput;

        [Tooltip("Where local commands are delivered. Usually on the same GameObject.")]
        [SerializeField] DikInputReceiver m_Receiver;

        [Tooltip("Contexts added when the player starts.")]
        [SerializeField] List<ContextEntry> m_DefaultContexts = new List<ContextEntry>();

        [SerializeField] InputMode m_StartMode = InputMode.GameOnly;

        [Header("Network")]
        [Tooltip("As a client, also deliver Replicated commands to the local receiver (client-side prediction).")]
        [SerializeField] bool m_PredictLocallyWhenClient;

        [Header("Binding Overrides")]
        [SerializeField] bool m_PersistBindingOverrides = true;
        [Tooltip("Saved per player index: <key>.p<index>.")]
        [SerializeField] string m_BindingSaveKey = "dik.input.bindings";

#if DIK_UGUI
        [Header("UI")]
        [Tooltip("Optional. Drives this UI module with this player's actions and devices (use a MultiplayerEventSystem for per-player UI).")]
        [SerializeField] InputSystemUIInputModule m_UIInputModule;
#endif

        readonly InputContextStack m_Contexts = new InputContextStack();
        readonly HashSet<InputAction> m_InProgress = new HashSet<InputAction>();
        readonly List<InputAction> m_ReleaseBuffer = new List<InputAction>();
        Action<InputAction.CallbackContext> m_ActionCallback;
        InputActionTable m_Table;
        InputActionAsset m_BoundActions;
        IInputNetworkBridge m_Bridge;
        InputMode m_Mode;
        DeviceKind m_DeviceKind;
        GamepadFamily m_GamepadFamily;
        string m_ControlScheme;
        bool m_Started;

        // ------------------------------------------------------------------ Events

        /// <summary>Control scheme or paired devices changed (keyboard to gamepad...). Refresh icons and prompts here.</summary>
        public event Action<DikInputPlayer> ControlsChanged;

        /// <summary>A paired device was unplugged. Consider pausing the game.</summary>
        public event Action<DikInputPlayer> DeviceLost;

        /// <summary>The player has a device again (same device reconnected, or auto-paired replacement).</summary>
        public event Action<DikInputPlayer> DeviceRegained;

        public event Action<DikInputPlayer, InputMode> ModeChanged;

        /// <summary>Every command produced by this player, before routing. Useful for recording or debugging.</summary>
        public event Action<DikInputPlayer, InputCommand> CommandProduced;

        // ------------------------------------------------------------------ Properties

        public PlayerInput PlayerInput => m_PlayerInput;

        /// <summary>This player's action asset (a per-player copy in local multiplayer).</summary>
        public InputActionAsset Actions => m_PlayerInput != null ? m_PlayerInput.actions : null;

        public InputActionTable ActionTable => m_Table;
        public InputContextStack Contexts => m_Contexts;
        public DikInputReceiver Receiver => m_Receiver;
        public int PlayerIndex => m_PlayerInput != null ? m_PlayerInput.playerIndex : -1;
        public InputMode Mode => m_Mode;
        public string CurrentControlScheme => m_ControlScheme;
        public DeviceKind CurrentDeviceKind => m_DeviceKind;
        public GamepadFamily CurrentGamepadFamily => m_GamepadFamily;
        public InputDevice PrimaryDevice => m_PlayerInput != null ? DeviceClassifier.GetPrimaryDevice(m_PlayerInput.devices) : null;

        /// <summary>True when the player has no usable device (unplugged, or none was available at spawn).</summary>
        public bool NeedsDevice => m_Started && m_PlayerInput != null && m_PlayerInput.user.valid &&
                                   (m_PlayerInput.devices.Count == 0 || m_PlayerInput.hasMissingRequiredDevices);

        /// <summary>Master switch for this player's input (e.g. players who did not open the pause menu).</summary>
        public bool InputEnabled
        {
            get => m_Contexts.Enabled;
            set => m_Contexts.Enabled = value;
        }

        public IInputNetworkBridge NetworkBridge
        {
            get => m_Bridge;
            set => m_Bridge = value;
        }

        public bool PredictLocallyWhenClient
        {
            get => m_PredictLocallyWhenClient;
            set => m_PredictLocallyWhenClient = value;
        }

#if DIK_UGUI
        /// <summary>Assign a UI module to drive it with this player's devices (e.g. the player who opened the menu).</summary>
        public InputSystemUIInputModule UIInputModule
        {
            get => m_UIInputModule;
            set
            {
                m_UIInputModule = value;
                if (m_Started)
                    AssignUIModule();
            }
        }
#endif

        // ------------------------------------------------------------------ Contexts and modes

        /// <summary>UE5 AddMappingContext. Re-adding updates the priority.</summary>
        public void AddContext(InputMappingContext context, int? priorityOverride = null) => m_Contexts.Add(context, priorityOverride);

        /// <summary>UE5 RemoveMappingContext.</summary>
        public bool RemoveContext(InputMappingContext context) => m_Contexts.Remove(context);

        public bool HasContext(InputMappingContext context) => m_Contexts.Contains(context);

        /// <summary>UE5 SetInputMode. Actions on layers that are no longer allowed are disabled and release their values.</summary>
        public void SetMode(InputMode mode)
        {
            var changed = m_Mode != mode;
            m_Mode = mode;
            m_Contexts.AllowedLayers = mode.ToLayers();
            if (changed)
                ModeChanged?.Invoke(this, mode);
        }

        /// <summary>Fine-grained alternative to <see cref="SetMode"/> for custom layers.</summary>
        public void SetAllowedLayers(InputLayer layers) => m_Contexts.AllowedLayers = layers;

        // ------------------------------------------------------------------ Unity lifecycle

        void Reset()
        {
            m_PlayerInput = GetComponent<PlayerInput>();
            m_Receiver = GetComponent<DikInputReceiver>();
        }

        void Awake()
        {
            if (m_PlayerInput == null)
                m_PlayerInput = GetComponent<PlayerInput>();
            if (m_Receiver == null)
                m_Receiver = GetComponent<DikInputReceiver>();
            if (m_Bridge == null)
                m_Bridge = GetComponent<IInputNetworkBridge>();

            m_ActionCallback = OnActionCallback;

            // Contexts decide which maps are enabled, so PlayerInput must not enable a default map,
            // and it must raise C# events for the device callbacks used below.
            m_PlayerInput.defaultActionMap = null;
            m_PlayerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            foreach (var entry in m_DefaultContexts)
            {
                if (entry.context != null)
                    m_Contexts.Add(entry.context, entry.overridePriority ? entry.priority : (int?)null);
            }

            m_Mode = m_StartMode;
            m_Contexts.AllowedLayers = m_StartMode.ToLayers();
        }

        // Start, not Awake: PlayerInput may replace its actions with a per-player copy in its own OnEnable.
        void Start()
        {
            m_Started = true;
            m_PlayerInput.onControlsChanged += OnControlsChanged;
            m_PlayerInput.onDeviceLost += OnDeviceLost;
            m_PlayerInput.onDeviceRegained += OnDeviceRegained;

            BindActions();
#if DIK_UGUI
            AssignUIModule();
#endif
            m_Contexts.Suspended = !isActiveAndEnabled;
            DeviceAutoPairing.Refresh();
        }

        void OnEnable()
        {
            DikInput.Register(this);
            if (m_Started)
            {
                BindActions();
                m_Contexts.Suspended = false;
            }
        }

        void OnDisable()
        {
            m_Contexts.Suspended = true;
            ReleaseHeldActions();
            DikInput.Unregister(this);
        }

        void OnDestroy()
        {
            if (m_PlayerInput != null)
            {
                m_PlayerInput.onControlsChanged -= OnControlsChanged;
                m_PlayerInput.onDeviceLost -= OnDeviceLost;
                m_PlayerInput.onDeviceRegained -= OnDeviceRegained;
            }
            UnbindActions();
        }

        // ------------------------------------------------------------------ Action wiring

        void BindActions()
        {
            var actions = Actions;
            if (actions == m_BoundActions)
                return;

            UnbindActions();
            if (actions == null)
            {
                Debug.LogError("[DikInput] PlayerInput has no Input Action Asset.", this);
                return;
            }

            m_BoundActions = actions;
            m_Table = new InputActionTable(actions);
            if (m_Receiver != null)
                m_Receiver.Initialize(m_Table);

            foreach (var map in actions.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    action.started += m_ActionCallback;
                    action.performed += m_ActionCallback;
                    action.canceled += m_ActionCallback;
                }
            }

            LoadBindingOverrides();
            m_Contexts.Actions = actions;
            UpdateDeviceInfo();
        }

        void UnbindActions()
        {
            if (m_BoundActions == null)
                return;

            foreach (var map in m_BoundActions.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    action.started -= m_ActionCallback;
                    action.performed -= m_ActionCallback;
                    action.canceled -= m_ActionCallback;
                }
            }
            m_BoundActions = null;
            m_InProgress.Clear();
        }

        void OnActionCallback(InputAction.CallbackContext context)
        {
            var trigger = InputCommand.ToTrigger(context.phase);
            if (trigger == InputTrigger.None || m_Table == null || !m_Table.TryGetIndex(context.action, out var index))
                return;

            if (trigger == InputTrigger.Canceled)
                m_InProgress.Remove(context.action);
            else
                m_InProgress.Add(context.action);

            Dispatch(context.action, InputCommand.FromContext(index, trigger, context, DikInput.CurrentTick()));
        }

        void Dispatch(InputAction action, in InputCommand command)
        {
            CommandProduced?.Invoke(this, command);

            if (DikInput.SendsToServer && m_Contexts.GetRouting(action) == InputRouting.Replicated)
            {
                if (m_Bridge == null || !m_Bridge.CanSend)
                    return;
                m_Bridge.SendToServer(command);
                if (!m_PredictLocallyWhenClient)
                    return;
            }

            if (m_Receiver != null)
                m_Receiver.Receive(command);
        }

        /// <summary>
        /// Sends a synthetic Canceled for actions still in progress, so nothing stays held when the device
        /// changes or disappears.
        /// </summary>
        void ReleaseHeldActions()
        {
            if (m_InProgress.Count == 0 || m_Table == null)
                return;

            m_ReleaseBuffer.Clear();
            m_ReleaseBuffer.AddRange(m_InProgress);
            m_InProgress.Clear();

            var tick = DikInput.CurrentTick();
            foreach (var action in m_ReleaseBuffer)
            {
                if (m_Table.TryGetIndex(action, out var index))
                    Dispatch(action, InputCommand.CreateRelease(index, InputCommand.GetValueKind(action), tick));
            }
        }

        // ------------------------------------------------------------------ Devices

        void OnControlsChanged(PlayerInput playerInput)
        {
            // Unity cancels actions whose controls went away before this callback; anything left is stuck.
            ReleaseHeldActions();
            // Consumption is computed from resolved controls, which depend on the paired devices.
            m_Contexts.Apply();
            UpdateDeviceInfo();
            DeviceAutoPairing.Refresh();
            ControlsChanged?.Invoke(this);
        }

        void OnDeviceLost(PlayerInput playerInput)
        {
            ReleaseHeldActions();
            UpdateDeviceInfo();
            DeviceAutoPairing.Refresh();
            DeviceLost?.Invoke(this);
        }

        void OnDeviceRegained(PlayerInput playerInput)
        {
            m_Contexts.Apply();
            UpdateDeviceInfo();
            DeviceAutoPairing.Refresh();
            DeviceRegained?.Invoke(this);
        }

        void UpdateDeviceInfo()
        {
            m_ControlScheme = m_PlayerInput.currentControlScheme;
            var device = PrimaryDevice;
            m_DeviceKind = DeviceClassifier.GetKind(device);
            m_GamepadFamily = DeviceClassifier.GetGamepadFamily(device);
        }

        /// <summary>
        /// Pairs <paramref name="device"/> (plus any other free devices its control scheme needs, e.g. a mouse with
        /// the keyboard) and switches to that scheme. Used by <see cref="DeviceAutoPairing"/>; can be called manually.
        /// </summary>
        public bool TryPairDevice(InputDevice device)
        {
            var actions = Actions;
            if (device == null || actions == null || m_PlayerInput == null)
                return false;

            var owner = InputUser.FindUserPairedToDevice(device);
            if (owner != null && owner.Value != m_PlayerInput.user)
                return false;

            if (actions.controlSchemes.Count == 0)
            {
                InputUser.PerformPairingWithDevice(device, m_PlayerInput.user);
                RaiseRegainedAfterManualPair();
                return true;
            }

            var scheme = InputControlScheme.FindControlSchemeForDevice(device, actions.controlSchemes);
            if (!scheme.HasValue)
                return false;

            var freeDevices = new List<InputDevice>();
            foreach (var candidate in InputSystem.devices)
            {
                if (candidate == device || InputUser.FindUserPairedToDevice(candidate) == null)
                    freeDevices.Add(candidate);
            }

            using (var match = scheme.Value.PickDevicesFrom(freeDevices, device))
            {
                if (!match.isSuccessfulMatch)
                    return false;

                var devices = new InputDevice[match.devices.Count];
                for (var i = 0; i < devices.Length; i++)
                    devices[i] = match.devices[i];
                m_PlayerInput.SwitchCurrentControlScheme(scheme.Value.name, devices);
            }

            RaiseRegainedAfterManualPair();
            return true;
        }

        void RaiseRegainedAfterManualPair()
        {
            m_Contexts.Apply();
            UpdateDeviceInfo();
            DeviceRegained?.Invoke(this);
        }

        // ------------------------------------------------------------------ UI

#if DIK_UGUI
        void AssignUIModule()
        {
            if (m_UIInputModule == null)
                return;
            // PlayerInput points the module at this player's actions (resolving the per-player copy).
            m_PlayerInput.uiInputModule = m_UIInputModule;
            // The module may enable its actions on assignment; re-apply so the mode still decides.
            m_Contexts.Apply();
        }
#endif

        // ------------------------------------------------------------------ Rebinding

        /// <summary>
        /// Interactive rebind for this player's copy of <paramref name="action"/>, restricted to the current control
        /// scheme. Saves overrides on completion.
        /// </summary>
        /// <param name="bindingIndex">-1 picks the binding of the current control scheme.</param>
        public RebindSession StartRebind(InputActionReference action, int bindingIndex = -1, Action<RebindResult> onFinished = null)
        {
            return StartRebind(action != null ? action.action : null, bindingIndex, onFinished);
        }

        public RebindSession StartRebind(InputAction action, int bindingIndex = -1, Action<RebindResult> onFinished = null)
        {
            var resolved = ResolveAction(action);
            if (resolved == null)
                return null;

            if (bindingIndex < 0)
                bindingIndex = DikRebind.FindBindingIndex(resolved, m_ControlScheme);
            if (bindingIndex < 0)
                return null;

            InputControlScheme? scheme = null;
            if (!string.IsNullOrEmpty(m_ControlScheme))
                scheme = Actions.FindControlScheme(m_ControlScheme);

            return DikRebind.Start(resolved, bindingIndex, result =>
            {
                m_Contexts.Apply();
                if (result == RebindResult.Completed)
                    SaveBindingOverrides();
                onFinished?.Invoke(result);
            }, scheme);
        }

        /// <summary>Finds this player's copy of an action from the source asset (or InputActionReference.action).</summary>
        public InputAction ResolveAction(InputAction action)
        {
            var actions = Actions;
            return action == null || actions == null ? null : actions.FindAction(action.id);
        }

        public InputAction ResolveAction(InputActionReference reference) => ResolveAction(reference != null ? reference.action : null);

        string BindingKey => $"{m_BindingSaveKey}.p{Mathf.Max(0, PlayerIndex)}";

        public void SaveBindingOverrides()
        {
            var actions = Actions;
            if (!m_PersistBindingOverrides || actions == null || DikInput.BindingStore == null)
                return;
            DikInput.BindingStore.Save(BindingKey, actions.SaveBindingOverridesAsJson());
        }

        public void LoadBindingOverrides()
        {
            var actions = Actions;
            if (!m_PersistBindingOverrides || actions == null || DikInput.BindingStore == null)
                return;
            var json = DikInput.BindingStore.Load(BindingKey);
            if (!string.IsNullOrEmpty(json))
                actions.LoadBindingOverridesFromJson(json);
        }

        public void ResetBindingOverrides()
        {
            var actions = Actions;
            if (actions == null)
                return;
            actions.RemoveAllBindingOverrides();
            if (m_PersistBindingOverrides && DikInput.BindingStore != null)
                DikInput.BindingStore.Delete(BindingKey);
            m_Contexts.Apply();
        }
    }
}
